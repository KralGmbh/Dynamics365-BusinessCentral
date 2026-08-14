namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>The live-data bracket semantics, tested without contacting a tenant.</summary>
/// <remarks>
/// Plain <see cref="FactAttribute"/> for the same reason as <see cref="GuardEnvironmentTests"/>:
/// the bracket is what decides whether a live failure is real, so its own rules must not skip in
/// the conditions that make it matter. These need no credentials.
/// </remarks>
public sealed class LiveTenantAssertTests
{
    [Fact]
    public void Set_bracket_accepts_members_that_appear_on_only_one_side()
    {
        var exception = Record.Exception(() =>
            LiveTenantAssert.SetWithinBracket(
                new HashSet<int> { 1, 2 },
                new HashSet<int> { 2, 3 },
                new HashSet<int> { 1, 2, 3 },
                "result"));

        Assert.Null(exception);
    }

    [Fact]
    public void Set_bracket_rejects_a_missing_stable_member()
    {
        var exception = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            LiveTenantAssert.SetWithinBracket(
                new HashSet<int> { 1, 2 },
                new HashSet<int> { 2, 3 },
                new HashSet<int> { 1, 3 },
                "result"));

        // Named, not just counted — the identity is the diagnosis.
        Assert.Contains("1 stable member(s) missing (2)", exception.Message);
    }

    [Fact]
    public void Set_bracket_rejects_a_member_absent_from_both_reads()
    {
        var exception = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            LiveTenantAssert.SetWithinBracket(
                new HashSet<int> { 1, 2 },
                new HashSet<int> { 2, 3 },
                new HashSet<int> { 2, 4 },
                "result"));

        Assert.Contains("1 member(s) absent from both surrounding reads (4)", exception.Message);
    }

    /// <summary>
    /// The numeric bracket collapses to an exact comparison when nothing moved, which is the
    /// property that keeps a quiet tenant from silently loosening the check.
    /// </summary>
    [Fact]
    public void Numeric_bracket_is_exact_when_the_two_reads_agree()
    {
        Assert.Null(Record.Exception(() => LiveTenantAssert.WithinBracket(100, 100, 100, "count")));
        Assert.Throws<Xunit.Sdk.TrueException>(
            () => LiveTenantAssert.WithinBracket(100, 100, 101, "count"));
    }

    /// <summary>And it accepts either order, since which read is larger is not knowable up front.</summary>
    [Fact]
    public void Numeric_bracket_spans_the_two_reads_in_either_order()
    {
        Assert.Null(Record.Exception(() => LiveTenantAssert.WithinBracket(100, 104, 102, "count")));
        Assert.Null(Record.Exception(() => LiveTenantAssert.WithinBracket(104, 100, 102, "count")));
        Assert.Throws<Xunit.Sdk.TrueException>(
            () => LiveTenantAssert.WithinBracket(104, 100, 99, "count"));
    }
}
