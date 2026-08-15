namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// Credentials for the app registration these tests authenticate with.
/// </summary>
/// <remarks>
/// <para>
/// The registration is granted access on the sandbox environment's <i>Microsoft Entra
/// applications</i> page and deliberately never added to Production's, so a token issued for it
/// is rejected at Production's door. That grant — not any code in this repository — is the
/// control that makes these tests safe to run.
/// </para>
/// <para>
/// Never commit these. The repository ignores <c>.env/</c>; CI supplies them as secrets.
/// </para>
/// </remarks>
public sealed record LiveTenantCredentials(string TenantId, string ClientId, string ClientSecret)
{
    /// <summary>Gitignored, <c>chmod 600</c>, and the only on-disk copy.</summary>
    private const string EnvFile = ".env/dev-tenant.md";

    /// <summary>
    /// Loads from environment variables first (CI), then from the gitignored local file
    /// (developer machines). Returns <see langword="null"/> when neither is complete — which is
    /// the normal state for a contributor without a tenant, and is what makes the live facts
    /// skip rather than fail.
    /// </summary>
    public static LiveTenantCredentials? TryLoad()
    {
        var fromEnvironment = FromEnvironment();
        if (fromEnvironment is not null)
            return fromEnvironment;

        var file = FindEnvFile();
        if (file is null)
            return null;

        var values = Parse(File.ReadAllLines(file));

        values.TryGetValue("TenantId", out var tenantId);
        values.TryGetValue("ClientId", out var clientId);

        // The local file spells it "Client-Secret"; the environment spells it BC_CLIENT_SECRET.
        // Accept either here so the two sources stay interchangeable.
        if (!values.TryGetValue("Client-Secret", out var clientSecret))
            values.TryGetValue("ClientSecret", out clientSecret);

        return IsComplete(tenantId, clientId, clientSecret)
            ? new LiveTenantCredentials(tenantId!, clientId!, clientSecret!)
            : null;
    }

    private static LiveTenantCredentials? FromEnvironment()
    {
        var tenantId = Environment.GetEnvironmentVariable("BC_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("BC_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("BC_CLIENT_SECRET");

        return IsComplete(tenantId, clientId, clientSecret)
            ? new LiveTenantCredentials(tenantId!, clientId!, clientSecret!)
            : null;
    }

    private static bool IsComplete(string? tenantId, string? clientId, string? clientSecret) =>
        !string.IsNullOrWhiteSpace(tenantId) &&
        !string.IsNullOrWhiteSpace(clientId) &&
        !string.IsNullOrWhiteSpace(clientSecret);

    /// <summary>
    /// Tolerates both <c>Key: value</c> and <c>Key=value</c>, splitting on the first separator
    /// only — a client secret can contain either character.
    /// </summary>
    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOfAny([':', '=']);
            if (separator <= 0)
                continue;

            values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return values;
    }

    /// <summary>
    /// Walks up from the test binaries to the repository root, and <b>stops there</b>.
    /// </summary>
    /// <remarks>
    /// The stop is the point. An unbounded walk keeps climbing past the repository into whatever
    /// workspace directory happens to contain it, so a checkout with no credentials of its own
    /// could silently authenticate with an unrelated sibling project's tenant instead of skipping
    /// as documented. The repository root is the directory holding <c>.git</c>; it is checked for
    /// the credential file, and the walk ends there whether or not one is found.
    /// </remarks>
    private static string? FindEnvFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, EnvFile);
            if (File.Exists(candidate))
                return candidate;

            // Path.Exists rather than Directory.Exists: .git is a file in a worktree or submodule.
            if (Path.Exists(Path.Combine(directory.FullName, ".git")))
                return null;

            directory = directory.Parent;
        }

        return null;
    }
}
