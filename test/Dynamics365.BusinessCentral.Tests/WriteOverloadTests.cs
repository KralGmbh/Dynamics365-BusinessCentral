using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Covers the two-generic write overloads and, critically, that adding them did not change
/// how existing single-generic call sites resolve.
/// </summary>
public class WriteOverloadTests
{
    private sealed class CreatedRow
    {
        public string SystemId { get; set; } = string.Empty;
    }

    #region Overload resolution — existing call shapes must be unaffected

    // The exact shape used by existing consumers: one explicit type argument.
    [Fact]
    public async Task Single_Generic_Call_Still_Echoes_Payload_On_204()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)));

        var payload = new { serialNo = "S1" };

        object? raw = await client.PostAsync<dynamic>("ldatSummary", payload);

        // Arity picks the one-generic overload, whose 204 contract is unchanged.
        Assert.Same(payload, raw);
    }

    [Fact]
    public async Task Single_Generic_Patch_With_Positional_IfMatch_Still_Binds()
    {
        HttpRequestMessage? captured = null;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));

        await client.PatchAsync<dynamic>(
            "prodOrders",
            Guid.NewGuid().ToString(),
            new { status = "Active" },
            "*",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
    }

    // No explicit type arguments at all — inference must still pick the one-generic form.
    [Fact]
    public async Task Inferred_Call_Binds_To_Single_Generic_Overload()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)));

        var payload = new TestPatchEntity { Id = "1", Name = "x" };

        var result = await client.PostAsync("orders", payload);

        Assert.Same(payload, result);
    }

    #endregion

    #region Two-generic overloads

    [Fact]
    public async Task Post_Can_Deserialize_Into_A_Different_Type()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"systemId\":\"abc-123\"}")));

        var created = await client.PostAsync<object, CreatedRow>(
            "ldatSummary",
            new { serialNo = "S1", productionOrderNo = "PO1" });

        Assert.NotNull(created);
        Assert.Equal("abc-123", created!.SystemId);
    }

    [Fact]
    public async Task Post_Returns_Null_When_Server_Returns_No_Representation()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)));

        var created = await client.PostAsync<object, CreatedRow>("ldatSummary", new { serialNo = "S1" });

        // null means "applied, not echoed" — not "failed". Failures throw.
        Assert.Null(created);
    }

    [Fact]
    public async Task Post_Returns_Null_On_Empty_Body()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") }));

        Assert.Null(await client.PostAsync<object, CreatedRow>("ldatSummary", new { serialNo = "S1" }));
    }

    // Value-typed results stay usable — JsonElement is the common "I have no model" case.
    [Fact]
    public async Task Two_Generic_Overload_Supports_Value_Type_Results()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"systemId\":\"abc-123\"}")));

        var element = await client.PostAsync<object, JsonElement>("ldatSummary", new { serialNo = "S1" });

        Assert.Equal("abc-123", element.GetProperty("systemId").GetString());
    }

    // Value types cannot be null, so a 204 yields default(TResult). For JsonElement that is
    // ValueKind.Undefined — the check callers must use instead of a null comparison.
    [Fact]
    public async Task Value_Type_Result_Is_Undefined_On_204()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)));

        var element = await client.PostAsync<object, JsonElement>("ldatSummary", new { serialNo = "S1" });

        Assert.Equal(JsonValueKind.Undefined, element.ValueKind);
    }

    [Fact]
    public async Task Patch_And_Put_Support_Distinct_Result_Types()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"systemId\":\"xyz\"}")));

        var patched = await client.PatchAsync<object, CreatedRow>(
            "prodOrders", "id-1", new { status = "Active" });

        var put = await client.PutAsync<object, CreatedRow>(
            "prodOrders", "id-1", new { status = "Active" });

        Assert.Equal("xyz", patched!.SystemId);
        Assert.Equal("xyz", put!.SystemId);
    }

    [Fact]
    public async Task Two_Generic_Overload_Still_Throws_On_Failure()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") }));

        await Assert.ThrowsAsync<Dynamics365.BusinessCentral.Errors.BusinessCentralValidationException>(() =>
            client.PostAsync<object, CreatedRow>("ldatSummary", new { serialNo = "S1" }));
    }

    [Fact]
    public async Task Two_Generic_Post_Is_Not_Replayed_On_Ambiguous_Failure()
    {
        var posts = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            posts++;
            return new HttpResponseMessage(HttpStatusCode.GatewayTimeout) { Content = new StringContent("x") };
        }));

        await Assert.ThrowsAsync<Dynamics365.BusinessCentral.Errors.BusinessCentralServerException>(() =>
            client.PostAsync<object, CreatedRow>("ldatSummary", new { serialNo = "S1" }));

        Assert.Equal(1, posts);
    }

    #endregion
}
