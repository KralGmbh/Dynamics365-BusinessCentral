using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.OData;
using System.Globalization;
using System.Text;

namespace Dynamics365.BusinessCentral.Client;

internal sealed class BusinessCentralUrlBuilder
{
    private readonly string _baseUrl;
    private readonly string _company;
    private readonly int? _maxLength;
    private readonly int? _warnLength;
    private readonly IBusinessCentralObserver _observer;

    public BusinessCentralUrlBuilder(
        string baseUrl,
        string company,
        int? maxLength = null,
        int? warnLength = null,
        IBusinessCentralObserver? observer = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _company = company;
        _maxLength = maxLength;
        _warnLength = warnLength;
        _observer = observer ?? new NullBusinessCentralObserver();
    }

    public string BuildEntityUrl(string path)
    {
        return Guard(EntityUrl(path));
    }

    public string BuildEntityUrl(string path, string key)
    {
        return Guard(EntityUrl(path, key));
    }

    public string BuildEntityUrl(string path, string key, IEnumerable<string>? select)
    {
        var url = EntityUrl(path, key);

        var fields = select?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (fields is { Count: > 0 })
            url += "?$select=" + string.Join(",", fields.Select(Uri.EscapeDataString));

        return Guard(url);
    }

    /// <summary>
    /// Builds a URL against the service root, i.e. without the <c>Company('...')</c>
    /// segment. Used for tenant-level entity sets such as the company list itself.
    /// </summary>
    public string BuildServiceRootUrl(string path)
    {
        return Guard($"{_baseUrl}/{EncodePath(path)}");
    }

    private string EntityUrl(string path) =>
        $"{BuildCompanyBase()}/{EncodePath(path)}";

    private string EntityUrl(string path, string key) =>
        $"{BuildCompanyBase()}/{EncodePath(path)}({EncodeKey(key)})";

    /// <summary>
    /// Builds a URL from a caller-supplied relative OData URL that may already carry
    /// its own query string. Path segments are encoded individually; everything after
    /// the first '?' is passed through verbatim because the caller owns it.
    /// </summary>
    public string BuildRawUrl(string path)
    {
        var split = path.IndexOf('?');

        if (split < 0)
            return Guard(EntityUrl(path));

        var pathPart = path[..split];
        var queryPart = path[(split + 1)..];

        return Guard($"{BuildCompanyBase()}/{EncodePath(pathPart)}?{queryPart}");
    }

    public string BuildQueryUrl(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select)
    {
        var url = EntityUrl(path);

        var query = new List<string>();

        // Filter — ODataFilter.MatchAll ("match every row") is emitted as no $filter at all.
        if (!string.IsNullOrWhiteSpace(filter) && filter != ODataFilter.MatchAll)
        {
            query.Add("$filter=" + Uri.EscapeDataString(filter));
        }

        // SELECT – only add if there are real fields
        if (select != null)
        {
            var fields = select
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (fields.Count > 0)
            {
                query.Add("$select=" + string.Join(",", fields.Select(Uri.EscapeDataString)));
            }
        }

        // Top
        if (options.Top != null)
        {
            query.Add("$top=" + options.Top);
        }

        // Skip
        if (options.Skip != null)
        {
            query.Add("$skip=" + options.Skip);
        }

        // OrderBy
        if (!string.IsNullOrWhiteSpace(options.OrderBy))
        {
            query.Add("$orderby=" + Uri.EscapeDataString(options.OrderBy));
        }

        // Expand — selectively encoded so nested syntax such as
        // "salesOrderLines($select=lineNo)" survives while unsafe characters
        // ('&', '#', '+', space, …) are still escaped.
        if (options.Expand.Count > 0)
        {
            query.Add("$expand=" + string.Join(",", options.Expand.Select(EncodeExpand)));
        }

        // Count
        if (options.IncludeCount)
        {
            query.Add("$count=true");
        }

        if (query.Count > 0)
        {
            url += "?" + string.Join("&", query);
        }

        return Guard(url);
    }

    /// <summary>
    /// Reports a URL that crossed the warning threshold and refuses one past the hard limit.
    /// </summary>
    /// <remarks>
    /// Guards only URLs this builder assembled. A server-issued <c>@odata.nextLink</c> never
    /// passes through here — it is sent verbatim, and the server's own limits already applied
    /// when it produced the link.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The URL is longer than the configured <c>MaxUrlLength</c>.
    /// </exception>
    private string Guard(string url)
    {
        // Nothing configured, or comfortably short: the overwhelming majority of calls.
        if (_warnLength is not { } warn || url.Length < warn)
            return _maxLength is { } onlyMax && url.Length > onlyMax
                ? throw new ArgumentException(BuildTooLongMessage(url, onlyMax))
                : url;

        var exceedsLimit = _maxLength is { } max && url.Length > max;

        // Warn before throwing, so a deployment measuring URL lengths records the outliers
        // that were rejected as well as the ones that were sent.
        _observer.OnUrlLengthWarning(new BusinessCentralUrlLengthInfo
        {
            Url = url,
            Length = url.Length,
            Threshold = warn,
            Limit = _maxLength,
            ExceedsLimit = exceedsLimit,
            OrClauseCount = CountOrClauses(url)
        });

        return exceedsLimit
            ? throw new ArgumentException(BuildTooLongMessage(url, _maxLength!.Value))
            : url;
    }

