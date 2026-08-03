namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// How <see cref="Filter.In(string, IEnumerable{object}, ODataInStyle)"/> renders a
/// membership test.
/// </summary>
/// <remarks>
/// This exists because the package's default encodes a measurement from one deployment, and
/// a measurement from one deployment should never be the only thing a consumer can express.
/// Business Central endpoints differ in whether they accept the OData <c>in</c> operator.
/// </remarks>
public enum ODataInStyle
{
    /// <summary>
    /// <c>(field eq v1) or (field eq v2) …</c> — the default. Accepted on every Business
    /// Central schema version, at about twice the encoded URL length per value.
    /// </summary>
    OrChain = 0,

    /// <summary>
    /// <c>field in (v1,v2,…)</c> — far shorter, but rejected with
    /// <c>BadRequest_MethodNotImplemented</c> by endpoints that do not serve
    /// <c>$schemaversion=2.1</c>. Verify against your own endpoint before relying on it.
    /// </summary>
    Native = 1
}
