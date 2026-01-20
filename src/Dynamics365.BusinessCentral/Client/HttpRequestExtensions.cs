using System.Net.Http.Headers;

namespace Dynamics365.BusinessCentral.Client;

internal static class HttpRequestExtensions
{
    public static HttpRequestMessage Clone(this HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (original.Content != null)
            clone.Content = original.Content;

        return clone;
    }

    public static void AddJsonHeaders(this HttpRequestMessage request)
    {
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
