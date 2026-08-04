namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Extension methods for composing OData filters using logical operators.
/// </summary>
public static class FilterExtensions
{
    /// <summary>Combines two filters using logical AND.</summary>
    public static ODataFilter And(this ODataFilter left, ODataFilter right) =>
        Combine(left, right, "and");

    /// <summary>Combines two filters using logical OR.</summary>
    /// <remarks>
    /// Business Central only supports <c>or</c> between filters on the <b>same field</b> —
    /// an OR across different fields (<c>field1 eq 1 or field2 eq 2</c>) has no AL filter
    /// equivalent and the server rejects it with
    /// <c>"The 'OR' operator is not supported on distinct fields on an OData filter."</c>
    /// This is a Business Central limitation, not something the client can translate away, and
    /// no schema version lifts it — the remedies are one request per field, or an AL API page
    /// that exposes the combination as a single filterable column. (Not to be confused with
    /// <c>BadRequest_MethodNotImplemented</c>, which is what the <c>in</c> operator answers
    /// below schema version 2.1 — see <see cref="Filter.In(string, object[])"/>.)
    /// </remarks>
    public static ODataFilter Or(this ODataFilter left, ODataFilter right) =>
        Combine(left, right, "or");

    /// <summary>Negates a filter using logical NOT.</summary>
    /// <remarks>
    /// <b>Business Central limitation.</b> <c>not</c> does not appear in Microsoft's documented
    /// set of supported filter expressions, which is field-and-operator only, and Microsoft
    /// documents that an expression with no AL approximation is rejected. This has not been
    /// measured against a live tenant, so it is stated as undocumented rather than as known to
    /// fail — but prefer the documented negations where they exist:
    /// <see cref="Filter.NotEquals(string, object?)"/> (<c>ne</c>, AL <c>&lt;&gt;</c>) covers
    /// the common case, and <see cref="Filter.IsNotNull(string)"/> covers the other.
    /// </remarks>
    public static ODataFilter Not(this ODataFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Negating a constant yields the other constant, which keeps both out of the URL:
        // "not (true)" is no more sendable than the "true" it wraps.
        if (filter.Value == ODataFilter.MatchAll)
            return Filter.None;

        if (filter.Value == ODataFilter.MatchNone)
            return Filter.All;

        return filter.IsSchemaSensitive
            ? new ODataFilter($"not ({filter})", native => $"not ({filter.Render(native)})")
            : new ODataFilter($"not ({filter})");
    }

    /// <summary>
    /// Joins two filters, carrying schema sensitivity through the composition and reducing
    /// away the two constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A membership filter left at <see cref="ODataInStyle.Auto"/> renders differently
    /// depending on the endpoint's schema version, and composing it must not freeze that
    /// choice — a chunked key lookup is almost always <c>.And(...)</c>-ed with something else,
    /// so losing the deferral on composition would mean the automatic form never applies where
    /// it matters most. The deferred branch reproduces the eager string exactly; only the
    /// operands can differ.
    /// </para>
    /// <para>
    /// <see cref="Filter.All"/> and <see cref="Filter.None"/> are reduced away rather than
    /// parenthesised in. Business Central has no boolean-literal filter construct, so
    /// <c>(true) and (status eq 'Open')</c> is not a weaker form of the same query — it is a
    /// filter the server has no AL translation for. Reducing here is what keeps the two
    /// constants from reaching the wire the moment anything is composed with them, which for
    /// <see cref="Filter.All"/> is the ordinary case: the URL builder's own
    /// "<c>true</c> means no <c>$filter</c>" rule only fires on an uncomposed filter.
    /// </para>
    /// </remarks>
    private static ODataFilter Combine(ODataFilter left, ODataFilter right, string op)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // Identity and annihilator, per operator. Reference-returning an operand is safe:
        // ODataFilter is immutable, and returning it preserves any deferred rendering it
        // carries — dropping a Filter.All must not cost a composed Filter.In its deferral.
        if (op == "and")
        {
            if (left.Value == ODataFilter.MatchAll) return right;
            if (right.Value == ODataFilter.MatchAll) return left;
            if (left.Value == ODataFilter.MatchNone || right.Value == ODataFilter.MatchNone)
                return Filter.None;
        }
        else
        {
            if (left.Value == ODataFilter.MatchNone) return right;
            if (right.Value == ODataFilter.MatchNone) return left;
            if (left.Value == ODataFilter.MatchAll || right.Value == ODataFilter.MatchAll)
                return Filter.All;
        }

        var eager = $"({left}) {op} ({right})";

        // No allocation for the overwhelmingly common case of two plain filters.
        return left.IsSchemaSensitive || right.IsSchemaSensitive
            ? new ODataFilter(eager, native => $"({left.Render(native)}) {op} ({right.Render(native)})")
            : new ODataFilter(eager);
    }
}
