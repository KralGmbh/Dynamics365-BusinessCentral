using System.Net.Http.Headers;

namespace Dynamics365.BusinessCentral.Client;

internal static class HttpRequestExtensions
{
    private const string UserAgent = "Dynamics365.BusinessCentral.Client/1.0";

    public static HttpRequestMessage Clone(this HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version
        };

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        // Shared by reference: every payload this client sends is a buffered
        // ByteArrayContent/StringContent, which can be re-sent on the 401 retry.
        if (original.Content != null)
            clone.Content = original.Content;

        return clone;
    }

    public static void AddJsonHeaders(this HttpRequestMessage request)
    {
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (!request.Headers.UserAgent.Any())
            request.Headers.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// Asks Business Central to return the affected entity in the response body so a
    /// write does not come back as a bare 204 NoContent.
    /// </summary>
    public static void AddReturnRepresentationPreference(this HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
    }
}
