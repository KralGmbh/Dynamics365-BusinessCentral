namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Extension methods for composing OData filters using logical operators.
/// </summary>
public static class FilterExtensions
{
    /// <summary>Combines two filters using logical AND.</summary>
    public static ODataFilter And(this ODataFilter left, ODataFilter right) =>
        new($"({left}) and ({right})");

    /// <summary>Combines two filters using logical OR.</summary>
    /// <remarks>
    /// Business Central only supports <c>or</c> between filters on the <b>same field</b> —
    /// an OR across different fields (<c>field1 eq 1 or field2 eq 2</c>) has no AL filter
    /// equivalent and the server rejects it with <c>BadRequest_MethodNotImplemented</c>.
    /// This is a Business Central limitation, not something the client can translate away.
    /// </remarks>
    public static ODataFilter Or(this ODataFilter left, ODataFilter right) =>
        new($"({left}) or ({right})");

    /// <summary>Negates a filter using logical NOT.</summary>
    public static ODataFilter Not(this ODataFilter filter) =>
        new($"not ({filter})");
}
