namespace Dynamics365.BusinessCentral.Options;

/// <summary>
/// Value for the <c>Data-Access-Intent</c> header, which tells Business Central whether a
/// read may be served from a database replica.
/// </summary>
/// <remarks>
/// Only ever sent on <c>GET</c>. Microsoft is explicit that modification requests reject it:
/// <i>"Modification requests (like POST, PUT, or DELETE) only support <c>ReadWrite</c> as a
/// value for data access intent. Trying to specify <c>Data-Access-Intent: ReadOnly</c> for
/// such requests will result in an error."</i>
/// </remarks>
public enum BusinessCentralDataAccessIntent
{
    /// <summary>
    /// Send no header — the default. Business Central uses whatever the page or query declares
    /// through its own <c>DataAccessIntent</c> property.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// <c>Data-Access-Intent: ReadOnly</c>. Reads may be served from a replica, taking load off
    /// the primary database.
    /// </summary>
    /// <remarks>
    /// Not a guarantee: Microsoft notes it <i>"doesn't guarantee that your data access will be
    /// running on the secondary replica. It's merely stating that the code only requires read
    /// ability."</i> Where a replica <i>is</i> used, replication lag means a read issued
    /// immediately after a write may not observe it — which is why this is opt-in.
    /// </remarks>
    ReadOnly = 1,

    /// <summary>
    /// <c>Data-Access-Intent: ReadWrite</c>. Forces the primary database, overriding a
    /// <c>ReadOnly</c> <c>DataAccessIntent</c> declared on the page or query.
    /// </summary>
    ReadWrite = 2
}
