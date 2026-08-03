using Dynamics365.BusinessCentral.Errors;
using System.Globalization;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Explains a <c>400</c> that a derived <c>$select</c> may have caused.
/// </summary>
/// <remarks>
/// <para>
/// Deriving the projection from the entity type's properties is normally
/// invisible, which is exactly what makes its failure mode confusing: a property that maps
/// to no Business Central column used to bind as its default and cost nothing, and now goes
/// into <c>$select</c> and fails the whole query before a single row is read. The server's
/// message names the column but cannot say why it was asked for, because the caller never
/// asked for it — <see cref="EntitySelect"/> did.
/// </para>
/// <para>
/// This closes that gap by naming the derivation, the property, and both escape hatches. The
/// hint is phrased conditionally: a <c>400</c> on a derived-select query is not proof the
/// projection caused it (a malformed <c>$filter</c> produces one too), so the text suggests
/// rather than asserts.
/// </para>
/// </remarks>
internal static class DerivedSelectHint
{
    /// <summary>
    /// Returns <paramref name="ex"/> re-wrapped with the hint appended to its message, all
    /// structured fields preserved, and the original as the inner exception.
    /// </summary>
    public static BusinessCentralValidationException Decorate<TEntity>(
        BusinessCentralValidationException ex,
        string[] derived)
    {
        var decorated = new BusinessCentralValidationException(
            $"{ex.ServerMessage} {BuildHint<TEntity>(ex, derived)}",
            ex.StatusCode,
            ex.Method,
            ex.RequestUrl,
            ex.ResponseBody,
            ex.ODataErrorCode,
            ex.CorrelationId,
            ex)
        {
            RetryAfter = ex.RetryAfter
        };

        return decorated;
    }

    private static string BuildHint<TEntity>(
        BusinessCentralValidationException ex,
        string[] derived)
    {
        var entity = typeof(TEntity).Name;
        var named = FindImplicatedField(ex, derived);

        var opening = string.Create(
            CultureInfo.InvariantCulture,
            $"This query sent a $select derived from {entity} ({derived.Length} properties, no explicit Select())");

        var subject = named is null
            ? "; if Business Central does not expose one of them as a column on this entity set, "
            : string.Create(CultureInfo.InvariantCulture, $", one of which is '{named}'; if Business Central does not expose that column on this entity set, ");

        return opening +
               subject +
               "mark the property [JsonIgnore] to drop it from the projection, or call SelectAll() " +
               "to send no $select at all. Note that $select is case-sensitive server-side even " +
               "though deserialization is not, so a [JsonPropertyName] whose casing drifts from " +
               "$metadata fails here while still deserializing correctly.";
    }

    /// <summary>
    /// Finds the derived field Business Central complained about. Its message quotes the
    /// offending name (<c>Could not find a property named 'systemCreatedAt' …</c>), so a
    /// quoted match is both the common case and the unambiguous one; the fallback tolerates
    /// other phrasings by requiring non-alphanumeric boundaries, which keeps a short name
    /// like <c>id</c> from matching inside <c>systemId</c>.
    /// </summary>
    /// <remarks>
    /// Matching ignores case on purpose: when the cause is casing drift, the name in the
    /// message is the one that was sent, and it must still be recognised as ours.
    /// </remarks>
    private static string? FindImplicatedField(BusinessCentralValidationException ex, string[] derived)
    {
        var haystack = ex.ServerMessage;

        if (string.IsNullOrWhiteSpace(haystack))
            haystack = ex.ResponseBody;

        if (string.IsNullOrWhiteSpace(haystack))
            return null;

        return Array.Find(derived, name => haystack.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase))
            ?? Array.Find(derived, name => ContainsAsWord(haystack, name));
    }

    private static bool ContainsAsWord(string haystack, string needle)
    {
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var after = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);

            if (before && after)
                return true;

            index = afterIndex;
        }

        return false;
    }
}
