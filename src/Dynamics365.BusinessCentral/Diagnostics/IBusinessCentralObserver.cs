namespace Dynamics365.BusinessCentral.Diagnostics;

public interface IBusinessCentralObserver
{
    void OnRequestStarting(BusinessCentralRequestInfo request);

    void OnRequestSucceeded(BusinessCentralRequestInfo request);

    void OnRequestFailed(BusinessCentralErrorInfo error);

    void OnTokenRequested();

    void OnTokenRefreshed(BusinessCentralTokenInfo token);

    void OnDeserializationFailed(BusinessCentralErrorInfo error);
}
