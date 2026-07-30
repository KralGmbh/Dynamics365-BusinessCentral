using System.Net.Http.Headers;

namespace Dynamics365.BusinessCentral.Client;

internal static class HttpRequestExtensions
{
    /// <summary>
    /// Derived from the assembly version rather than hard-coded, so outbound requests always
    /// identify the library version actually in use.
    /// </summary>
    private static readonly string UserAgent =
        $"Dynamics365.BusinessCentral.Client/{ClientVersion()}";

    private static string ClientVersion() =>
        typeof(HttpRequestExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static void AddJsonHeaders(this HttpRequestMessage request)
    {
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (request.Headers.UserAgent.Count == 0)
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

    /// <summary>
    /// Asks the server to cap pages at <paramref name="maxPageSize"/> rows via
    /// <c>Prefer: odata.maxpagesize</c>. The server clamps the value to its own configured
    /// maximum and continues via <c>@odata.nextLink</c> — the supported, deployment-portable
    /// way to pace a streaming read (verified against a live BC SaaS tenant).
    /// </summary>
    public static void AddMaxPageSizePreference(this HttpRequestMessage request, int maxPageSize)
    {
        request.Headers.TryAddWithoutValidation("Prefer", $"odata.maxpagesize={maxPageSize}");
    }
}
