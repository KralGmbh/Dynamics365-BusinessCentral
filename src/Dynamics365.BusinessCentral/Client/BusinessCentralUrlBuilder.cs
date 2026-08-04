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

    private readonly string? _schemaVersion;

    public BusinessCentralUrlBuilder(
        string baseUrl,
        string company,
        int? maxLength = null,
        int? warnLength = null,
        IBusinessCentralObserver? observer = null,
        string? schemaVersion = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _company = company;
        _maxLength = maxLength;
        _warnLength = warnLength;
        _observer = observer ?? new NullBusinessCentralObserver();
        _schemaVersion = string.IsNullOrWhiteSpace(schemaVersion) ? null : schemaVersion.Trim();
    }

    public string BuildEntityUrl(string path)
    {
        return Finish(EntityUrl(path));
    }

    public string BuildEntityUrl(string path, string key)
    {
        return Finish(EntityUrl(path, key));
    }

    public string BuildEntityUrl(string path, string key, IEnumerable<string>? select)
    {
        var url = EntityUrl(path, key);

        var fields = select?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (fields is { Count: > 0 })
            url += "?$select=" + string.Join(",", fields.Select(Uri.EscapeDataString));

        return Finish(url);
    }

    /// <summary>
    /// Builds a URL against the service root, i.e. without the <c>Company('...')</c>
    /// segment. Used for tenant-level entity sets such as the company list itself.
    /// </summary>
    public string BuildServiceRootUrl(string path)
    {
        return Finish($"{_baseUrl}/{EncodePath(path)}");
    }

    /// <summary>
    /// Builds the service-document metadata URL. Deliberately not routed through
    /// <see cref="BuildServiceRootUrl"/>: <c>EncodePath</c> would percent-encode the leading
    /// '$' into <c>%24metadata</c>, which Business Central does not recognise. Like the
    /// company list, this lives at the service root — <c>$metadata</c> describes the whole
    /// tenant, not one company.
    /// </summary>
    public string BuildMetadataUrl() => Finish($"{_baseUrl}/$metadata");

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
            return Finish(EntityUrl(path));

        var pathPart = path[..split];
        var queryPart = path[(split + 1)..];

        return Finish($"{BuildCompanyBase()}/{EncodePath(pathPart)}?{queryPart}");
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

        // Schema version — gates the filter features Business Central documents as 2.1-only,
        // the 'in' operator among them. Last so the interesting parts of a URL stay readable
        // at the front when it turns up in a log.
        if (_schemaVersion is not null)
        {
            query.Add("$schemaversion=" + Uri.EscapeDataString(_schemaVersion));
        }

        if (query.Count > 0)
        {
            url += "?" + string.Join("&", query);
        }

        return Guard(url);
    }

    /// <summary>
    /// Completes any URL that did not compose its own query list: adds the schema version,
    /// then guards the length.
    /// </summary>
    /// <remarks>
    /// Every builder goes through here or through <see cref="BuildQueryUrl"/>. A schema version
    /// that reached only list queries would leave reads-by-key, writes, the company list, raw
    /// queries and <c>$metadata</c> running under a different contract from the rest of the
    /// client — silently, and differently per method.
    /// </remarks>
    private string Finish(string url) => Guard(AppendSchemaVersion(url));

    private string AppendSchemaVersion(string url)
    {
        if (_schemaVersion is null)
            return url;

        // BuildRawUrl passes a caller-owned query string through verbatim; if they already
        // stated a version, theirs wins rather than being contradicted by a second one.
        if (url.Contains("$schemaversion=", StringComparison.OrdinalIgnoreCase))
            return url;

        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        return $"{url}{separator}$schemaversion={Uri.EscapeDataString(_schemaVersion)}";
    }

    /// <summary>
    /// Reports a request whose query string crossed the warning threshold and refuses one past
    /// the hard limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measures the <b>query string</b>, not the whole URL. Measured against a live SaaS
    /// tenant, the gateway's ceiling sits at 8,099 accepted query-string characters and holds
    /// still across environments while the full URL does not: the prefix varies with
    /// environment name, company name (<c>Company('KRAL%20AG')</c> — spaces inflate it) and
    /// entity-set path, so a full-URL limit is simultaneously too strict on deployments with
    /// long prefixes and too loose on short ones. No single full-URL default is portable; a
    /// query-string one is.
    /// </para>
    /// <para>
    /// Guards only URLs this builder assembled. A server-issued <c>@odata.nextLink</c> never
    /// passes through here — it is sent verbatim, and the server's own limits already applied
    /// when it produced the link.
    /// </para>
    /// </remarks>
    /// <exception cref="Errors.BusinessCentralUrlTooLongException">
    /// The query string is longer than the configured <c>MaxQueryStringLength</c>. A
    /// <see cref="Errors.BusinessCentralException"/> rather than an <see cref="ArgumentException"/>,
    /// so <c>catch (BusinessCentralException)</c> still sees every failure the client produces —
    /// this one is data-dependent, not a malformed argument: the same call site is fine for 20
    /// keys and refused for 200.
    /// </exception>
    private string Guard(string url)
    {
        var queryLength = QueryStringLength(url);

        // Nothing configured, or comfortably short: the overwhelming majority of calls.
        if (_warnLength is not { } warn || queryLength < warn)
            return _maxLength is { } onlyMax && queryLength > onlyMax
                ? throw TooLong(url, queryLength, onlyMax)
                : url;

        var exceedsLimit = _maxLength is { } max && queryLength > max;

        // Warn before throwing, so a deployment measuring lengths records the outliers that
        // were rejected as well as the ones that were sent.
        _observer.OnUrlLengthWarning(new BusinessCentralUrlLengthInfo
        {
            Url = url,
            UrlLength = url.Length,
            QueryStringLength = queryLength,
            Threshold = warn,
            Limit = _maxLength,
            ExceedsLimit = exceedsLimit,
            OrClauseCount = CountOrClauses(url)
        });

        return exceedsLimit
            ? throw TooLong(url, queryLength, _maxLength!.Value)
            : url;
    }

    private static Errors.BusinessCentralUrlTooLongException TooLong(string url, int queryLength, int limit) =>
        new(BuildTooLongMessage(url, queryLength, limit),
            url,
            url.Length,
            queryLength,
            limit,
            CountOrClauses(url));

    /// <summary>
    /// Length of everything after the first <c>?</c>, or <c>0</c> when there is no query string.
    /// </summary>
    private static int QueryStringLength(string url) => QueryString(url).Length;

    /// <summary>
    /// Everything after the first <c>?</c>, or empty when there is no query string.
    /// </summary>
    private static string QueryString(string url)
    {
        var split = url.IndexOf('?', StringComparison.Ordinal);

        return split < 0 ? string.Empty : url[(split + 1)..];
    }

    private static string BuildTooLongMessage(string url, int queryLength, int limit)
    {
        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"This request produced a {queryLength:N0}-character query string ")
            .Append(CultureInfo.InvariantCulture, $"(in a {url.Length:N0}-character URL); the configured limit ")
            .Append(CultureInfo.InvariantCulture, $"(BusinessCentralOptions.MaxQueryStringLength) is {limit:N0}. Business Central's ")
            .Append("gateway answers 414 URI Too Long past its own ceiling, before the request reaches the entity set.");

        var orClauses = CountOrClauses(url);

        // The dominant cause, and the one whose cost is least obvious: Filter.In renders a
        // same-field or-chain because BC gates the OData 'in' operator on schema version 2.1,
        // and each encoded clause costs roughly twice what the value it replaces would.
        // For an 8-character key, "(no eq 'EBH00000') or " encodes to 38 characters against
        // 17 for the "'EBH00000'," it replaces.
        if (orClauses >= 2)
        {
            message
                .Append(CultureInfo.InvariantCulture, $" The filter contains {orClauses:N0} 'or' clauses — Filter.In renders an ")
                .Append("or-chain rather than 'in (...)', which Business Central rejects, so a bulk key ")
                .Append("lookup approaches this limit about twice as fast as the value count ")
                .Append("suggests. Chunk the values across several requests.");
        }

        return message.ToString();
    }

    /// <summary>
    /// Counts <c>or</c> clauses inside the <c>$filter</c> parameter. The filter is
    /// percent-encoded by the time it gets here, so the encoded spelling is the one that
    /// matches; the literal form is counted too because <see cref="BuildRawUrl"/> passes
    /// caller-supplied query strings through verbatim.
    /// </summary>
    /// <remarks>
    /// Scoped to <c>$filter</c> rather than run over the whole URL: a company name, entity-set
    /// path or <c>$orderby</c> column containing a standalone "or" would otherwise inflate the
    /// count and make the exception message blame <c>Filter.In</c> for a query that never used
    /// it. The count exists to name a cause, so a false one is worse than none.
    /// </remarks>
    private static int CountOrClauses(string url)
    {
        var query = QueryString(url);

        if (query.Length == 0)
            return 0;

        foreach (var parameter in query.Split('&'))
        {
            if (!parameter.StartsWith("$filter=", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = parameter["$filter=".Length..];

            return CountOccurrences(value, "%20or%20") + CountOccurrences(value, " or ");
        }

        return 0;
    }

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
