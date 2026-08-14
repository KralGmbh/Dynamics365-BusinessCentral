using Dynamics365.BusinessCentral.Diagnostics;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// Records what the client did on the wire, so a fact can assert on the requests themselves
/// rather than only on the rows they returned.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IBusinessCentralObserver"/> is public API, so using it here keeps this project a
/// consumer — the same reason there is no <c>InternalsVisibleTo</c>.
/// </para>
/// <para>
/// <see cref="OnRequestStarting"/> is raised once per request, before any retry, so
/// <see cref="Requests"/> counts round trips the package <i>chose</i> to make. That is what makes
/// it a usable measure of how many pages a read fetched.
/// </para>
/// </remarks>
internal sealed class RecordingObserver : IBusinessCentralObserver
{
    private readonly List<string> _requests = [];
    private readonly List<BusinessCentralUrlLengthInfo> _lengthWarnings = [];

    /// <summary>URLs of the requests sent since the last <see cref="Clear"/>.</summary>
    public IReadOnlyList<string> Requests => _requests;

    /// <summary><c>OnUrlLengthWarning</c> payloads, for facts that assert on a measured length.</summary>
    public IReadOnlyList<BusinessCentralUrlLengthInfo> LengthWarnings => _lengthWarnings;

    /// <summary>
    /// Drops everything recorded so far, so a fact can measure one read rather than the setup
    /// that preceded it.
    /// </summary>
    public void Clear()
    {
        _requests.Clear();
        _lengthWarnings.Clear();
    }

    public void OnRequestStarting(BusinessCentralRequestInfo request) => _requests.Add(request.Url);

    public void OnUrlLengthWarning(BusinessCentralUrlLengthInfo url) => _lengthWarnings.Add(url);

    public void OnRequestSucceeded(BusinessCentralRequestInfo request) { }
    public void OnRequestFailed(BusinessCentralErrorInfo error) { }
    public void OnTokenRequested() { }
    public void OnTokenRefreshed(BusinessCentralTokenInfo token) { }
    public void OnDeserializationFailed(BusinessCentralErrorInfo error) { }
}
