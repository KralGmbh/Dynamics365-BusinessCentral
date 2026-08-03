using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins the derived-<c>$select</c> failure explanation.
/// </summary>
/// <remarks>
/// A property that maps to no Business Central column used to bind as its default and cost
/// nothing; under F2 it enters <c>$select</c> and fails the whole request with a <c>400</c>.
/// That is breakage the package creates, not latent drift it surfaces, so the exception has
/// to say where the projection came from and how to opt out — the server's message names the
/// column but cannot explain why it was asked for.
/// </remarks>
public class DerivedSelectDiagnosticsTests : TestBase
{
    /// <summary>
    /// The shape that motivated this: system fields on a shared base class, inherited by
    /// entities whose published pages do not all expose them.
    /// </summary>
    public abstract class SystemFieldsEntity
    {
        public DateTimeOffset SystemCreatedAt { get; set; }

        public DateTimeOffset SystemModifiedAt { get; set; }
    }

    public sealed class LdatSummary : SystemFieldsEntity
    {
        public string No { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Shaped after a real Business Central SaaS response: the server quotes the name it was
    /// sent, verbatim, and never substitutes its own canonical casing. Our derived wire name
    /// for <c>SystemCreatedAt</c> is <c>systemCreatedAt</c>, so that is what comes back.
    /// </summary>
    private const string MissingColumnBody =
        """
        {"error":{"code":"BadRequest_NotFound",
         "message":"Could not find a property named 'systemCreatedAt' on type 'NAV.LdatSummary'."}}
        """;

    private static Func<HttpRequestMessage, HttpResponseMessage> BadRequest(string body) =>
        WithToken(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body)
        });

    #region The hint

    [Fact]
    public async Task Derived_Select_400_Names_The_Implicated_Property()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Contains("systemCreatedAt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Derived_Select_400_Explains_Where_The_Projection_Came_From()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Contains("$select derived from LdatSummary", ex.Message, StringComparison.Ordinal);
        Assert.Contains("4 properties", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Derived_Select_400_Names_Both_Escape_Hatches()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Contains("[JsonIgnore]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SelectAll()", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression guard for M1 (`METADATA-PROBE-FINDINGS-BASTION.md`). Alpha.6 and alpha.7
    /// both claimed <c>$select</c> was case-sensitive server-side; live-tenant measurement
    /// showed the opposite — three spellings of one column all returned <c>200</c>, and
    /// Business Central answers in its own canonical casing regardless of what was requested.
    /// </summary>
    /// <remarks>
    /// Since casing drift cannot produce this <c>400</c>, a hint naming it would misdirect
    /// every real occurrence away from the cause the server's own message already states —
    /// strictly worse than saying nothing. This wording has drifted back twice, hence a test
    /// rather than a comment.
    /// </remarks>
    [Theory]
    [InlineData("case-sensitive")]
    [InlineData("case sensitive")]
    [InlineData("casing")]
    [InlineData("JsonPropertyName")]
    public async Task Hint_Makes_No_Case_Sensitivity_Claim(string forbidden)
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.DoesNotContain(forbidden, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The hint names one cause and stops.</summary>
    [Fact]
    public async Task Hint_Names_Exactly_One_Cause()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Contains("does not expose that column", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith("(GET → HTTP 400 BadRequest)", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 400 the projection plainly did not cause still carries the generic hint — the
    /// package cannot tell the two apart, so the wording suggests rather than asserts.
    /// </summary>
    [Fact]
    public async Task Unrelated_400_Gets_The_Generic_Hint_Without_Naming_A_Field()
    {
        var client = CreateClient(BadRequest(
            """{"error":{"code":"BadRequest","message":"Invalid filter expression."}}"""));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Contains("$select derived from LdatSummary", ex.Message, StringComparison.Ordinal);
        Assert.Contains("one of them", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("one of which is", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Short names must not match inside longer ones.</summary>
    [Fact]
    public async Task Field_Match_Respects_Word_Boundaries()
    {
        var client = CreateClient(BadRequest(
            """{"error":{"code":"BadRequest","message":"Could not find a property named 'noSuchThing'."}}"""));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        // "No" is a derived field, but it only occurs inside "noSuchThing" here.
        Assert.DoesNotContain("one of which is", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region When the hint must stay away

    [Fact]
    public async Task Explicit_Select_Gets_No_Hint()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries")
                .Select(x => x.No)
                .ToListAsync());

        Assert.DoesNotContain("$select derived", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectAll_Gets_No_Hint()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries")
                .SelectAll()
                .ToListAsync());

        Assert.DoesNotContain("$select derived", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A count query sends no projection, so it has nothing to explain.</summary>
    [Fact]
    public async Task CountAsync_Gets_No_Hint()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").CountAsync());

        Assert.DoesNotContain("$select derived", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The path-based API never derives a projection.</summary>
    [Fact]
    public async Task Path_Based_Query_Gets_No_Hint()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<LdatSummary>("ldatSummaries"));

        Assert.DoesNotContain("$select derived", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A non-400 is never about the projection.</summary>
    [Fact]
    public async Task Non_400_Gets_No_Hint()
    {
        var client = CreateClient(WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"error":{"code":"NotFound","message":"gone"}}""")
            }));

        var ex = await Assert.ThrowsAsync<BusinessCentralNotFoundException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.DoesNotContain("$select derived", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region The decorated exception stays usable

    [Fact]
    public async Task Decoration_Preserves_The_Structured_Fields()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.True(ex.IsValidation);
        Assert.False(ex.IsTransient);
        Assert.Equal("GET", ex.Method);
        Assert.Equal("BadRequest_NotFound", ex.ODataErrorCode);
        Assert.Contains("ldatSummaries", ex.RequestUrl!, StringComparison.Ordinal);
        Assert.Contains("systemCreatedAt", ex.ResponseBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decoration_Keeps_The_Server_Message_And_The_Original_As_Inner()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.Contains("Could not find a property named", ex.Message, StringComparison.Ordinal);
        Assert.IsType<BusinessCentralValidationException>(ex.InnerException);
    }

    /// <summary>
    /// The base type promises a single-line message, so the hint must not reintroduce the
    /// newlines that would break structured log ingestion.
    /// </summary>
    [Fact]
    public async Task Decorated_Message_Stays_One_Line()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        Assert.DoesNotContain('\n', ex.Message);
        Assert.DoesNotContain('\r', ex.Message);
    }

    /// <summary>The status decoration is appended once, not once per wrapping.</summary>
    [Fact]
    public async Task Decorated_Message_Does_Not_Repeat_The_Status_Suffix()
    {
        var client = CreateClient(BadRequest(MissingColumnBody));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.Query<LdatSummary>("ldatSummaries").ToListAsync());

        var occurrences = ex.Message.Split("HTTP 400").Length - 1;
        Assert.Equal(1, occurrences);
    }

    #endregion
}
