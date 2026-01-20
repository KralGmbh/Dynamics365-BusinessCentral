using Dynamics365.BusinessCentral.Diagnostics;

namespace Dynamics365.BusinessCentral.Tests.Utils;

public sealed class TestObserver : IBusinessCentralObserver
{
    public readonly List<string> Events = [];

    public void OnRequestStarting(BusinessCentralRequestInfo info)
        => Events.Add($"start:{info.Method}");

    public void OnRequestSucceeded(BusinessCentralRequestInfo info)
        => Events.Add($"success:{info.StatusCode}");

    public void OnRequestFailed(BusinessCentralErrorInfo info)
        => Events.Add($"fail:{info.StatusCode}");

    public void OnTokenRequested()
        => Events.Add("token-requested");

    public void OnTokenRefreshed(BusinessCentralTokenInfo info)
        => Events.Add(info.FromCache ? "token-cached" : "token-refreshed");

    public void OnDeserializationFailed(BusinessCentralErrorInfo info)
        => Events.Add("deserialization-failed");
}
