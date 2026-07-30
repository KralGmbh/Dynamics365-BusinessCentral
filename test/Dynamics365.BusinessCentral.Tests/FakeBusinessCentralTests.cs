using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Testing;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Dogfoods the shipped <c>Dynamics365.BusinessCentral.Testing</c> package. These tests
/// are the package's contract: a consumer can assert the exact OData a call produced,
/// script multi-page and failure responses, and never touch auth.
/// </summary>
public class FakeBusinessCentralTests
{
    [Fact]
    public async Task Records_The_Exact_OData_Url_A_Query_Produced()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage(new TestEntity { Id = 1, Name = "Pump" });

        var result = await bc.Client.QueryAsync<TestEntity>(
            "items", Filter.Equals("name", "Pump"), select: ["id", "name"]);

        Assert.Single(result);

        var request = Assert.Single(bc.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/Company('TEST')/items?$filter=name eq 'Pump'&$select=id,name",
            request.DecodedPathAndQuery);
    }

    [Fact]
    public async Task Token_Acquisition_Is_Answered_Automatically_And_Not_Recorded()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage<TestEntity>();

        await bc.Client.QueryAsync<TestEntity>("items");

        Assert.Equal(1, bc.TokenRequestCount);
        Assert.All(bc.Requests, r => Assert.DoesNotContain("login.test", r.Uri.Host));
    }

    [Fact]
    public async Task Server_Driven_Paging_Can_Be_Scripted_With_A_Relative_NextLink()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage([new TestEntity { Id = 1 }], nextLink: "page2")
          .EnqueuePage([new TestEntity { Id = 2 }], nextLink: "page3")
          .EnqueuePage([new TestEntity { Id = 3 }]);

        var all = await bc.Client.QueryAllAsync<TestEntity>("items");

        Assert.Equal([1, 2, 3], all.Select(e => e.Id));
        Assert.Equal(3, bc.Requests.Count);
        Assert.Equal("/page2", bc.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task Scripted_Errors_Surface_As_The_Matching_Exception_Type()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueueError(HttpStatusCode.NotFound, odataCode: "BadRequest_ResourceNotFound");

        var ex = await Assert.ThrowsAsync<BusinessCentralNotFoundException>(() =>
            bc.Client.QueryAsync<TestEntity>("items"));

        Assert.True(ex.IsNotFound);
        Assert.Equal("BadRequest_ResourceNotFound", ex.ODataErrorCode);
    }

    [Fact]
    public async Task Scripted_Throttling_Exercises_The_Real_Retry_Pipeline()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueueError(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(5))
          .EnqueuePage(new TestEntity { Id = 1 });

        var result = await bc.Client.QueryAsync<TestEntity>("items");

        Assert.Single(result);
        Assert.Equal(2, bc.Requests.Count);
    }

    [Fact]
    public async Task Network_Failures_Can_Be_Scripted()
    {
        using var bc = new FakeBusinessCentral(o => o.Retry.Enabled = false);
        bc.EnqueueNetworkFailure();

        await Assert.ThrowsAsync<BusinessCentralConnectionException>(() =>
            bc.Client.QueryAsync<TestEntity>("items"));
    }

    [Fact]
    public async Task Writes_Record_The_Serialized_Body()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueueNoContent();

        var payload = new TestPatchEntity { Id = "7", Name = "Pump" };
        var echoed = await bc.Client.PostAsync("items", payload);

        // 204 echoes the payload — the documented write contract, exercised for real.
        Assert.Same(payload, echoed);

        var request = Assert.Single(bc.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Contains("\"name\":\"Pump\"", request.Body);
    }

    [Fact]
    public async Task Unscripted_Requests_Throw_A_Message_Naming_The_Request()
    {
        using var bc = new FakeBusinessCentral();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bc.Client.QueryAsync<TestEntity>("items"));

        Assert.Contains("items", ex.Message);
        Assert.Contains("EnqueuePage", ex.Message);
    }

    // Token detection must not fire on a JSON payload that merely contains the grant
    // string — only the real form-urlencoded token POST is auto-answered.
    [Fact]
    public async Task Data_Post_Containing_The_Grant_String_Is_Not_Mistaken_For_A_Token_Request()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueueNoContent();

        var payload = new TestPatchEntity { Id = "1", Name = "grant_type=client_credentials" };
        await bc.Client.PostAsync("items", payload);

        var request = Assert.Single(bc.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Contains("grant_type=client_credentials", request.Body);
        Assert.Equal(1, bc.TokenRequestCount);
    }

    // Relative nextLinks must follow a BaseUrl override, or the continuation request is
    // captured on a different host than every other request.
    [Fact]
    public async Task Relative_NextLink_Resolves_Against_An_Overridden_BaseUrl()
    {
        using var bc = new FakeBusinessCentral(o => o.BaseUrl = "https://custom.example/odata");
        bc.EnqueuePage([new TestEntity { Id = 1 }], nextLink: "page2")
          .EnqueuePage([new TestEntity { Id = 2 }]);

        await bc.Client.QueryAllAsync<TestEntity>("items");

        Assert.Equal(2, bc.Requests.Count);
        Assert.All(bc.Requests, r => Assert.Equal("custom.example", r.Uri.Host));
    }

    [Fact]
    public async Task Company_Segment_Encoding_Is_Assertable()
    {
        using var bc = new FakeBusinessCentral(o => o.Company = "CRONUS AG");
        bc.EnqueuePage<TestEntity>();

        await bc.Client.QueryAsync<TestEntity>("items");

        Assert.StartsWith("/Company('CRONUS%20AG')/items", bc.Requests[0].PathAndQuery);
    }
}
