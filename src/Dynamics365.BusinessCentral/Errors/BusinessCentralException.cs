using System.Net;
using System.Text;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Errors;

/// <summary>
/// Base type for every error surfaced by the Business Central client.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is deliberately a single line so it stays usable as a
/// log message. The response body, request URL, OData error code and correlation ID are
/// available as properties, and <see cref="ToString"/> renders all of them.
/// </remarks>
public abstract class BusinessCentralException : Exception
{
    /// <summary>HTTP status code returned by Business Central.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>HTTP method of the failed request.</summary>
    public string Method { get; }

    /// <summary>URL of the failed request.</summary>
    public string? RequestUrl { get; }

    /// <summary>Raw response body, when one was returned.</summary>
    public string? ResponseBody { get; }

    /// <summary>OData error code, e.g. <c>BadRequest_ResourceNotFound</c>.</summary>
    public string? ODataErrorCode { get; }

    /// <summary>Business Central correlation ID, useful when raising a support ticket.</summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Delay requested by the server via <c>Retry-After</c>, when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; internal set; }

    /// <summary>
    /// Whether retrying the same request could plausibly succeed. <see langword="true"/> for
    /// throttling and transient server failures, <see langword="false"/> for validation,
    /// authentication and not-found errors.
    /// </summary>
    public virtual bool IsTransient => false;

