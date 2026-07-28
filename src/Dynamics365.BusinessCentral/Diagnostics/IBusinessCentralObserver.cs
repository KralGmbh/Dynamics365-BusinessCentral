namespace Dynamics365.BusinessCentral.Diagnostics;

public interface IBusinessCentralObserver
{
    void OnRequestStarting(BusinessCentralRequestInfo request);

    void OnRequestSucceeded(BusinessCentralRequestInfo request);

    /// <summary>
    /// Raised once per failed attempt. A request that is retried after a 401 raises this
    /// for the rejected attempt and then either <see cref="OnRequestSucceeded"/> or a
    /// second failure — never twice for the same attempt.
    /// </summary>
    void OnRequestFailed(BusinessCentralErrorInfo error);

    /// <summary>Raised immediately before a token is requested from the identity provider.</summary>
    void OnTokenRequested();

    /// <summary>
    /// Raised only when a new token was actually obtained. Cache hits raise
    /// <see cref="OnTokenServedFromCache"/> instead.
    /// </summary>
    void OnTokenRefreshed(BusinessCentralTokenInfo token);

    /// <summary>
    /// Raised when an existing, unexpired token was reused. Has a default no-op
    /// implementation so existing observers keep compiling.
    /// </summary>
    void OnTokenServedFromCache(BusinessCentralTokenInfo token) { }

    void OnDeserializationFailed(BusinessCentralErrorInfo error);
}
