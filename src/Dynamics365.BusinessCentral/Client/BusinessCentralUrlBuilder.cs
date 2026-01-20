using Dynamics365.BusinessCentral.OData;

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
        return $"{BuildCompanyBase()}/{Encode(path)}";
    }

    public string BuildEntityUrl(string path, string key)
    {
        return $"{BuildCompanyBase()}/{Encode(path)}({Encode(key)})";
    }

    public string BuildQueryUrl(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select)
    {
        var url = BuildEntityUrl(path);

        var query = new List<string>();

        // Filter
        if (!string.IsNullOrWhiteSpace(filter) && filter != "true")
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

    private static string Encode(string value)
    {
        return Uri.EscapeDataString(value);
    }
}
