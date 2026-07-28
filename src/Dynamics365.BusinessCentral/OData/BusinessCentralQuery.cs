using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Default <see cref="IBusinessCentralQuery{TEntity}"/> implementation.
/// </summary>
/// <remarks>
/// Builder state is kept as primitives and materialised into a fresh
/// <see cref="QueryOptions"/> per execution, so terminal operators that need to adjust
/// paging — <c>FirstOrDefaultAsync</c>, <c>CountAsync</c> — do not mutate the builder.
/// </remarks>
internal sealed class BusinessCentralQuery<TEntity> : IBusinessCentralQuery<TEntity>
{
    private const int DefaultPageSize = 1000;

    private readonly IBusinessCentralQueryExecutor _executor;
    private readonly string _path;

    private readonly List<string> _orderBy = [];
    private readonly List<string> _expand = [];
    private readonly List<string> _select = [];

    private ODataFilter? _filter;
    private int? _top;
    private int? _skip;
    private int? _pageSize;

    public BusinessCentralQuery(IBusinessCentralQueryExecutor executor, string path)
    {
        _executor = executor;
        _path = path;
    }

    public IBusinessCentralQuery<TEntity> Where(ODataFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filter = _filter is null ? filter : _filter.And(filter);
        return this;
    }

    public IBusinessCentralQuery<TEntity> Where(string filter) =>
        string.IsNullOrWhiteSpace(filter) ? this : Where(new ODataFilter(filter));

    public IBusinessCentralQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> field)
    {
        _orderBy.Clear();
        return ThenBy(field);
    }

    public IBusinessCentralQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> field)
    {
        _orderBy.Clear();
        return ThenByDescending(field);
    }

    public IBusinessCentralQuery<TEntity> ThenBy(Expression<Func<TEntity, object?>> field)
    {
        _orderBy.Add($"{PropertyPath.Resolve(field)} asc");
        return this;
    }

    public IBusinessCentralQuery<TEntity> ThenByDescending(Expression<Func<TEntity, object?>> field)
    {
        _orderBy.Add($"{PropertyPath.Resolve(field)} desc");
        return this;
    }

    public IBusinessCentralQuery<TEntity> Select(params Expression<Func<TEntity, object?>>[] fields)
    {
        foreach (var field in fields)
        {
            var name = PropertyPath.Resolve(field);
            if (!_select.Contains(name))
                _select.Add(name);
        }

        return this;
    }

    public IBusinessCentralQuery<TEntity> Expand(params Expression<Func<TEntity, object?>>[] fields)
    {
        foreach (var field in fields)
        {
            var name = PropertyPath.Resolve(field);
            if (!_expand.Contains(name))
                _expand.Add(name);
        }

        return this;
    }

    public IBusinessCentralQuery<TEntity> Expand(params string[] fields)
    {
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field) && !_expand.Contains(field))
                _expand.Add(field);
        }

        return this;
    }

    public IBusinessCentralQuery<TEntity> Top(int count)
    {
        _top = count;
        return this;
    }

    public IBusinessCentralQuery<TEntity> Skip(int count)
    {
        _skip = count;
        return this;
    }

    public IBusinessCentralQuery<TEntity> PageSize(int size)
    {
        _pageSize = size;
        return this;
    }

    public async Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var page = await _executor
            .FetchPageAsync<TEntity>(_path, FilterValue, BuildOptions(), SelectOrNull, cancellationToken)
            .ConfigureAwait(false);

        return page.Value;
    }

    public async Task<BusinessCentralPage<TEntity>> ToPageAsync(CancellationToken cancellationToken = default)
    {
        var options = BuildOptions();
        options.WithCount();

        var page = await _executor
            .FetchPageAsync<TEntity>(_path, FilterValue, options, SelectOrNull, cancellationToken)
            .ConfigureAwait(false);

        return new BusinessCentralPage<TEntity>(page.Value, page.Count, page.NextLink);
    }

    public async Task<List<TEntity>> ToAllAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<TEntity>();

        await foreach (var entity in StreamAsync(cancellationToken).ConfigureAwait(false))
            all.Add(entity);

        return all;
    }

    public async IAsyncEnumerable<TEntity> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pageSize = _pageSize ?? DefaultPageSize;
        var limit = _top;
        var skip = _skip ?? 0;
        var emitted = 0;

        var requested = NextTop(pageSize, limit, emitted);
        var page = await FetchAsync(requested, skip, cancellationToken).ConfigureAwait(false);

        // True once the server started driving paging via @odata.nextLink, at which point
        // it — not our $top — decides where the collection ends.
        var serverDriven = false;

        while (true)
        {
            var inPage = 0;

            foreach (var entity in page.Value)
            {
                yield return entity;

                emitted++;
                inPage++;

                if (limit is { } cap && emitted >= cap)
                    yield break;
            }

            if (!string.IsNullOrWhiteSpace(page.NextLink))
            {
                serverDriven = true;

                page = await _executor
                    .FetchNextPageAsync<TEntity>(page.NextLink!, cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            // The server was paging and stopped offering a nextLink: nothing left.
            if (serverDriven)
                yield break;

            // No nextLink and a short page means the collection is exhausted.
            if (inPage < requested)
                yield break;

            skip += inPage;
            requested = NextTop(pageSize, limit, emitted);

            page = await FetchAsync(requested, skip, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<ODataResponse<TEntity>> FetchAsync(int top, int skip, CancellationToken cancellationToken)
    {
        var options = BuildOptions();
        options.Top = top;
        options.Skip = skip;

        return _executor.FetchPageAsync<TEntity>(_path, FilterValue, options, SelectOrNull, cancellationToken);
    }

    /// <summary>Page size for the next request, never overshooting a caller-set <c>$top</c>.</summary>
    private static int NextTop(int pageSize, int? limit, int emitted)
    {
        if (limit is not { } cap)
            return pageSize;

        var remaining = cap - emitted;
        return remaining < pageSize ? remaining : pageSize;
    }

    public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var options = BuildOptions();
        options.Top = 1;

        var page = await _executor
            .FetchPageAsync<TEntity>(_path, FilterValue, options, SelectOrNull, cancellationToken)
            .ConfigureAwait(false);

        return page.Value.Count == 0 ? default : page.Value[0];
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var options = BuildOptions();
        options.WithCount();
        options.Top = 0;

        var page = await _executor
            .FetchPageAsync<TEntity>(_path, FilterValue, options, SelectOrNull, cancellationToken)
            .ConfigureAwait(false);

        if (page.Count is { } count)
            return count;

        // Endpoint ignored $count — fall back to walking the collection.
        var walked = 0L;

        await foreach (var _ in StreamAsync(cancellationToken).ConfigureAwait(false))
            walked++;

        return walked;
    }

    private string FilterValue => _filter?.Value ?? string.Empty;

    private IEnumerable<string>? SelectOrNull => _select.Count == 0 ? null : _select;

    private QueryOptions BuildOptions()
    {
        var options = new QueryOptions
        {
            Top = _top,
            Skip = _skip,
            PageSize = _pageSize
        };

        foreach (var clause in _orderBy)
            options.AppendOrderByClause(clause);

        if (_expand.Count > 0)
            options.WithExpand([.. _expand]);

        return options;
    }
}
