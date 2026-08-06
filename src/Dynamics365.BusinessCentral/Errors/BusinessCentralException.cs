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
    /// The message as supplied to the constructor, before <see cref="Exception.Message"/>
    /// decorated it with method and status. Kept so the client can re-wrap an exception with
    /// added context without the decoration accumulating.
    /// </summary>
    internal string ServerMessage { get; }

    /// <summary>
    /// Delay requested by the server via <c>Retry-After</c>, when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; internal set; }

    /// <summary>
    /// The failure happened while acquiring the OAuth2 token, not while calling Business
    /// Central. The two share this exception hierarchy, so without this flag a misconfigured
    /// <c>TokenEndpoint</c> is indistinguishable from an answer about the entity: a token
    /// endpoint returning <c>404</c> produced a
    /// <see cref="BusinessCentralNotFoundException"/> that <c>GetAsync</c> read as "no such
    /// entity" and swallowed into a <see langword="null"/>.
    /// </summary>
    public bool IsTokenAcquisitionFailure { get; internal set; }

    /// <summary>
    /// Whether retrying the same request could plausibly succeed. <see langword="true"/> for
    /// throttling and transient server failures, <see langword="false"/> for validation,
    /// authentication and not-found errors.
    /// </summary>
    public virtual bool IsTransient => false;

    /// <summary>The entity or entity set does not exist (<c>404</c>).</summary>
    /// <remarks>
    /// The exception subtypes are sealed siblings, so
    /// <c>catch (BusinessCentralServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)</c>
    /// can never match — the 404 is a <see cref="BusinessCentralNotFoundException"/>. These
    /// predicates make the safe form the obvious one:
    /// <c>catch (BusinessCentralException ex) when (ex.IsNotFound)</c>.
    /// </remarks>
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>The request was throttled (<c>429</c>).</summary>
    public bool IsThrottled => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>Business Central rejected the request as invalid (<c>400</c>).</summary>
    public bool IsValidation => StatusCode == HttpStatusCode.BadRequest;

    /// <summary>Authentication or authorisation failed (<c>401</c> / <c>403</c>).</summary>
    public bool IsAuth => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>No response was received: connection failure or client-side timeout.</summary>
    /// <remarks>
    /// Matches on the exception type rather than on <see cref="StatusCode"/> being <c>0</c>.
    /// Several pre-response or protocol failures carry status <c>0</c>; matching the subtype
    /// ensures only a connection failure or timeout is classified as a network failure.
    /// </remarks>
    public bool IsConnectionFailure => this is BusinessCentralConnectionException;

    /// <summary>
    /// The client refused the request before sending it, because its query string exceeded
    /// <c>BusinessCentralOptions.MaxQueryStringLength</c>. Never transient: the same call
    /// produces the same length every time.
    /// </summary>
    public bool IsUrlTooLong => this is BusinessCentralUrlTooLongException;

    /// <summary>
    /// The response broke the OData contract in a way the client refused to act on — a
    /// continuation pointing somewhere other than the configured service, or one that never
    /// advances. Never transient: the same response repeats the same violation.
    /// </summary>
    public bool IsProtocolViolation => this is BusinessCentralProtocolException;

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
        ServerMessage = message;
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

        // A client-side refusal has no method to name, and its message already says what
        // happened — decorating it with "( → no response)" would only add noise.
        if (string.IsNullOrEmpty(method))
            return trimmed;

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
            ? IsConnectionFailure
                ? "Status: (no response received)"
                : "Status: (no HTTP status associated)"
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

