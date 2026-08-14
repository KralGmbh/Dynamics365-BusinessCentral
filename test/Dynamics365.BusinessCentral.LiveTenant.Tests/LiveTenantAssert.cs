namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// Assertions for facts that compare two reads of a collection nobody froze.
/// </summary>
/// <remarks>
/// <para>
/// Every comparison in this suite spans more than one request against a tenant that other people
/// and other systems are using. A row inserted or deleted between those requests moves the second
/// number, and an exact comparison then fails while the package was working perfectly — a flake
/// that looks exactly like the regression the fact exists to catch, which is the worst kind of
/// flake to own.
/// </para>
/// <para>
/// The answer is to measure the movement instead of guessing a tolerance: read the moving quantity
/// either side of the thing under test and require the result to land inside that bracket. When
/// nothing changed the bracket is a point and the check is exact; when something did, the tolerance
/// is the size of what actually happened. A real defect still fails, because it moves the result
/// far outside a window that only ever spans concurrent tenant activity.
/// </para>
/// </remarks>
internal static class LiveTenantAssert
{
    /// <summary>
    /// Requires <paramref name="actual"/> to lie between the two measurements, in either order.
    /// </summary>
    /// <param name="first">The quantity measured before the thing under test.</param>
    /// <param name="second">The same quantity, measured after it.</param>
    /// <param name="actual">What the thing under test produced.</param>
    /// <param name="subject">
    /// Names the comparison in the failure message — this suite runs unattended, so the message is
    /// the whole diagnosis.
    /// </param>
    public static void WithinBracket(long first, long second, long actual, string subject)
    {
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);

        Assert.True(
            actual >= low && actual <= high,
            $"{subject}: got {actual}, outside the {low}..{high} measured either side of it. " +
            $"A collection that changed mid-read explains any result inside that bracket; " +
            $"nothing outside it is explained by tenant activity.");
    }

    /// <summary>
    /// Requires a set read between two reference reads to contain every member stable across the
    /// bracket and no member absent from both sides of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same instrument as <see cref="WithinBracket"/> for a comparison of identity rather than
    /// size, and the two halves are not symmetric. A member both reference reads saw is one the
    /// tenant held throughout, so its absence is the reading's fault and the check
    /// <i>requires</i> it. A member only one of them saw appeared or disappeared while the three
    /// reads ran, so either answer about it is defensible and the check <i>permits</i> it. What is
    /// left is the finding: a member neither reference read ever contained, which no amount of
    /// concurrent activity explains.
    /// </para>
    /// <para>
    /// The offending members are named, not counted. This suite runs unattended and weekly, so a
    /// failure that says only "one was missing" costs a reproduction; a failure that says which
    /// one is usually the whole diagnosis.
    /// </para>
    /// <para>
    /// Membership is decided by <typeparamref name="T"/>'s default equality, not by the comparer
    /// the caller's sets were built with — enough for the identifiers this suite compares, and
    /// worth knowing before passing a case-insensitive set of strings.
    /// </para>
    /// </remarks>
    public static void SetWithinBracket<T>(
        IReadOnlySet<T> first,
        IReadOnlySet<T> second,
        IReadOnlySet<T> actual,
        string subject)
    {
        var required = first.Intersect(second).ToHashSet();
        var permitted = first.Union(second).ToHashSet();
        var missing = required.Except(actual).ToArray();
        var unexpected = actual.Except(permitted).ToArray();

        Assert.True(
            missing.Length == 0 && unexpected.Length == 0,
            $"{subject}: {Describe(missing, "stable member(s) missing")}, " +
            $"{Describe(unexpected, "member(s) absent from both surrounding reads")}. " +
            $"The reference reads measured {first.Count}/{second.Count} members around " +
            $"{actual.Count} actual members.");
    }

    /// <summary>Names up to five offenders, so a failure diagnoses itself.</summary>
    private static string Describe<T>(T[] members, string what)
    {
        if (members.Length == 0)
            return $"0 {what}";

        var named = string.Join(", ", members.Take(5));
        var rest = members.Length > 5 ? $", and {members.Length - 5} more" : string.Empty;

        return $"{members.Length} {what} ({named}{rest})";
    }
}
