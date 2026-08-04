namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>
/// Isolates the pipeline from a throwing observer: diagnostics are best-effort, and a bug
/// in an <see cref="IBusinessCentralObserver"/> implementation must not turn a successful
/// request into a failure — or worse, break the retry loop mid-flight. Every callback is
/// swallowed on error, mirroring how the client already treats unreadable response bodies
/// ("diagnostics must never mask the original failure").
/// </summary>
internal sealed class SafeBusinessCentralObserver : IBusinessCentralObserver
{
    private readonly IBusinessCentralObserver _inner;

    private SafeBusinessCentralObserver(IBusinessCentralObserver inner) => _inner = inner;

    /// <summary>
    /// Wraps <paramref name="observer"/>, or returns a null observer when none was given.
    /// Idempotent: an already-wrapped observer is returned as-is, so the client can hand
    /// its observer to the token provider without double-wrapping.
    /// </summary>
    public static IBusinessCentralObserver Wrap(IBusinessCentralObserver? observer) =>
        observer switch
        {
            null => new NullBusinessCentralObserver(),
            SafeBusinessCentralObserver safe => safe,
            NullBusinessCentralObserver @null => @null,
            _ => new SafeBusinessCentralObserver(observer)
        };

    public void OnRequestStarting(BusinessCentralRequestInfo request) =>
        Invoke(() => _inner.OnRequestStarting(request));

    public void OnRequestSucceeded(BusinessCentralRequestInfo request) =>
        Invoke(() => _inner.OnRequestSucceeded(request));

    public void OnRequestFailed(BusinessCentralErrorInfo error) =>
        Invoke(() => _inner.OnRequestFailed(error));

    public void OnRequestRetrying(BusinessCentralRetryInfo retry) =>
        Invoke(() => _inner.OnRequestRetrying(retry));

    public void OnUrlLengthWarning(BusinessCentralUrlLengthInfo url) =>
        Invoke(() => _inner.OnUrlLengthWarning(url));

    public void OnDeserializationFailed(BusinessCentralErrorInfo error) =>
        Invoke(() => _inner.OnDeserializationFailed(error));

    public void OnTokenRequested() =>
        Invoke(_inner.OnTokenRequested);

    public void OnTokenRefreshed(BusinessCentralTokenInfo token) =>
        Invoke(() => _inner.OnTokenRefreshed(token));

    public void OnTokenServedFromCache(BusinessCentralTokenInfo token) =>
        Invoke(() => _inner.OnTokenServedFromCache(token));

    private static void Invoke(Action callback)
    {
        try
        {
            callback();
        }
        catch
        {
            // Observer failures are deliberately silent: there is no safe place to report
            // a broken reporter.
        }
    }
}
