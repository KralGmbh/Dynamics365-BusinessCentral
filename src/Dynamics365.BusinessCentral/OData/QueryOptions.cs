namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Optional OData query modifiers: <c>$top</c>, <c>$skip</c>, <c>$orderby</c>,
/// <c>$expand</c> and <c>$count</c>.
/// </summary>
public sealed class QueryOptions
{
    private readonly List<string> _orderBy = [];
    private readonly List<string> _expand = [];

    /// <summary>Maximum number of entities to return (<c>$top</c>).</summary>
    public int? Top { get; internal set; }

    /// <summary>Number of entities to skip (<c>$skip</c>).</summary>
    public int? Skip { get; internal set; }

    /// <summary>
    /// Maximum rows per page when auto-paging, requested from the server via
    /// <c>Prefer: odata.maxpagesize</c>. This is <b>not</b> a result limit — use
    /// <see cref="WithTop"/> for that. When unset, the server pages at its own configured
    /// Max Page Size (or the registration-level <c>BusinessCentralOptions.MaxPageSize</c>,
    /// when that is set).
    /// </summary>
    public int? PageSize { get; internal set; }

    /// <summary>Whether to request a total count (<c>$count=true</c>).</summary>
    public bool IncludeCount { get; internal set; }

    /// <summary>Navigation properties to expand (<c>$expand</c>).</summary>
    public IReadOnlyList<string> Expand => _expand;

    /// <summary>
    /// The composed <c>$orderby</c> clause, or <see langword="null"/> when no ordering was set.
    /// </summary>
    public string? OrderBy
    {
        get => _orderBy.Count == 0 ? null : string.Join(",", _orderBy);
        internal set
        {
            _orderBy.Clear();
            if (!string.IsNullOrWhiteSpace(value))
                _orderBy.Add(value);
        }
    }

    /// <summary>Limits the result to <paramref name="value"/> entities (<c>$top</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public QueryOptions WithTop(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        Top = value;
        return this;
    }

    /// <summary>Skips <paramref name="value"/> entities (<c>$skip</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public QueryOptions WithSkip(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        Skip = value;
        return this;
    }

    /// <summary>
    /// Requests at most <paramref name="value"/> rows per page when auto-paging, via
    /// <c>Prefer: odata.maxpagesize</c>. Affects the number of round trips and per-response
    /// size, not how many entities come back. The server clamps the value to its own
    /// configured maximum, so this can only ask for <i>smaller</i> pages.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is less than 1.
    /// </exception>
    public QueryOptions WithPageSize(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);

        PageSize = value;
        return this;
    }

    /// <summary>Requests a total count alongside the page (<c>$count=true</c>).</summary>
    public QueryOptions WithCount(bool include = true)
    {
        IncludeCount = include;
        return this;
    }

    /// <summary>Expands the given navigation properties (<c>$expand</c>).</summary>
    public QueryOptions WithExpand(params string[] fields)
    {
        foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f) && !_expand.Contains(f)))
            _expand.Add(field);

        return this;
    }

    /// <summary>
    /// Orders ascending by <paramref name="field"/>, <b>replacing</b> any ordering set so far.
    /// Use <see cref="ThenByAsc"/> to add a secondary key.
    /// </summary>
    public QueryOptions OrderByAsc(string field)
    {
        _orderBy.Clear();
        return ThenByAsc(field);
    }

    /// <summary>
    /// Orders descending by <paramref name="field"/>, <b>replacing</b> any ordering set so far.
    /// Use <see cref="ThenByDesc"/> to add a secondary key.
    /// </summary>
    public QueryOptions OrderByDesc(string field)
    {
        _orderBy.Clear();
        return ThenByDesc(field);
    }

    /// <summary>Appends an ascending sort key, keeping the existing ordering.</summary>
    public QueryOptions ThenByAsc(string field) =>
        string.IsNullOrWhiteSpace(field) ? this : AppendOrderByClause($"{field} asc");

    /// <summary>Appends a descending sort key, keeping the existing ordering.</summary>
    public QueryOptions ThenByDesc(string field) =>
        string.IsNullOrWhiteSpace(field) ? this : AppendOrderByClause($"{field} desc");

    /// <summary>Appends an already-formatted clause such as <c>"no asc"</c>.</summary>
    internal QueryOptions AppendOrderByClause(string clause)
    {
        if (!string.IsNullOrWhiteSpace(clause))
            _orderBy.Add(clause);

        return this;
    }
}
