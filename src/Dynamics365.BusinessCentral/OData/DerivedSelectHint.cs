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
/// <para>
/// It names exactly one cause — a property that is not a column — because that is the only
/// one measured to produce this error. Do not add a casing explanation: <c>$select</c> was
/// measured case-<b>insensitive</b> on Business Central SaaS, so casing drift cannot be the
/// cause, and a hint naming it would misdirect every real occurrence away from the answer the
/// server already supplied. See <see cref="EntitySelect"/>.
/// </para>
/// </remarks>
internal static class DerivedSelectHint
{
    /// <summary>
    /// Whether the derived projection could plausibly be the cause of this <c>400</c>, and the
    /// hint is therefore worth appending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hint suggests rather than asserts, so it is right for it to fire when nothing is
    /// known. It is <b>not</b> right for it to fire when the server has already named a
    /// specific column and that column is not one we asked for: there the reader was handed
    /// the answer, and a competing explanation about the projection can only pull them off it.
    /// </para>
    /// <para>
    /// The discriminator is Business Central's own documented message shape, which
    /// <see cref="FindImplicatedField"/> already relies on — it quotes the names it is
    /// complaining about (<c>Could not find a property named 'x' on type 'y'</c>). So: no
    /// quoted name means no evidence either way, and the hint stands; quoted names that
    /// include one of ours means the projection is implicated, and the hint names it; quoted
    /// names that include none of ours means the server is complaining about something the
    /// projection did not send — a <c>$filter</c> or <c>$orderby</c> column, typically — and
    /// the hint is suppressed.
    /// </para>
    /// <para>
    /// Deliberately keyed on the message rather than on an allow-list of
    /// <c>ODataErrorCode</c> values. Codes that name a non-projection cause certainly exist,
    /// but none is recorded in this repository and none has been measured here; hard-coding
    /// names that cannot be verified is how a hint ends up asserting something Business
    /// Central does not do. The quoted-name rule needs no such list.
    /// </para>
    /// </remarks>
    public static bool CouldExplain(
        BusinessCentralValidationException ex,
        IReadOnlyList<string> derived)
    {
        var haystack = Haystack(ex);

        if (string.IsNullOrWhiteSpace(haystack))
            return true;

        var quoted = QuotedNames(haystack);

        // Nothing specific named: the hint's conditional wording is still the best available.
        if (quoted.Count == 0)
            return true;

        return quoted.Any(name =>
            derived.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The single-quoted tokens in a server message. Business Central quotes both the property
    /// it could not find and the type it looked on, so a match against the derived list — not
    /// the mere presence of a quoted token — is what implicates the projection.
    /// </summary>
    private static List<string> QuotedNames(string message)
    {
        var names = new List<string>();
        var index = 0;

        while (index < message.Length)
        {
            var open = message.IndexOf('\'', index);
            if (open < 0)
                break;

            var close = message.IndexOf('\'', open + 1);
            if (close < 0)
                break;

            var token = message[(open + 1)..close];

            if (token.Length > 0)
                names.Add(token);

            index = close + 1;
        }

        return names;
    }

    private static string? Haystack(BusinessCentralValidationException ex) =>
        string.IsNullOrWhiteSpace(ex.ServerMessage) ? ex.ResponseBody : ex.ServerMessage;

    /// <summary>
    /// Returns <paramref name="ex"/> re-wrapped with the hint appended to its message, all
    /// structured fields preserved, and the original as the inner exception.
    /// </summary>
    public static BusinessCentralValidationException Decorate<TEntity>(
        BusinessCentralValidationException ex,
        IReadOnlyList<string> derived)
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
        IReadOnlyList<string> derived)
    {
        var entity = typeof(TEntity).Name;
        var named = FindImplicatedField(ex, derived);

        var opening = string.Create(
            CultureInfo.InvariantCulture,
            $"This query sent a $select derived from {entity} ({derived.Count} properties, no explicit Select())");

        var subject = named is null
            ? "; if Business Central does not expose one of them as a column on this entity set, "
            : string.Create(CultureInfo.InvariantCulture, $", one of which is '{named}'; if Business Central does not expose that column on this entity set, ");

        // Deliberately names one cause and stops. An earlier version added a sentence about
        // $select being case-sensitive server-side; measurement against a live SaaS tenant
        // showed it is not (three spellings of one column all returned 200), so the sentence
        // pointed every real occurrence at a cause that cannot produce this error — worse
        // than no hint, since the server's own message already gives the right answer.
        return opening +
               subject +
               "mark the property [JsonIgnore] to drop it from the projection, or call SelectAll() " +
               "to send no $select at all.";
    }

    /// <summary>
    /// Finds the derived field Business Central complained about. Its message quotes the
    /// offending name (<c>Could not find a property named 'systemCreatedAt' …</c>), so a
    /// quoted match is both the common case and the unambiguous one; the fallback tolerates
    /// other phrasings by requiring non-alphanumeric boundaries, which keeps a short name
    /// like <c>id</c> from matching inside <c>systemId</c>.
    /// </summary>
    /// <remarks>
    /// An ordinal match would in fact be sufficient against Business Central SaaS, which was
    /// measured to echo the requested name back verbatim rather than substituting its own
    /// canonical casing. Matching ignores case anyway, purely as insurance against a
    /// deployment that does substitute: the cost of being wrong is a hint that says "one of
    /// them" instead of naming the field, and that is a better failure than silence.
    /// </remarks>
    private static string? FindImplicatedField(
        BusinessCentralValidationException ex,
        IReadOnlyList<string> derived)
    {
        var haystack = Haystack(ex);

        if (string.IsNullOrWhiteSpace(haystack))
            return null;

        return derived.FirstOrDefault(name => haystack.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase))
            ?? derived.FirstOrDefault(name => ContainsAsWord(haystack, name));
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
