using Dynamics365.BusinessCentral.Diagnostics;

namespace Dynamics365.BusinessCentral.Tests.Utils;

public sealed class TestObserver : IBusinessCentralObserver
{
    public readonly List<string> Events = [];

    public readonly List<BusinessCentralErrorInfo> Failures = [];

    public void OnRequestStarting(BusinessCentralRequestInfo info)
        => Events.Add($"start:{info.Method}");

    public void OnRequestSucceeded(BusinessCentralRequestInfo info)
        => Events.Add($"success:{info.StatusCode}");

    public void OnRequestFailed(BusinessCentralErrorInfo info)
    {
        Events.Add($"fail:{info.StatusCode}");
        Failures.Add(info);
    }

    public void OnTokenRequested()
        => Events.Add("token-requested");

    public void OnTokenRefreshed(BusinessCentralTokenInfo info)
        => Events.Add("token-refreshed");

    public void OnTokenServedFromCache(BusinessCentralTokenInfo info)
        => Events.Add("token-cached");

    public readonly List<BusinessCentralUrlLengthInfo> UrlWarnings = [];

    public void OnUrlLengthWarning(BusinessCentralUrlLengthInfo info)
    {
        Events.Add($"url-length:{info.Length}");
        UrlWarnings.Add(info);
    }

    public void OnDeserializationFailed(BusinessCentralErrorInfo info)
        => Events.Add("deserialization-failed");
}
