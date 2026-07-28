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
        Require(options.BaseUrl, nameof(options.BaseUrl), failures);
        Require(options.Company, nameof(options.Company), failures);
        Require(options.Scope, nameof(options.Scope), failures);
        Require(options.TokenEndpoint, nameof(options.TokenEndpoint), failures);

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"{nameof(options.BaseUrl)} must be an absolute URL.");
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
}
