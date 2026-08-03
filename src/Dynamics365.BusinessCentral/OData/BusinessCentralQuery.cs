using Dynamics365.BusinessCentral.Errors;
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
    private readonly IBusinessCentralQueryExecutor _executor;
    private readonly string _path;

    private readonly List<string> _orderBy = [];
    private readonly List<string> _expand = [];
    private readonly List<string> _select = [];

    private ODataFilter? _filter;
    private bool _selectAll;
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

    public IBusinessCentralQuery<TEntity> Where(Func<IFilterBuilder<TEntity>, ODataFilter> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        return Where(build(FilterBuilder<TEntity>.Instance));
    }

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
        _selectAll = false;

        foreach (var field in fields)
        {
            var name = PropertyPath.Resolve(field);
            if (!_select.Contains(name))
                _select.Add(name);
        }

        return this;
    }

    public IBusinessCentralQuery<TEntity> SelectAll()
    {
        // Mutually exclusive with Select(...): the last call wins, so the builder's state
        // is always one of explicit / all / derived — never an ambiguous mix.
        _selectAll = true;
        _select.Clear();
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
        foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f) && !_expand.Contains(f)))
            _expand.Add(field);

        return this;
    }

    public IBusinessCentralQuery<TEntity> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _top = count;
        return this;
    }

    public IBusinessCentralQuery<TEntity> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _skip = count;
        return this;
    }

    public IBusinessCentralQuery<TEntity> PageSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        _pageSize = size;
        return this;
    }

    public async Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var page = await ExecutePageAsync(BuildOptions(), SelectOrNull, null, cancellationToken)
            .ConfigureAwait(false);

        return page.Value;
    }

    public async Task<BusinessCentralPage<TEntity>> ToPageAsync(CancellationToken cancellationToken = default)
    {
        var options = BuildOptions();
        options.WithCount();

        var page = await ExecutePageAsync(options, SelectOrNull, null, cancellationToken)
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
        // The paging state machine lives in QueryPager, shared with the path-based
        // QueryStreamAsync; only the fetch delegates differ. Paging is server-driven: the
        // per-query PageSize (else the registration-level MaxPageSize, else nothing) is
        // sent as Prefer: odata.maxpagesize and continuation follows @odata.nextLink.
        var maxPageSize = _pageSize ?? _executor.DefaultMaxPageSize;

        var stream = QueryPager.StreamAsync(
            _top,
            _skip ?? 0,
            (top, skip, ct) => FetchAsync(top, skip, maxPageSize, ct),
            (link, ct) => _executor.FetchNextPageAsync<TEntity>(link, maxPageSize, ct),
            cancellationToken);

        await foreach (var entity in stream.ConfigureAwait(false))
            yield return entity;
    }

    private Task<ODataResponse<TEntity>> FetchAsync(
        int? top,
        int skip,
        int? maxPageSize,
        CancellationToken cancellationToken)
    {
        var options = BuildOptions();
        options.Top = top;
        options.Skip = skip == 0 ? null : skip;

        return ExecutePageAsync(options, SelectOrNull, maxPageSize, cancellationToken);
    }

    public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var options = BuildOptions();
        options.Top = 1;

        var page = await ExecutePageAsync(options, SelectOrNull, null, cancellationToken)
            .ConfigureAwait(false);

        return page.Value.Count == 0 ? default : page.Value[0];
    }

    /// <summary>
    /// Runs one page fetch, adding the derived-<c>$select</c> explanation to a <c>400</c>
    /// when this query is using one.
    /// </summary>
    /// <remarks>
    /// Only the first request of a stream needs this: a continuation replays the same
    /// projection, so a projection the server rejects has already failed here. Continuations
    /// are also sent as the server's verbatim <c>nextLink</c>, which the builder never sees.
    /// </remarks>
    private async Task<ODataResponse<TEntity>> ExecutePageAsync(
        QueryOptions options,
        IEnumerable<string>? select,
        int? maxPageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _executor
                .FetchPageAsync<TEntity>(_path, FilterValue, options, select, maxPageSize, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BusinessCentralValidationException ex) when (UsesDerivedSelect && select is not null)
        {
            throw DerivedSelectHint.Decorate<TEntity>(ex, EntitySelect.For<TEntity>());
        }
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var options = BuildOptions();
        options.WithCount();
        options.Top = 0;

        // A count query returns no rows, so it needs no column list — skip the derived
        // $select rather than sending a pointless projection.
        var page = await ExecutePageAsync(options, select: null, maxPageSize: null, cancellationToken)
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

    /// <summary>
    /// Whether the projection this query sends came from <see cref="EntitySelect"/> rather
    /// than from the caller — the condition under which a <c>400</c> is worth explaining.
    /// </summary>
    private bool UsesDerivedSelect => _select.Count == 0 && !_selectAll;

    /// <summary>
    /// Explicit <c>Select(...)</c> wins; <c>SelectAll()</c> suppresses; otherwise the
    /// projection is derived from the entity type (<see cref="EntitySelect"/>).
    /// </summary>
    private IEnumerable<string>? SelectOrNull =>
        _select.Count > 0 ? _select
        : _selectAll ? null
        : EntitySelect.For<TEntity>() is { Length: > 0 } derived ? derived : null;

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
