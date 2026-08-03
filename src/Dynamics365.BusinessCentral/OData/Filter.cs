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
    /// Creates a filter matching any of <paramref name="values"/>, rendered as a chain of
    /// same-field equalities: <c>(field eq v1) or (field eq v2) or ...</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> the OData <c>in</c> operator: Business Central only accepts
    /// <c>field in (...)</c> with <c>$schemaversion=2.1</c> and answers
    /// <c>BadRequest_MethodNotImplemented</c> without it — verified against a live tenant.
    /// A same-field <c>or</c>-chain is explicitly supported on every schema version and is
    /// semantically identical. The URL grows with the value count; chunk large key sets.
    /// </para>
    /// <para>
    /// An empty <paramref name="values"/> produces a filter that matches nothing
    /// (<c>false</c>), so <c>Filter.In(field, ids)</c> is safe when <c>ids</c> turns out
    /// to be empty.
    /// </para>
    /// </remarks>
    public static ODataFilter In(string field, params object[] values)
    {
        if (values is null || values.Length == 0)
            return None;

        if (values.Length == 1)
            return Equals(field, values[0]);

        return new ODataFilter(
            string.Join(" or ", values.Select(v => $"({field} eq {Format(v)})")));
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

    /// <summary>
    /// Creates a membership filter using an explicitly chosen rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>or</c>-chain is the default because Business Central's <c>in</c> operator is
    /// gated on schema version: Microsoft documents it as working <i>only</i> in
    /// <c>$schemaversion=2.1</c>, and a live tenant on an earlier version answered
    /// <c>BadRequest_MethodNotImplemented</c>. A deployment that <i>is</i> on 2.1 pays about
    /// <b>twice</b> the encoded URL length per key for a workaround it does not need — and
    /// reaches <c>BusinessCentralOptions.MaxUrlLength</c> twice as fast.
    /// </para>
    /// <para>
    /// Pass <see cref="ODataInStyle.Native"/> to emit <c>field in (v1,v2,…)</c> instead. This
    /// requires <c>BusinessCentralOptions.SchemaVersion = "2.1"</c> — the two are a pair, and
    /// a native <c>in</c> without it is a request the server will reject. Empty and
    /// single-value collections behave as they do for the default rendering.
    /// </para>
    /// </remarks>
    /// <param name="field">Wire name of the field to match.</param>
    /// <param name="values">Values to match against.</param>
    /// <param name="style">How to render the membership test.</param>
    public static ODataFilter In(string field, IEnumerable<object> values, ODataInStyle style)
    {
        var array = values?.ToArray() ?? [];

        if (style != ODataInStyle.Native || array.Length == 0)
            return In(field, array);

        // A single value gains nothing from 'in' and 'eq' is accepted everywhere, so the
        // collapse applies to both renderings.
        if (array.Length == 1)
            return Equals(field, array[0]);

        return new ODataFilter($"{field} in ({string.Join(",", array.Select(Format))})");
    }

    /// <inheritdoc cref="In(string, IEnumerable{object}, ODataInStyle)"/>
    public static ODataFilter In<TEntity>(
        Expression<Func<TEntity, object?>> field,
        IEnumerable<object> values,
        ODataInStyle style) =>
        In(PropertyPath.Resolve(field), values, style);

    /// <summary>Creates a filter of the form: field eq null</summary>
    /// <remarks>
    /// On Business Central text fields this means <b>"null or blank"</b>, not just null:
    /// AL text fields cannot be null — an unset field is an empty string — and BC's OData
    /// layer maps <c>eq null</c> onto "is blank". Verified against a live tenant: an item
    /// whose field serialises as <c>""</c> matches <c>eq null</c>. Differs from the
    /// equivalent LINQ predicate, which would not match an empty string.
    /// </remarks>
    public static ODataFilter IsNull(string field) =>
        new($"{field} eq null");

    /// <inheritdoc cref="IsNull(string)"/>
    public static ODataFilter IsNull<TEntity>(Expression<Func<TEntity, object?>> field) =>
        IsNull(PropertyPath.Resolve(field));

    /// <summary>Creates a filter of the form: field ne null</summary>
    /// <remarks>
    /// The complement of <see cref="IsNull(string)"/>: on Business Central text fields this
    /// <b>excludes blank strings</b>, because BC treats an unset text field (an empty
    /// string) as null. "Has any value including empty" cannot be expressed with this
    /// filter — it is "has a non-blank value".
    /// </remarks>
    public static ODataFilter IsNotNull(string field) =>
        new($"{field} ne null");

    /// <inheritdoc cref="IsNotNull(string)"/>
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
