using System.Net;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Errors;

public static class BusinessCentralExceptionFactory
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<BusinessCentralException> CreateAsync(
        HttpResponseMessage res,
        CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);

        var url = res.RequestMessage?.RequestUri?.ToString();
        var method = res.RequestMessage?.Method.Method ?? "UNKNOWN";

        string? odataCode = null;
        string? odataMessage = null;
        string? correlationId = null;

        try
        {
            var parsed = JsonSerializer.Deserialize<BusinessCentralODataError>(body, _jsonOptions);

            if (parsed?.Error != null)
            {
                odataCode = parsed.Error.Code;
                odataMessage = parsed.Error.Message;

                correlationId = ExtractCorrelationId(parsed.Error.Message);
            }
        }
        catch
        {
            // ignore parsing errors - we still have raw body
        }

        var message = odataMessage ?? $"Business Central returned {(int)res.StatusCode} {res.StatusCode}.";

        return res.StatusCode switch
        {
            HttpStatusCode.NotFound => new BusinessCentralNotFoundException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BusinessCentralAuthException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            HttpStatusCode.BadRequest => new BusinessCentralValidationException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            _ => new BusinessCentralServerException(
                message, res.StatusCode, method, url, body, odataCode, correlationId)
        };
    }

    private static string? ExtractCorrelationId(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        const string marker = "CorrelationId:";

        var index = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        return message[(index + marker.Length)..]
            .Trim()
            .TrimEnd('.');
    }
}
