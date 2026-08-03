namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>
/// Diagnostics hook for request, retry and token lifecycle events.
/// </summary>
/// <remarks>
/// Register an implementation with <c>services.AddObserver&lt;MyObserver&gt;()</c>. The
/// client has no logging dependency; this interface is how you attach one. Methods with a
/// default implementation can be ignored by existing observers.
/// </remarks>
public interface IBusinessCentralObserver
{
    /// <summary>Raised once when a request is about to be sent, before any retries.</summary>
    void OnRequestStarting(BusinessCentralRequestInfo request);

    /// <summary>Raised when a request completed with a success status code.</summary>
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

    /// <summary>
    /// Raised before the client waits and retries a throttled or transient failure. Has a
    /// default no-op implementation so existing observers keep compiling.
    /// </summary>
    void OnRequestRetrying(BusinessCentralRetryInfo retry) { }

    /// <summary>
    /// Raised when a built request URL crossed
    /// <c>BusinessCentralOptions.QueryStringLengthWarningThreshold</c>, before the request is
    /// sent.
    /// Fires whether or not the URL also exceeded the hard limit — check
    /// <see cref="BusinessCentralUrlLengthInfo.ExceedsLimit"/>. Has a default no-op
    /// implementation so existing observers keep compiling.
    /// </summary>
    void OnUrlLengthWarning(BusinessCentralUrlLengthInfo url) { }

    /// <summary>Raised when a response could not be deserialized into the requested type.</summary>
    void OnDeserializationFailed(BusinessCentralErrorInfo error);
}
