using Dynamics365.BusinessCentral.OData;
using System.Text;

namespace Dynamics365.BusinessCentral.Client;

internal sealed class BusinessCentralUrlBuilder
{
    private readonly string _baseUrl;
    private readonly string _company;

    public BusinessCentralUrlBuilder(string baseUrl, string company)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _company = company;
    }

    public string BuildEntityUrl(string path)
    {
        return $"{BuildCompanyBase()}/{EncodePath(path)}";
    }

    public string BuildEntityUrl(string path, string key)
    {
        return $"{BuildCompanyBase()}/{EncodePath(path)}({EncodeKey(key)})";
    }

    public string BuildEntityUrl(string path, string key, IEnumerable<string>? select)
    {
        var url = BuildEntityUrl(path, key);

        var fields = select?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (fields is { Count: > 0 })
            url += "?$select=" + string.Join(",", fields.Select(Uri.EscapeDataString));

        return url;
    }

    /// <summary>
    /// Builds a URL against the service root, i.e. without the <c>Company('...')</c>
    /// segment. Used for tenant-level entity sets such as the company list itself.
    /// </summary>
    public string BuildServiceRootUrl(string path)
    {
        return $"{_baseUrl}/{EncodePath(path)}";
    }

    /// <summary>
    /// Builds a URL from a caller-supplied relative OData URL that may already carry
    /// its own query string. Path segments are encoded individually; everything after
    /// the first '?' is passed through verbatim because the caller owns it.
    /// </summary>
    public string BuildRawUrl(string path)
    {
        var split = path.IndexOf('?');

        if (split < 0)
            return BuildEntityUrl(path);

        var pathPart = path[..split];
        var queryPart = path[(split + 1)..];

        return $"{BuildCompanyBase()}/{EncodePath(pathPart)}?{queryPart}";
    }

    public string BuildQueryUrl(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select)
    {
        var url = BuildEntityUrl(path);

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

        return url;
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
