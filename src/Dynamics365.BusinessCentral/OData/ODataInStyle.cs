namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// How <see cref="Filter.In(string, IEnumerable{object}, ODataInStyle)"/> renders a
/// membership test.
/// </summary>
/// <remarks>
/// Business Central supports the <c>in</c> operator, but only from schema version 2.1 —
/// Microsoft's filter-expression reference states <i>"In a list of values … Note: This only
/// works in <c>$schemaversion=2.1</c>"</i>, and a live tenant on an earlier version answered
/// <c>BadRequest_MethodNotImplemented</c>. So the default cannot be <see cref="Native"/>, and
/// the workaround cannot be the only option either.
/// </remarks>
public enum ODataInStyle
{
    /// <summary>
    /// <c>(field eq v1) or (field eq v2) …</c> — the default. Accepted on every Business
    /// Central schema version, at about twice the encoded URL length per value.
    /// </summary>
    OrChain = 0,

    /// <summary>
    /// <c>field in (v1,v2,…)</c> — far shorter, and what Business Central natively supports
    /// from schema version 2.1 onwards.
    /// </summary>
    /// <remarks>
    /// <b>Requires <c>BusinessCentralOptions.SchemaVersion = "2.1"</c>.</b> The two are a pair:
    /// without the schema version the server answers
    /// <c>BadRequest_MethodNotImplemented</c>, so choosing this style alone only changes which
    /// error you get. Verify against your own endpoint — availability depends on the deployment
    /// and its version.
    /// </remarks>
    Native = 1
}
