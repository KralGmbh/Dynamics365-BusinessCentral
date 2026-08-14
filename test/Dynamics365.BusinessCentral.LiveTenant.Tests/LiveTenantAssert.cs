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
}
