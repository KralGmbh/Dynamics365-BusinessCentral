using Microsoft.Extensions.Options;

namespace Dynamics365.BusinessCentral.Options;

/// <summary>
/// Validates <see cref="BusinessCentralOptions"/> and reports every missing setting by
/// name, rather than collapsing the whole configuration into a single pass/fail.
/// </summary>
internal sealed class BusinessCentralOptionsValidator : IValidateOptions<BusinessCentralOptions>
{
    public ValidateOptionsResult Validate(string? name, BusinessCentralOptions options)
    {
        var failures = new List<string>();

        Require(options.TenantId, nameof(options.TenantId), failures);
        Require(options.ClientId, nameof(options.ClientId), failures);
        Require(options.ClientSecret, nameof(options.ClientSecret), failures);
        Require(options.Company, nameof(options.Company), failures);

        // These have defaults, so an empty value means the caller cleared them.
        Require(options.BaseUrl, nameof(options.BaseUrl), failures);
        Require(options.Scope, nameof(options.Scope), failures);
        Require(options.TokenEndpoint, nameof(options.TokenEndpoint), failures);
        Require(options.Environment, nameof(options.Environment), failures);

        // Validate the resolved URLs: an unsubstituted placeholder is the failure mode
        // this is really guarding against.
        RequireAbsoluteUrl(options.ResolvedBaseUrl, nameof(options.BaseUrl), failures);
        RequireAbsoluteUrl(options.ResolvedTokenEndpoint, nameof(options.TokenEndpoint), failures);

        if (options.MaxPageSize is < 1)
            failures.Add($"{nameof(BusinessCentralOptions)}.{nameof(options.MaxPageSize)} must be at least 1 when set.");

        // Upper bound matches HttpClient.Timeout's own maximum (int.MaxValue ms, ~24.8
        // days) — a larger value would pass here and then throw as a bare
        // ArgumentOutOfRangeException when the client is created.
        if (options.RequestTimeout is { } timeout &&
            (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(int.MaxValue)))
        {
            failures.Add(
                $"{nameof(BusinessCentralOptions)}.{nameof(options.RequestTimeout)} must be positive " +
                "and at most int.MaxValue milliseconds (~24.8 days, HttpClient's maximum) when set.");
        }

        if (options.MaxUrlLength is < 1)
            failures.Add($"{nameof(BusinessCentralOptions)}.{nameof(options.MaxUrlLength)} must be at least 1 when set.");

        if (options.UrlLengthWarningThreshold is < 1)
        {
            failures.Add(
                $"{nameof(BusinessCentralOptions)}.{nameof(options.UrlLengthWarningThreshold)} " +
                "must be at least 1 when set.");
        }

        // A threshold above the limit would never be reached before the throw, silently
        // costing the deployment the measurement window the two settings exist to create.
        if (options.UrlLengthWarningThreshold is { } warn &&
            options.MaxUrlLength is { } max &&
            warn > max)
        {
            failures.Add(
                $"{nameof(BusinessCentralOptions)}.{nameof(options.UrlLengthWarningThreshold)} ({warn}) " +
                $"must not exceed {nameof(options.MaxUrlLength)} ({max}) — a warning above the limit " +
                "can never be raised.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(string? value, string propertyName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add($"{nameof(BusinessCentralOptions)}.{propertyName} must not be empty.");
    }

    private static void RequireAbsoluteUrl(string? resolved, string propertyName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(resolved))
            return;

        if (resolved.Contains('{') || resolved.Contains('}'))
        {
            failures.Add(
                $"{nameof(BusinessCentralOptions)}.{propertyName} still contains an unsubstituted " +
                $"placeholder after resolution ('{resolved}'). Supported placeholders are " +
                $"{{tenant}} (or the historical {{TenantId}}) and {{environment}}.");
            return;
        }

        if (!Uri.TryCreate(resolved, UriKind.Absolute, out _))
            failures.Add($"{nameof(BusinessCentralOptions)}.{propertyName} must be an absolute URL.");
    }
}
