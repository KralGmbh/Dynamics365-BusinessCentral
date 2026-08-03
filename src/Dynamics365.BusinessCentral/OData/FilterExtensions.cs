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
    /// equivalent and the server rejects it with <c>BadRequest_MethodNotImplemented</c>.
    /// This is a Business Central limitation, not something the client can translate away.
    /// </remarks>
    public static ODataFilter Or(this ODataFilter left, ODataFilter right) =>
        Combine(left, right, "or");

    /// <summary>Negates a filter using logical NOT.</summary>
    public static ODataFilter Not(this ODataFilter filter) =>
        filter.IsSchemaSensitive
            ? new ODataFilter($"not ({filter})", native => $"not ({filter.Render(native)})")
            : new ODataFilter($"not ({filter})");

    /// <summary>
    /// Joins two filters, carrying schema sensitivity through the composition.
    /// </summary>
    /// <remarks>
    /// A membership filter left at <see cref="ODataInStyle.Auto"/> renders differently
    /// depending on the endpoint's schema version, and composing it must not freeze that
    /// choice — a chunked key lookup is almost always <c>.And(...)</c>-ed with something else,
    /// so losing the deferral on composition would mean the automatic form never applies where
    /// it matters most. The deferred branch reproduces the eager string exactly; only the
    /// operands can differ.
    /// </remarks>
    private static ODataFilter Combine(ODataFilter left, ODataFilter right, string op)
    {
        var eager = $"({left}) {op} ({right})";

        // No allocation for the overwhelmingly common case of two plain filters.
        return left.IsSchemaSensitive || right.IsSchemaSensitive
            ? new ODataFilter(eager, native => $"({left.Render(native)}) {op} ({right.Render(native)})")
            : new ODataFilter(eager);
    }
}
