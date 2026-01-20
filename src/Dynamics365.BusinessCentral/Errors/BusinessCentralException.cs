using System.Net;
using System.Text;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Errors;

public abstract class BusinessCentralException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string? RequestUrl { get; }
    public string? ResponseBody { get; }
    public string? ODataErrorCode { get; }
    public string? CorrelationId { get; }

    protected BusinessCentralException(
        string message,
        HttpStatusCode statusCode,
        string method,
        string? requestUrl,
        string? responseBody,
        string? odataErrorCode = null,
        string? correlationId = null,
        Exception? inner = null)
        : base(BuildMessage(message, statusCode, method, requestUrl, responseBody, odataErrorCode, correlationId), inner)
    {
        StatusCode = statusCode;
        Method = method;
        RequestUrl = requestUrl;
        ResponseBody = responseBody;
        ODataErrorCode = odataErrorCode;
        CorrelationId = correlationId;
    }

    private static string BuildMessage(
        string message,
        HttpStatusCode status,
        string method,
        string? url,
        string? body,
        string? odataErrorCode,
        string? correlationId)
    {
        var sb = new StringBuilder();

        sb.AppendLine(message);
        sb.AppendLine($"Status: {(int)status} {status}");
        sb.AppendLine($"Method: {method}");
        sb.AppendLine($"URL: {url}");

        if (!string.IsNullOrWhiteSpace(odataErrorCode))
            sb.AppendLine($"OData Code: {odataErrorCode}");

        if (!string.IsNullOrWhiteSpace(correlationId))
            sb.AppendLine($"CorrelationId: {correlationId}");

        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine("Response:");
            sb.AppendLine(body);
        }

        return sb.ToString();
    }

    public override string ToString() => Message;
}

public sealed class BusinessCentralNotFoundException : BusinessCentralException
{
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

public sealed class BusinessCentralAuthException : BusinessCentralException
{
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

public sealed class BusinessCentralValidationException : BusinessCentralException
{
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

public sealed class BusinessCentralServerException : BusinessCentralException
{
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
