namespace Dynamics365.BusinessCentral.Diagnostics;

internal sealed class NullBusinessCentralObserver : IBusinessCentralObserver
{
    public void OnRequestStarting(BusinessCentralRequestInfo request) { }

    public void OnRequestSucceeded(BusinessCentralRequestInfo request) { }

    public void OnRequestFailed(BusinessCentralErrorInfo error) { }

    public void OnTokenRequested() { }

    public void OnTokenRefreshed(BusinessCentralTokenInfo token) { }

    public void OnDeserializationFailed(BusinessCentralErrorInfo error) { }
}
