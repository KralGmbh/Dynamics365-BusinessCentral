namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Represents an immutable OData $filter expression.
/// Instances are created via the <see cref="Filter"/> factory and combined via extension methods.
/// </summary>
public sealed class ODataFilter
{
    /// <summary>
    /// The rendered form of <see cref="Filter.All"/>: a filter matching every row, which the
    /// URL builder emits as no <c>$filter</c> at all. The single definition of that contract —
    /// both <see cref="Filter"/> and the URL builder reference this constant. The same value
    /// passed as a raw filter string keeps the same meaning, which is pinned by tests.
    /// </summary>
    internal const string MatchAll = "true";

    /// <summary>
    /// The raw OData filter expression string.
    /// </summary>
    public string Value { get; }

    internal ODataFilter(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Returns the underlying OData filter string.
    /// </summary>
    public override string ToString() => Value;
}