    /// <summary>Creates a new <see cref="BusinessCentralException"/>.</summary>
    /// <param name="message">Short, single-line description of the failure.</param>
    /// <param name="statusCode">HTTP status code returned by Business Central.</param>
    /// <param name="method">HTTP method of the failed request.</param>
    /// <param name="requestUrl">URL of the failed request.</param>
    /// <param name="responseBody">Raw response body, when one was returned.</param>
    /// <param name="odataErrorCode">OData error code parsed from the response.</param>
    /// <param name="correlationId">Business Central correlation ID.</param>
    /// <param name="inner">Underlying exception, when this wraps one.</param>
    protected BusinessCentralException(
        string message,
        HttpStatusCode statusCode,
        string method,
        string? requestUrl,
        string? responseBody,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(BuildSummary(message, statusCode, method), inner)
    {
        StatusCode = statusCode;
        Method = method;
        RequestUrl = requestUrl;
        ResponseBody = responseBody;
        ODataErrorCode = odataErrorCode;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// One line: the server's message plus method and status. Everything else lives on
    /// properties so structured logs are not flooded with response bodies.
    /// </summary>
    private static string BuildSummary(string message, HttpStatusCode status, string method)
    {
        var trimmed = string.IsNullOrWhiteSpace(message)
            ? $"Business Central request failed."
            : message.Trim().ReplaceLineEndings(" ");

        // Status 0 means no response was ever received (connection failure or timeout).
        return status == 0
            ? $"{trimmed} ({method} → no response)"
            : $"{trimmed} ({method} → HTTP {(int)status} {status})";
    }

    /// <summary>
    /// Renders the full diagnostic picture: message, stack trace, request URL, OData code,
    /// correlation ID and response body.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder(base.ToString());

        sb.AppendLine();
        sb.AppendLine("--- Business Central details ---");
        sb.AppendLine(StatusCode == 0
            ? "Status: (no response received)"
            : $"Status: {(int)StatusCode} {StatusCode}");
        sb.AppendLine($"Method: {Method}");
        sb.AppendLine($"URL: {RequestUrl}");

        if (!string.IsNullOrWhiteSpace(ODataErrorCode))
            sb.AppendLine($"OData Code: {ODataErrorCode}");

        if (!string.IsNullOrWhiteSpace(CorrelationId))
            sb.AppendLine($"CorrelationId: {CorrelationId}");

        if (RetryAfter != null)
            sb.AppendLine($"Retry-After: {RetryAfter}");

        if (!string.IsNullOrWhiteSpace(ResponseBody))
        {
            sb.AppendLine("Response:");
            sb.AppendLine(ResponseBody);
        }

        return sb.ToString();
    }
}

/// <summary>The requested entity or entity set does not exist (<c>404</c>).</summary>
public sealed class BusinessCentralNotFoundException : BusinessCentralException
{
    /// <inheritdoc cref="BusinessCentralException(string, HttpStatusCode, string, string?, string?, string?, string?, Exception?)"/>
    public BusinessCentralNotFoundException(
        string message,
        HttpStatusCode code,
        string method,
        string? url,
        string? body,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(message, code, method, url, body, odataErrorCode, correlationId, inner) { }
}

/// <summary>Authentication or authorisation failed (<c>401</c> / <c>403</c>).</summary>
public sealed class BusinessCentralAuthException : BusinessCentralException
{
    /// <inheritdoc cref="BusinessCentralException(string, HttpStatusCode, string, string?, string?, string?, string?, Exception?)"/>
    public BusinessCentralAuthException(
        string message,
        HttpStatusCode code,
        string method,
        string? url,
        string? body,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(message, code, method, url, body, odataErrorCode, correlationId, inner) { }
}

/// <summary>Business Central rejected the request as invalid (<c>400</c>).</summary>
public sealed class BusinessCentralValidationException : BusinessCentralException
{
    /// <inheritdoc cref="BusinessCentralException(string, HttpStatusCode, string, string?, string?, string?, string?, Exception?)"/>
    public BusinessCentralValidationException(
        string message,
        HttpStatusCode code,
        string method,
        string? url,
        string? body,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(message, code, method, url, body, odataErrorCode, correlationId, inner) { }
}

/// <summary>
/// The request was throttled (<c>429</c>). Always transient; inspect
/// <see cref="BusinessCentralException.RetryAfter"/> for the server's requested delay.
/// </summary>
public sealed class BusinessCentralThrottledException : BusinessCentralException
{
    /// <inheritdoc cref="BusinessCentralException(string, HttpStatusCode, string, string?, string?, string?, string?, Exception?)"/>
    public BusinessCentralThrottledException(
        string message,
        HttpStatusCode code,
        string method,
        string? url,
        string? body,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(message, code, method, url, body, odataErrorCode, correlationId, inner) { }

    /// <inheritdoc />
    public override bool IsTransient => true;
}

/// <summary>
/// No HTTP response was received: the connection failed or the request timed out
/// client-side before Business Central answered. Always transient.
/// <see cref="BusinessCentralException.StatusCode"/> is <c>0</c>, because without a
/// response there is no status code.
/// </summary>
/// <remarks>
/// The underlying <see cref="HttpRequestException"/> or timeout is preserved as
/// <see cref="Exception.InnerException"/>. Like the ambiguous transient statuses, the
/// request may have reached the server even though the response never arrived, so
/// idempotent methods are replayed but a <c>POST</c> is not. Cancellation through the
/// caller's own <see cref="CancellationToken"/> is never wrapped in this type.
/// </remarks>
public sealed class BusinessCentralConnectionException : BusinessCentralException
{
    /// <summary>Creates a new <see cref="BusinessCentralConnectionException"/>.</summary>
    /// <param name="message">Short, single-line description of the failure.</param>
    /// <param name="method">HTTP method of the failed request.</param>
    /// <param name="requestUrl">URL of the failed request.</param>
    /// <param name="inner">The underlying connection or timeout exception.</param>
    public BusinessCentralConnectionException(
        string message,
        string method,
        string? requestUrl,
        Exception inner)
        : base(message, 0, method, requestUrl, null, null, null, inner) { }

    /// <inheritdoc />
    public override bool IsTransient => true;
}

/// <summary>
/// Business Central returned a server-side failure, or a response that could not be
/// deserialized. <see cref="BusinessCentralException.IsTransient"/> is <see langword="true"/>
/// for <c>408</c>, <c>502</c>, <c>503</c> and <c>504</c>.
/// </summary>
public sealed class BusinessCentralServerException : BusinessCentralException
{
    /// <inheritdoc cref="BusinessCentralException(string, HttpStatusCode, string, string?, string?, string?, string?, Exception?)"/>
    public BusinessCentralServerException(
        string message,
        HttpStatusCode code,
        string method,
        string? url,
        string? body,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(message, code, method, url, body, odataErrorCode, correlationId, inner) { }

    /// <inheritdoc />
    public override bool IsTransient => StatusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;
}

internal sealed class BusinessCentralODataError
{
    [JsonPropertyName("error")]
    public BusinessCentralODataErrorDetail? Error { get; set; }
}

internal sealed class BusinessCentralODataErrorDetail
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
