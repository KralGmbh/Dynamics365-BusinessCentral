using Dynamics365.BusinessCentral.Options;
using System.Net;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Errors;

/// <summary>
/// Translates a failed <see cref="HttpResponseMessage"/> into the matching
/// <see cref="BusinessCentralException"/> subtype, parsing Business Central's OData error
/// envelope for the message, error code and correlation ID.
/// </summary>
public static class BusinessCentralExceptionFactory
{
    private static readonly JsonSerializerOptions _jsonOptions = BusinessCentralJson.Options;

    /// <summary>
    /// Builds the exception describing <paramref name="res"/>. Never throws — an
    /// unparseable body simply leaves the structured fields null.
    /// </summary>
    /// <param name="res">The failed response.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<BusinessCentralException> CreateAsync(
        HttpResponseMessage res,
        CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

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

        BusinessCentralException exception = res.StatusCode switch
        {
            HttpStatusCode.NotFound => new BusinessCentralNotFoundException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BusinessCentralAuthException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            HttpStatusCode.BadRequest => new BusinessCentralValidationException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            HttpStatusCode.TooManyRequests => new BusinessCentralThrottledException(
                message, res.StatusCode, method, url, body, odataCode, correlationId),

            _ => new BusinessCentralServerException(
                message, res.StatusCode, method, url, body, odataCode, correlationId)
        };

        exception.RetryAfter = ReadRetryAfter(res);

        return exception;
    }

    /// <summary>
    /// Reads <c>Retry-After</c>, which Business Central may send either as a delay in
    /// seconds or as an absolute HTTP date.
    /// </summary>
    internal static TimeSpan? ReadRetryAfter(HttpResponseMessage res)
    {
        var retryAfter = res.Headers.RetryAfter;

        if (retryAfter == null)
            return null;

        if (retryAfter.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;

        if (retryAfter.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        return null;
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
