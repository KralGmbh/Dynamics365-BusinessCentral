using System.Net;

namespace Dynamics365.BusinessCentral.Errors;

public abstract class BusinessCentralException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string? RequestUrl { get; }
    public string? ResponseBody { get; }

    protected BusinessCentralException(
        string message,
        HttpStatusCode statusCode,
        string method,
        string? requestUrl,
        string? responseBody,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Method = method;
        RequestUrl = requestUrl;
        ResponseBody = responseBody;
    }
}

public sealed class BusinessCentralNotFoundException : BusinessCentralException
{
    public BusinessCentralNotFoundException(string message, HttpStatusCode code, string method, string? url, string? body, Exception? inner = null)
        : base(message, code, method, url, body, inner) { }
}

public sealed class BusinessCentralAuthException : BusinessCentralException
{
    public BusinessCentralAuthException(string message, HttpStatusCode code, string method, string? url, string? body, Exception? inner = null)
        : base(message, code, method, url, body, inner) { }
}

public sealed class BusinessCentralValidationException : BusinessCentralException
{
    public BusinessCentralValidationException(string message, HttpStatusCode code, string method, string? url, string? body, Exception? inner = null)
        : base(message, code, method, url, body, inner) { }
}

public sealed class BusinessCentralServerException : BusinessCentralException
{
    public BusinessCentralServerException(string message, HttpStatusCode code, string method, string? url, string? body, Exception? inner = null)
        : base(message, code, method, url, body, inner) { }
}
