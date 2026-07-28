using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// A company exposed by the Business Central tenant.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is the value to pass to <c>ForCompany</c> and is always populated.
/// The remaining properties depend on which endpoint is configured and are
/// <see langword="null"/> when the service does not return them.
/// </remarks>
public sealed class BusinessCentralCompany
{
    /// <summary>Company name, as used in the <c>Company('...')</c> URL segment.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable company name, when the endpoint exposes one.</summary>
    [JsonPropertyName("Display_Name")]
    public string? DisplayName { get; set; }

    /// <summary>Company system ID, when the endpoint exposes one.</summary>
    [JsonPropertyName("Id")]
    public Guid? Id { get; set; }

    /// <summary>Whether this is an evaluation company, when the endpoint exposes it.</summary>
    [JsonPropertyName("Evaluation_Company")]
    public bool? IsEvaluationCompany { get; set; }

    /// <inheritdoc />
    public override string ToString() => DisplayName is null ? Name : $"{Name} ({DisplayName})";
}
