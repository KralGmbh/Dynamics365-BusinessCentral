using System.Globalization;
using System.Linq.Expressions;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Factory methods for creating OData filter expressions.
/// </summary>
/// <remarks>
/// Every method has a string overload and a typed overload taking a property selector.
/// Prefer the typed form — it survives renames and guarantees the field name matches how
/// the entity is deserialized:
/// <code>
/// Filter.Equals&lt;SalesOrder&gt;(o =&gt; o.Status, "Open")
///       .And(Filter.GreaterThan&lt;SalesOrder&gt;(o =&gt; o.Amount, 100));
/// </code>
/// </remarks>
public static class Filter
{
    /// <summary>Creates a filter of the form: field eq value</summary>
    public static ODataFilter Equals(string field, object? value) =>
        new($"{field} eq {Format(value)}");

    /// <summary>Creates a filter of the form: field eq value</summary>
    public static ODataFilter Equals<TEntity>(Expression<Func<TEntity, object?>> field, object? value) =>
        Equals(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter of the form: field ne value</summary>
    public static ODataFilter NotEquals(string field, object? value) =>
        new($"{field} ne {Format(value)}");

    /// <summary>Creates a filter of the form: field ne value</summary>
    public static ODataFilter NotEquals<TEntity>(Expression<Func<TEntity, object?>> field, object? value) =>
        NotEquals(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter of the form: field gt value</summary>
    public static ODataFilter GreaterThan(string field, object value) =>
        new($"{field} gt {Format(value)}");

    /// <summary>Creates a filter of the form: field gt value</summary>
    public static ODataFilter GreaterThan<TEntity>(Expression<Func<TEntity, object?>> field, object value) =>
        GreaterThan(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter of the form: field ge value</summary>
    public static ODataFilter GreaterOrEqual(string field, object value) =>
        new($"{field} ge {Format(value)}");

    /// <summary>Creates a filter of the form: field ge value</summary>
    public static ODataFilter GreaterOrEqual<TEntity>(Expression<Func<TEntity, object?>> field, object value) =>
        GreaterOrEqual(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter of the form: field lt value</summary>
    public static ODataFilter LessThan(string field, object value) =>
        new($"{field} lt {Format(value)}");

    /// <summary>Creates a filter of the form: field lt value</summary>
    public static ODataFilter LessThan<TEntity>(Expression<Func<TEntity, object?>> field, object value) =>
        LessThan(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter of the form: field le value</summary>
    public static ODataFilter LessOrEqual(string field, object value) =>
        new($"{field} le {Format(value)}");

    /// <summary>Creates a filter of the form: field le value</summary>
    public static ODataFilter LessOrEqual<TEntity>(Expression<Func<TEntity, object?>> field, object value) =>
        LessOrEqual(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter using the contains(...) function.</summary>
    public static ODataFilter Contains(string field, string value) =>
        new($"contains({field}, {Format(value)})");

    /// <summary>Creates a filter using the contains(...) function.</summary>
    public static ODataFilter Contains<TEntity>(Expression<Func<TEntity, object?>> field, string value) =>
        Contains(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter using the startswith(...) function.</summary>
    public static ODataFilter StartsWith(string field, string value) =>
        new($"startswith({field}, {Format(value)})");

    /// <summary>Creates a filter using the startswith(...) function.</summary>
    public static ODataFilter StartsWith<TEntity>(Expression<Func<TEntity, object?>> field, string value) =>
        StartsWith(PropertyPath.Resolve(field), value);

    /// <summary>Creates a filter using the endswith(...) function.</summary>
    public static ODataFilter EndsWith(string field, string value) =>
        new($"endswith({field}, {Format(value)})");

    /// <summary>Creates a filter using the endswith(...) function.</summary>
    public static ODataFilter EndsWith<TEntity>(Expression<Func<TEntity, object?>> field, string value) =>
        EndsWith(PropertyPath.Resolve(field), value);

    /// <summary>
    /// Creates a filter of the form: field in (value1,value2,...).
    /// </summary>
    /// <remarks>
    /// An empty <paramref name="values"/> produces a filter that matches nothing
    /// (<c>false</c>) rather than the invalid OData expression <c>field in ()</c>. This
    /// makes <c>Filter.In(field, ids)</c> safe when <c>ids</c> turns out to be empty.
    /// </remarks>
    public static ODataFilter In(string field, params object[] values)
    {
        if (values is null || values.Length == 0)
            return new ODataFilter("false");

        return new ODataFilter($"{field} in ({string.Join(",", values.Select(Format))})");
    }

    /// <inheritdoc cref="In(string, object[])"/>
    public static ODataFilter In(string field, IEnumerable<object> values) =>
        In(field, values?.ToArray() ?? []);

    /// <inheritdoc cref="In(string, object[])"/>
    public static ODataFilter In<TEntity>(Expression<Func<TEntity, object?>> field, params object[] values) =>
        In(PropertyPath.Resolve(field), values);

    /// <inheritdoc cref="In(string, object[])"/>
    public static ODataFilter In<TEntity>(Expression<Func<TEntity, object?>> field, IEnumerable<object> values) =>
        In(PropertyPath.Resolve(field), values);

    /// <summary>Creates a filter of the form: field eq null</summary>
    public static ODataFilter IsNull(string field) =>
        new($"{field} eq null");

    /// <summary>Creates a filter of the form: field eq null</summary>
    public static ODataFilter IsNull<TEntity>(Expression<Func<TEntity, object?>> field) =>
        IsNull(PropertyPath.Resolve(field));

    /// <summary>Creates a filter of the form: field ne null</summary>
    public static ODataFilter IsNotNull(string field) =>
        new($"{field} ne null");

    /// <summary>Creates a filter of the form: field ne null</summary>
    public static ODataFilter IsNotNull<TEntity>(Expression<Func<TEntity, object?>> field) =>
        IsNotNull(PropertyPath.Resolve(field));

    /// <summary>A filter that matches every row. Emitted as no <c>$filter</c> at all.</summary>
    public static ODataFilter All => new(ODataFilter.MatchAll);

    /// <summary>A filter that matches nothing.</summary>
    public static ODataFilter None => new("false");

    private static string Format(object? value) =>
        value switch
        {
            null => "null",
            string s => $"'{s.Replace("'", "''")}'",
            DateTime dt => FormatDateTime(dt),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
            // Edm.Date / Edm.TimeOfDay literals. Without these both fall through to
            // Convert.ToString, whose culture-formatted output ("07/29/2026") Business
            // Central rejects.
            DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("O", CultureInfo.InvariantCulture),
            bool b => b.ToString().ToLowerInvariant(),
            Guid g => g.ToString(),
            Enum e => $"'{e}'",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };

    /// <summary>
    /// Formats a <see cref="DateTime"/> as a UTC OData literal.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeKind.Unspecified"/> is taken to already be UTC rather than local:
    /// <see cref="DateTime.ToUniversalTime"/> would assume local and shift the value by the
    /// machine's timezone, making the filter depend on where the code happens to run.
    /// Business Central stores datetimes in UTC, so a kindless value — parsed from config,
    /// loaded from a database — almost always is UTC already.
    /// </remarks>
    private static string FormatDateTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

        return utc.ToString("O");
    }
}