    private static string BuildTooLongMessage(string url, int limit)
    {
        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"This request produced a {url.Length:N0}-character URL; the configured limit ")
            .Append(CultureInfo.InvariantCulture, $"(BusinessCentralOptions.MaxUrlLength) is {limit:N0}. Business Central will ")
            .Append("reject it before it reaches the entity set, with a 400 or 404 that does not mention length.");

        var orClauses = CountOrClauses(url);

        // The dominant cause, and the one whose cost is least obvious: Filter.In renders a
        // same-field or-chain because BC rejects the OData 'in' operator, and each encoded
        // "(field eq 'value') or " runs ~4x the width of the "'value'," it replaces.
        if (orClauses >= 2)
        {
            message
                .Append(CultureInfo.InvariantCulture, $" The filter contains {orClauses:N0} 'or' clauses — Filter.In renders an ")
                .Append("or-chain rather than 'in (...)', which Business Central rejects, so a bulk key ")
                .Append("lookup approaches this limit roughly four times faster than the value count ")
                .Append("suggests. Chunk the values across several requests.");
        }

        return message.ToString();
    }

    /// <summary>
    /// Counts <c>or</c> clauses in a built URL. The filter is percent-encoded by the time it
    /// gets here, so the encoded spelling is the one that matches; the literal form is
    /// counted too because <see cref="BuildRawUrl"/> passes caller-supplied query strings
    /// through verbatim.
    /// </summary>
    private static int CountOrClauses(string url) =>
        CountOccurrences(url, "%20or%20") + CountOccurrences(url, " or ");

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private string BuildCompanyBase()
    {
        var encodedCompany = Uri.EscapeDataString(_company);
        return $"{_baseUrl}/Company('{encodedCompany}')";
    }

    /// <summary>
    /// Encodes each path segment separately so '/' keeps its meaning as a segment
    /// separator (navigation properties, nested paths) while spaces and other unsafe
    /// characters are still escaped.
    /// </summary>
    private static string EncodePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var segments = path.Split('/');

        for (var i = 0; i < segments.Length; i++)
            segments[i] = Uri.EscapeDataString(segments[i]);

        return string.Join("/", segments);
    }

    /// <summary>
    /// Encodes an OData entity key. Unlike <see cref="Uri.EscapeDataString(string)"/> this keeps
    /// the characters that make up OData key syntax — quotes, '=', ',' and parentheses —
    /// so alternate keys such as <c>No='1000'</c> survive intact, while spaces and other
    /// unsafe characters are still percent-encoded.
    /// </summary>
    private static string EncodeKey(string key) => EncodeSelectively(key, IsKeySafe);

    /// <summary>
    /// Encodes an <c>$expand</c> clause. Expand syntax needs its structural characters —
    /// parentheses, '$', '=', ',', ';', '/', quotes and '*' — preserved, so wholesale
    /// escaping is impossible; everything else ('&amp;', '#', '+', space, …) is
    /// percent-encoded so a value inside a nested <c>$filter</c> cannot break the URL.
    /// </summary>
    private static string EncodeExpand(string expand) => EncodeSelectively(expand, IsExpandSafe);

    /// <summary>
    /// Percent-encodes every character <paramref name="isSafe"/> rejects, leaving the rest
    /// literal. Unsafe characters are escaped a whole run at a time so surrogate pairs are
    /// never split across two <see cref="Uri.EscapeDataString(string)"/> calls.
    /// </summary>
    private static string EncodeSelectively(string value, Func<char, bool> isSafe)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = new StringBuilder(value.Length);
        var i = 0;

        while (i < value.Length)
        {
            if (isSafe(value[i]))
            {
                result.Append(value[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < value.Length && !isSafe(value[i]))
                i++;

            result.Append(Uri.EscapeDataString(value[start..i]));
        }

        return result.ToString();
    }

    private static bool IsKeySafe(char c) =>
        IsUnreserved(c) ||
        c is '\'' or '=' or ',' or '(' or ')';

    private static bool IsExpandSafe(char c) =>
        IsUnreserved(c) ||
        c is '\'' or '=' or ',' or '(' or ')' or '$' or ';' or '/' or '*';

    private static bool IsUnreserved(char c) =>
        c is >= 'A' and <= 'Z' ||
        c is >= 'a' and <= 'z' ||
        c is >= '0' and <= '9' ||
        c is '-' or '.' or '_' or '~';
}
