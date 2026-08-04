namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// How <see cref="Filter.In(string, IEnumerable{object}, ODataInStyle)"/> renders a
/// membership test.
/// </summary>
/// <remarks>
/// Business Central supports the <c>in</c> operator, but only from schema version 2.1 —
/// Microsoft's filter-expression reference states <i>"In a list of values … Note: This only
/// works in <c>$schemaversion=2.1</c>"</i>, and a live tenant confirms it: the same query
/// answers <c>501</c> without the parameter and <c>200</c> with it, returning byte-identical
/// rows to the <c>or</c>-chain. So neither rendering can be unconditionally right, which is
/// what <see cref="Auto"/> is for.
/// </remarks>
public enum ODataInStyle
{
    /// <summary>
    /// Let the client decide — the default. It emits <see cref="Native"/> when the configured
    /// <c>BusinessCentralOptions.SchemaVersion</c> is 2.1 or later (or when
    /// <c>BusinessCentralOptions.InStyle</c> forces a rendering), and <see cref="OrChain"/>
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// The decision happens when the request URL is built, not when the filter is constructed,
    /// so composing an automatic membership filter with <c>.And(...)</c> keeps it automatic.
    /// Reading <c>ODataFilter.Value</c> outside a client yields the portable <c>or</c>-chain,
    /// because a bare value has no endpoint to ask.
    /// </remarks>
    Auto = 0,

    /// <summary>
    /// <c>(field eq v1) or (field eq v2) …</c> — accepted on every Business Central schema
    /// version, at about twice the encoded query-string length per value.
    /// </summary>
    OrChain = 1,

    /// <summary>
    /// <c>field in (v1,v2,…)</c> — what Business Central natively supports from schema version
    /// 2.1 onwards, and roughly half the encoded width.
    /// </summary>
    /// <remarks>
    /// Forcing this without <c>BusinessCentralOptions.SchemaVersion = "2.1"</c> produces a
    /// request the server answers with <c>501</c>. Prefer <see cref="Auto"/> and set the schema
    /// version, which makes the two move together.
    /// </remarks>
    Native = 2
}
