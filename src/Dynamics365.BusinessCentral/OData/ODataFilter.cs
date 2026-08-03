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
    /// Re-renders this filter for a client that knows whether the endpoint accepts the OData
    /// <c>in</c> operator. <see langword="null"/> for every filter whose text does not depend
    /// on that, which is nearly all of them.
    /// </summary>
    private readonly Func<bool, string>? _render;

    /// <summary>
    /// The raw OData filter expression string, rendered without knowledge of the endpoint's
    /// schema version.
    /// </summary>
    /// <remarks>
    /// For a membership filter left at <see cref="ODataInStyle.Auto"/> this is the portable
    /// <c>or</c>-chain, because a value read outside a configured client cannot know whether
    /// <c>in</c> would be accepted. A client that <i>does</i> know renders through
    /// <see cref="Render"/> instead, so what goes on the wire may be the shorter form.
    /// </remarks>
    public string Value { get; }

    /// <summary>
    /// Whether this filter's text depends on the endpoint's schema version, i.e. whether
    /// <see cref="Render"/> can differ from <see cref="Value"/>.
    /// </summary>
    internal bool IsSchemaSensitive => _render is not null;

    internal ODataFilter(string value)
    {
        Value = value;
    }

    internal ODataFilter(string value, Func<bool, string> render)
    {
        Value = value;
        _render = render;
    }

    /// <summary>
    /// Renders the filter for an endpoint that does (<paramref name="nativeIn"/>) or does not
    /// accept the OData <c>in</c> operator.
    /// </summary>
    internal string Render(bool nativeIn) => _render?.Invoke(nativeIn) ?? Value;

    /// <summary>
    /// Returns the underlying OData filter string.
    /// </summary>
    public override string ToString() => Value;
}