/// <summary>
/// The client refused to send a request whose query string exceeded
/// <c>BusinessCentralOptions.MaxQueryStringLength</c>. Never transient.
/// <see cref="BusinessCentralException.StatusCode"/> is <c>0</c> and
/// <see cref="BusinessCentralException.Method"/> is empty, because the request was never sent
/// and never had either.
/// </summary>
/// <remarks>
/// <para>
/// This is a pre-flight refusal, not a server answer. Business Central's gateway answers
/// <c>414 URI Too Long</c> past its own ceiling — measured at 8,099 accepted query-string
/// characters — and the point of failing first is that this exception can name the length, the
/// limit and the <c>or</c>-clause count, which a bare <c>414</c> cannot. Keeping status at
/// <c>0</c> rather than borrowing <c>414</c> is what lets a caller tell the two apart.
/// </para>
/// <para>
/// It derives from <see cref="BusinessCentralException"/> so that
/// <c>catch (BusinessCentralException)</c> keeps seeing every failure the client produces —
/// the guard used to throw <see cref="ArgumentException"/>, which no handler written against
/// that contract would catch, and which is a poor fit besides: the length depends on the data
/// a call is given, not on the arguments being malformed.
/// </para>
/// </remarks>
public sealed class BusinessCentralUrlTooLongException : BusinessCentralException
{
    /// <summary>Length of the query string that was refused.</summary>
    public int QueryStringLength { get; }

    /// <summary>Length of the whole URL, of which the query string is the measured part.</summary>
    public int UrlLength { get; }

    /// <summary>The configured <c>MaxQueryStringLength</c> that was exceeded.</summary>
    public int Limit { get; }

    /// <summary>
    /// How many <c>or</c> clauses the URL contains, counted inside <c>$filter</c> only. Two or
    /// more usually means a <see cref="OData.Filter.In(string, object[])"/> rendered as an
    /// or-chain, which is the dominant cause of hitting this limit.
    /// </summary>
    public int OrClauseCount { get; }

    /// <summary>Creates a new <see cref="BusinessCentralUrlTooLongException"/>.</summary>
    /// <param name="message">Short, single-line description of the failure.</param>
    /// <param name="requestUrl">The URL that was built but not sent.</param>
    /// <param name="urlLength">Length of the whole URL.</param>
    /// <param name="queryStringLength">Length of the query string that was refused.</param>
    /// <param name="limit">The configured limit that was exceeded.</param>
    /// <param name="orClauseCount">Number of <c>or</c> clauses found in the filter.</param>
    public BusinessCentralUrlTooLongException(
        string message,
        string? requestUrl,
        int urlLength,
        int queryStringLength,
        int limit,
        int orClauseCount)
        : base(message, 0, string.Empty, requestUrl, null, null, null, null)
    {
        UrlLength = urlLength;
        QueryStringLength = queryStringLength;
        Limit = limit;
        OrClauseCount = orClauseCount;
    }
}

/// <summary>
/// The client refused to act on a response that broke the OData contract. Never transient.
/// <see cref="BusinessCentralException.StatusCode"/> is <c>0</c> and
/// <see cref="BusinessCentralException.Method"/> is empty, because this is a refusal to send
/// the *next* request rather than the result of one.
/// </summary>
/// <remarks>
/// <para>
/// Raised for two continuation faults, both of which would otherwise turn a bad response
/// into something worse than an error:
/// </para>
/// <list type="bullet">
/// <item><description>
/// An <c>@odata.nextLink</c> whose origin is not the configured service root. Continuations
/// are sent verbatim and carry the bearer token, so following one to another host would
/// disclose the token and turn the client into an SSRF vector on behalf of whoever wrote
/// the response.
/// </description></item>
/// <item><description>
/// A continuation cursor that has already been followed. When its page carried rows,
/// refetching yields the page already emitted, so an uncapped stream would repeat those rows
/// forever. When the page was empty the fault is the opposite one: a <c>nextLink</c> asserts
/// that continuation remains, so the client cannot conclude the collection is complete —
/// stopping quietly would hand back a possibly truncated result as success. Neither is
/// something the caller can be left to notice, so both throw.
/// </description></item>
/// </list>
/// <para>
/// <see cref="BusinessCentralException.RequestUrl"/> carries the offending continuation.
/// </para>
/// </remarks>
public sealed class BusinessCentralProtocolException : BusinessCentralException
{
    /// <summary>Creates a new <see cref="BusinessCentralProtocolException"/>.</summary>
    /// <param name="message">Short, single-line description of the violation.</param>
    /// <param name="requestUrl">The continuation URL that was rejected.</param>
    public BusinessCentralProtocolException(string message, string? requestUrl)
        : base(message, 0, string.Empty, requestUrl, null, null, null, null) { }
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
