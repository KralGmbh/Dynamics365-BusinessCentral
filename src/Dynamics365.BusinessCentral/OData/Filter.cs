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
    /// Creates a filter matching any of <paramref name="values"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rendering is decided by the client, not here (<see cref="ODataInStyle.Auto"/>): a
    /// configured <c>BusinessCentralOptions.SchemaVersion</c> of 2.1 or later emits
    /// <c>field in (v1,v2,…)</c>, and anything else emits the portable same-field
    /// <c>or</c>-chain <c>(field eq v1) or (field eq v2) …</c>. Business Central rejects
    /// <c>in</c> below schema version 2.1 with HTTP <c>501</c> carrying the OData error code
    /// <c>BadRequest_MethodNotImplemented</c> — both measured against a live tenant, in
    /// separate rounds. The <c>BadRequest_</c> prefix is Business Central's naming, not the
    /// status: this is a <c>501</c>, so it surfaces as
    /// <c>BusinessCentralServerException</c> and not as a validation error. The two renderings
    /// return identical rows where both work, also verified live.
    /// </para>
    /// <para>
    /// The decision is deferred until the request URL is built, so composing with
    /// <c>.And(...)</c> keeps it. Reading <see cref="ODataFilter.Value"/> directly yields the
    /// <c>or</c>-chain, since a bare value has no endpoint to ask. Pass an explicit
    /// <see cref="ODataInStyle"/> to pin one rendering.
    /// </para>
    /// <para>
    /// An empty <paramref name="values"/> produces <see cref="None"/>, which the client answers
    /// with an empty result and <b>no request at all</b> — Business Central has no
    /// boolean-literal filter to express "match nothing" with. So <c>Filter.In(field, ids)</c>
    /// is safe when <c>ids</c> turns out to be empty, and cheap. A single value collapses to a
    /// plain <c>eq</c>, which every version accepts and which is shorter than either list form.
    /// </para>
    /// </remarks>
    public static ODataFilter In(string field, params object[] values) =>
        In(field, values, ODataInStyle.Auto);

    /// <inheritdoc cref="In(string, object[])"/>
    public static ODataFilter In(string field, IEnumerable<object> values) =>
        In(field, values, ODataInStyle.Auto);

    /// <inheritdoc cref="In(string, object[])"/>
    public static ODataFilter In<TEntity>(Expression<Func<TEntity, object?>> field, params object[] values) =>
        In(PropertyPath.Resolve(field), values, ODataInStyle.Auto);

    /// <inheritdoc cref="In(string, object[])"/>
    public static ODataFilter In<TEntity>(Expression<Func<TEntity, object?>> field, IEnumerable<object> values) =>
        In(PropertyPath.Resolve(field), values, ODataInStyle.Auto);

    /// <summary>
    /// Creates a membership filter, optionally pinning how it is rendered.
    /// </summary>
    /// <remarks>
    /// <see cref="ODataInStyle.Auto"/> — the default for the shorter overloads — lets the
    /// client choose from its configured schema version. Pin a rendering only when you know
    /// better than the configuration does; forcing <see cref="ODataInStyle.Native"/> without
    /// <c>SchemaVersion = "2.1"</c> produces a request Business Central answers with
    /// <c>501</c>.
    /// </remarks>
    /// <param name="field">Wire name of the field to match.</param>
    /// <param name="values">Values to match against.</param>
    /// <param name="style">How to render the membership test.</param>
    public static ODataFilter In(string field, IEnumerable<object> values, ODataInStyle style)
    {
        var array = values?.ToArray() ?? [];

        if (array.Length == 0)
            return None;

        // One value gains nothing from either list form, and 'eq' is accepted everywhere.
        if (array.Length == 1)
            return Equals(field, array[0]);

        var orChain = string.Join(" or ", array.Select(v => $"({field} eq {Format(v)})"));
        var native = $"{field} in ({string.Join(",", array.Select(Format))})";

        return style switch
        {
            ODataInStyle.Native => new ODataFilter(native),
            ODataInStyle.OrChain => new ODataFilter(orChain),

            // Auto: keep the portable rendering as Value, and let a configured client
            // substitute the shorter one when it knows the endpoint accepts it.
            _ => new ODataFilter(orChain, useNative => useNative ? native : orChain)
        };
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
    /// <remarks>
    /// Composing this away is part of the contract, not an optimisation: <c>.And(...)</c> and
    /// <c>.Or(...)</c> drop it rather than parenthesising it into the expression, because
    /// <c>(true) and (status eq 'Open')</c> is not a filter Business Central accepts.
    /// </remarks>
    public static ODataFilter All => new(ODataFilter.MatchAll);

    /// <summary>A filter that matches nothing. Answered client-side, without a request.</summary>
    /// <remarks>
    /// Business Central's documented filter set is field-and-operator only — it has no boolean
    /// literal, and Microsoft documents that a filter with no AL equivalent is rejected — so
    /// this is never sent. A query whose filter is this one returns an empty result without a
    /// round trip, which is both the correct answer and the only portable way to express it.
    /// <c>Filter.In(field, [])</c> produces it, so an empty key set is genuinely safe.
    /// </remarks>
    public static ODataFilter None => new(ODataFilter.MatchNone);

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
            // The C# member name, quoted. Business Central option values are free text and
            // routinely contain spaces ("Firm Planned"), which no enum member can spell — so
            // an enum only works here when its member names match the option strings exactly.
            // Where they cannot, pass the option string itself.
            Enum e => $"'{e}'",
            _ => FormatFallback(value)
        };

    /// <summary>
    /// Formats a value no other case claimed, and refuses the one mistake that would otherwise
    /// reach the server as garbage.
    /// </summary>
    /// <remarks>
    /// A collection falls through to <see cref="Convert.ToString(object?, IFormatProvider?)"/>,
    /// which yields <c>System.Object[]</c> — a filter the URL builder happily encodes and
    /// Business Central rejects with no clue as to why. Since <see cref="In(string, object[])"/>
    /// is right there, passing a collection to a scalar comparison is a plausible slip and is
    /// worth naming.
    /// </remarks>
    private static string FormatFallback(object value)
    {
        if (value is System.Collections.IEnumerable and not string)
        {
            throw new ArgumentException(
                $"Cannot format {value.GetType().Name} as a single OData filter value: a " +
                "collection has no scalar literal. Use Filter.In(field, values) to match any " +
                "of several values.",
                nameof(value));
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
    }

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
