using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Tests;

public partial class ClientTests
{
    #region QueryRawAsync Tests

    [Fact]
    public async Task QueryRawAsync_Returns_Deserialized_Response()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":123,\"name\":\"Test\"}")
            };
        });

        var result = await client.QueryRawAsync<TestRawResponse>("orders/raw");

        Assert.NotNull(result);
        Assert.Equal(123, result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task QueryRawAsync_Throws_ServerException_On_Invalid_Json()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("invalid json")
            };
        });

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Contains("Failed to deserialize", ex.Message);
        Assert.Contains("invalid json", ex.ResponseBody);
    }


    [Fact]
    public async Task QueryRawAsync_Throws_ServerException_On_Null_Response()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null")
            };
        });

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Contains("Failed to deserialize", ex.Message);
        Assert.Contains("null", ex.ResponseBody);
    }


    [Fact]
    public async Task QueryRawAsync_Throws_NotFoundException_On_404()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Not found")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralNotFoundException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task QueryRawAsync_Throws_AuthException_On_403()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("Forbidden")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralAuthException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task QueryRawAsync_Throws_ValidationException_On_400()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Bad request")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    #endregion

    #region PatchAsync Tests

    [Fact]
    public async Task PatchAsync_Sends_Correct_Request()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"test-id\",\"name\":\"Updated\"}")
            };
        });

        var payload = new TestPatchEntity { Id = "test-id", Name = "Updated" };

        // Act
        var result = await client.PatchAsync<TestPatchEntity>("orders", "test-id", payload);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Patch, capturedRequest.Method);
        Assert.Contains("test-id", capturedRequest.RequestUri!.ToString());
        Assert.True(capturedRequest.Headers.Contains("If-Match"));

        Assert.NotNull(result);
        Assert.Equal("test-id", result!.Id);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task PatchAsync_Uses_Custom_IfMatch()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"test-id\",\"name\":\"Updated\"}")
            };
        });

        var payload = new TestPatchEntity { Id = "test-id", Name = "Updated" };

        // Act
        var result = await client.PatchAsync<TestPatchEntity>("orders", "test-id", payload, "W/\"etag-123\"");

        // Assert
        Assert.NotNull(capturedRequest);
        var ifMatchValues = capturedRequest.Headers.GetValues("If-Match");
        Assert.Contains("W/\"etag-123\"", ifMatchValues);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task PatchAsync_Encodes_SystemId_In_Url()
    {
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var payload = new TestPatchEntity { Id = "abc 123", Name = "Test" };

        var result = await client.PatchAsync("orders", "abc 123", payload);

        Assert.NotNull(capturedUrl);
        Assert.Contains("abc%20123", capturedUrl);

        // 204 NoContent is a successful write; the sent payload is echoed back.
        Assert.Same(payload, result);
    }

    [Fact]
    public async Task PatchAsync_Preserves_Alternate_Key_Syntax()
    {
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await client.PatchAsync("salesOrders", "No='1000'", new TestPatchEntity { Id = "1", Name = "x" });

        Assert.NotNull(capturedUrl);

        // Quotes and '=' are OData key syntax and must survive verbatim.
        Assert.Contains("salesOrders(No='1000')", capturedUrl);
        Assert.DoesNotContain("%27", capturedUrl);
        Assert.DoesNotContain("%3D", capturedUrl);
    }

    [Fact]
    public async Task Writes_Ask_For_Return_Representation()
    {
        HttpRequestMessage? captured = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            captured = req;

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await client.PostAsync("orders", new TestPatchEntity { Id = "1", Name = "x" });

        Assert.NotNull(captured);
        Assert.Contains("return=representation", captured!.Headers.GetValues("Prefer"));
    }

    [Fact]
    public async Task Writes_Return_Payload_On_Empty_Body()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            };
        });

        var payload = new TestPatchEntity { Id = "1", Name = "x" };

        var posted = await client.PostAsync("orders", payload);
        var put = await client.PutAsync("orders", "1", payload);

        Assert.Same(payload, posted);
        Assert.Same(payload, put);
    }

    [Fact]
    public async Task PatchAsync_Throws_On_Error()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Invalid data")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.PatchAsync<TestPatchEntity>("orders", "test-id", new TestPatchEntity { Id = "test-id", Name = "Test" }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task PatchAsync_Serializes_Payload()
    {
        string? capturedBody = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedBody = req.Content?.ReadAsStringAsync().Result;

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var payload = new TestPatchEntity { Id = "test-id", Name = "Updated" };

        await client.PatchAsync("orders", "test-id", payload);

        Assert.NotNull(capturedBody);

        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;

        Assert.Equal("test-id", root.GetProperty("id").GetString());
        Assert.Equal("Updated", root.GetProperty("name").GetString());
    }

    #endregion

    #region URL Builder & Serialization Tests

    [Fact]
    public async Task Query_Encodes_Company_Name_And_Path()
    {
        // Arrange
        string? capturedUrl = null;
        var http = new HttpClient(new FakeHttpHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        }));

        var options = new BusinessCentralOptions
        {
            BaseUrl = "https://api.businesscentral.dynamics.com/v2.0/8fab1fd2-48ee-4f54-83c2-4132e421f062/UAT3/ODataV4",
            Company = "KRAL AG",
            TenantId = "tenant",
            ClientId = "client",
            ClientSecret = "secret",
            Scope = "scope",
            TokenEndpoint = "https://auth/{TenantId}"
        };

        var client = new BusinessCentralClient(
            http,
            Microsoft.Extensions.Options.Options.Create(options));

        // Act
        await client.QueryAsync<TestEntity>("LDATReservationEntries", "true", select: IdNameFields);

        // Assert
        Assert.NotNull(capturedUrl);
        var url = capturedUrl!;
        Assert.True(url.Contains("Company('KRAL%20AG')/LDATReservationEntries"), url);
        Assert.True(url.Contains("$select=id,name"), url);
    }

    [Fact]
    public async Task Query_Encodes_Path_Segments_With_Spaces()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("My Custom Table", "true");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("My%20Custom%20Table", capturedUrl);
    }

    [Fact]
    public async Task Query_Encodes_Raw_Filter_String()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = "positive eq true and sourceId eq 'BIF4079921'";

        // Act
        await client.QueryAsync<TestEntity>("LDATReservationEntries", filter, select: IdNameFields);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);
        Assert.Equal(filter, filterValue);
    }

    [Fact]
    public async Task Query_Encodes_Select_Fields_With_Special_Characters()
    {
        // Arrange
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var fields = new[] { "Unit Price", "Item No." };

        // Act
        await client.QueryAsync<TestEntity>("orders", "true", select: fields);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$select=", capturedUrl);
        Assert.Contains("Unit%20Price", capturedUrl);
        Assert.Contains("Item%20No.", capturedUrl);
    }

    [Fact]
    public async Task Query_Deserializes_Real_BusinessCentral_Casing()
    {
        // Arrange
        var response = """
    {
        "value": [
            {
                "entryNo": 10,
                "itemNo": "ABC",
                "serialNo": "XYZ"
            }
        ]
    }
    """;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            };
        });

        // Act
        var result = await client.QueryAsync<RealBcEntity>("items", "true");

        // Assert
        Assert.Single(result);
        Assert.Equal(10, result[0].EntryNo);
        Assert.Equal("ABC", result[0].ItemNo);
        Assert.Equal("XYZ", result[0].SerialNo);
    }

    #endregion

    #region Query Options Tests

    [Fact]
    public async Task Query_With_ODataFilter_Object()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("name", "test");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$filter=", capturedUrl);
    }

    [Fact]
    public async Task Query_With_Select()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true", select: IdNameFields);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$select=", capturedUrl);
        Assert.Contains("id", capturedUrl);
        Assert.Contains("name", capturedUrl);
    }

    [Fact]
    public async Task Query_Ignores_Empty_Select_List()
    {
        // Arrange
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true", select: Array.Empty<string>());

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.DoesNotContain("$select=", capturedUrl);
    }

    [Fact]
    public async Task Query_With_Top()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true", options: o => o.WithTop(50));

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$top=50", capturedUrl);
    }

    [Fact]
    public async Task Query_With_Skip()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true", options: o => o.WithSkip(100));

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$skip=100", capturedUrl);
    }

    [Fact]
    public async Task Query_With_OrderBy()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true", options: o => o.OrderBy = "name desc");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$orderby=", capturedUrl);
        Assert.Contains("name", capturedUrl);
    }

    [Fact]
    public async Task Query_Skips_Filter_When_True()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.DoesNotContain("$filter=", capturedUrl);
    }

    #endregion

    #region QueryAll Tests

    [Fact]
    public async Task QueryAll_Respects_Custom_Page_Size()
    {
        // Arrange
        var requests = new List<string>();
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            requests.Add(req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAllAsync<TestEntity>("orders", options: o => o.WithTop(25));

        // Assert
        Assert.Contains(requests, r => r.Contains("$top=25"));
    }

    [Fact]
    public async Task QueryAll_Uses_Default_Page_Size_When_Not_Set()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAllAsync<TestEntity>("orders");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$top=1000", capturedUrl);
    }

    [Fact]
    public async Task QueryAll_Preserves_OrderBy()
    {
        // Arrange
        var callCount = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            callCount++;
            var content = callCount == 1 ? "[{\"id\":1}]" : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"value\":{content}}}")
            };
        });

        // Act
        var result = await client.QueryAllAsync<TestEntity>("orders",
            options: o => o.OrderBy = "name asc");

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task QueryAll_With_Filter()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("status", "active");

        // Act
        await client.QueryAllAsync<TestEntity>("orders", filter);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$filter=", capturedUrl);
    }

    [Fact]
    public async Task QueryAll_With_Select()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAllAsync<TestEntity>("orders", select: IdNameFields);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("$select=id,name", capturedUrl);
    }

    #endregion

    #region Token Management Tests

    [Fact]
    public async Task Token_Expires_And_Refreshes()
    {
        // Arrange
        var tokenCalls = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;
                // Return a token that expires immediately
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token" + tokenCalls + "\",\"expires_in\":-1}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true");
        await Task.Delay(100); // Ensure token is expired
        await client.QueryAsync<TestEntity>("orders", "true");

        // Assert
        Assert.Equal(2, tokenCalls);
    }

    [Fact]
    public async Task Token_Request_Failure_Throws_Exception()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("Invalid credentials")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralAuthException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Token_Invalid_Json_Throws_Exception()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("invalid json")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));
    }

    [Fact]
    public async Task Token_Is_Reused_When_Not_Expired()
    {
        // Arrange
        var tokenCalls = 0;
        var dataCalls = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\",\"expires_in\":3600}")
                };
            }

            dataCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        await client.QueryAsync<TestEntity>("orders", "true");
        await client.QueryAsync<TestEntity>("orders", "true");

        // Assert
        Assert.Equal(1, tokenCalls);
        Assert.Equal(2, dataCalls);
    }

    [Fact]
    public async Task Unauthorized_Retries_After_Refreshing_Token()
    {
        // Arrange
        var tokenCalls = 0;
        var dataCalls = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"access_token\":\"token{tokenCalls}\",\"expires_in\":3600}}")
                };
            }

            dataCalls++;
            if (dataCalls == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("unauthorized")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        var result = await client.QueryAsync<TestEntity>("orders", "true");

        // Assert
        Assert.Empty(result);
        Assert.Equal(2, tokenCalls); // initial and after invalidation
        Assert.Equal(2, dataCalls);  // failed attempt + retry
    }

    #endregion

    #region Token Concurrency & Safety Tests

    [Fact]
    public async Task Parallel_Requests_Share_Single_Token_Request()
    {
        // Arrange
        var tokenCalls = 0;
        var dataCalls = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                Interlocked.Increment(ref tokenCalls);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token\",\"expires_in\":3600}")
                };
            }

            Interlocked.Increment(ref dataCalls);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.QueryAsync<TestEntity>("orders", "true"));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(1, tokenCalls);
        Assert.Equal(5, dataCalls);
    }

    [Fact]
    public async Task Parallel_Requests_Refresh_Token_Once_When_Expired()
    {
        // Arrange
        var tokenCalls = 0;
        var dataCalls = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                var id = Interlocked.Increment(ref tokenCalls);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"access_token\":\"token{id}\",\"expires_in\":61}}")
                };
            }

            Interlocked.Increment(ref dataCalls);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // initial burst uses first token
        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => client.QueryAsync<TestEntity>("orders", "true")));

        // Wait so the safety buffer marks the token as expired (61s - 60s buffer)
        await Task.Delay(1200);

        // second burst should trigger only one refresh
        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => client.QueryAsync<TestEntity>("orders", "true")));

        // Assert
        Assert.Equal(2, tokenCalls);
        Assert.Equal(6, dataCalls);
    }

    [Fact]
    public async Task Token_Failure_Is_Propagated_To_Parallel_Requests()
    {
        // Arrange
        var tokenCalls = 0;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                Interlocked.Increment(ref tokenCalls);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("Invalid credentials")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        // Act
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Assert.ThrowsAsync<BusinessCentralAuthException>(
                () => client.QueryAsync<TestEntity>("orders", "true")));

        var exceptions = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(3, tokenCalls);
        Assert.All(exceptions, ex => Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode));
    }

    #endregion

    #region OData Filter Tests

    [Fact]
    public async Task Query_With_Filter_Equals()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("status", "active");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("status eq 'active'", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_NotEquals()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.NotEquals("status", "deleted");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("status ne 'deleted'", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_GreaterThan()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.GreaterThan("amount", 100);

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("amount gt 100", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_Contains()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Contains("name", "test");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("contains", capturedUrl);
    }

    [Fact]
    public async Task Query_With_Filter_And()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("status", "active")
            .And(Filter.GreaterThan("amount", 100));

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("(status eq 'active') and (amount gt 100)", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_Or()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("status", "pending")
            .Or(Filter.Equals("status", "processing"));

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("(status eq 'pending') or (status eq 'processing')", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_Not()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("status", "deleted").Not();

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("not (status eq 'deleted')", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_DateTime_Uses_Utc_Iso_Format()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };


            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var localDate = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var filter = Filter.GreaterThan("createdAt", localDate);

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal($"createdAt gt {localDate.ToUniversalTime():O}", filterValue);
    }

    [Fact]
    public async Task Query_With_Filter_IsNull()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.IsNull("deletedAt");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("null", capturedUrl);
    }

    [Fact]
    public async Task Query_With_Filter_In()
    {
        // Arrange
        string? capturedUrl = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.In("status", "active", "pending", "processing");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);

        Assert.Equal("status in ('active','pending','processing')", filterValue);
    }

    [Fact]
    public async Task Query_Filter_Encodes_Special_Characters()
    {
        // Arrange
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        var filter = Filter.Equals("name", "A&B/C");

        // Act
        await client.QueryAsync<TestEntity>("orders", filter);

        // Assert
        var filterValue = ExtractFilter(capturedUrl);
        Assert.Equal("name eq 'A&B/C'", filterValue);
    }

    [Fact]
    public async Task Query_Throws_On_Invalid_Json_Response()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("invalid")
            };
        });

        // Act & Assert
        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));
    }

    #endregion

    #region Exception Tests

    [Fact]
    public async Task Query_Throws_ServerException_On_500()
    {
        // Arrange
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Exception_Preserves_Response_Body()
    {
        // Arrange
        var expectedBody = "{\"error\":\"Something went wrong\"}";
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(expectedBody)
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        // Assert
        Assert.Equal(expectedBody, ex.ResponseBody);
    }

    [Fact]
    public async Task ExceptionFactory_Parses_BusinessCentral_Error_Format()
    {
        // Arrange
        var bcError = @"
    {
        ""error"": {
            ""code"": ""BadRequest_ResourceNotFound"",
            ""message"": ""Resource not found for the segment 'LDATSalesLine'.  CorrelationId:  953b8867-cd45-4516-becf-e22d63f7f98c.""
        }
    }";

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(bcError)
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        // Assert — Message stays a single line; the structured detail lives on properties.
        Assert.Contains("Resource not found", ex.Message);
        Assert.DoesNotContain(System.Environment.NewLine, ex.Message);

        Assert.Equal("BadRequest_ResourceNotFound", ex.ODataErrorCode);
        Assert.Equal("953b8867-cd45-4516-becf-e22d63f7f98c", ex.CorrelationId);

        // ToString() is where everything shows up.
        var detailed = ex.ToString();
        Assert.Contains("BadRequest_ResourceNotFound", detailed);
        Assert.Contains("953b8867-cd45-4516-becf-e22d63f7f98c", detailed);
        Assert.Contains(bcError.Trim(), detailed);
    }

    [Fact]
    public async Task Exception_Message_Stays_A_Single_Line()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(new string('x', 5000))
            };
        });

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        // A 5 KB body must not end up in the log line.
        Assert.DoesNotContain("xxxx", ex.Message);
        Assert.Contains("GET", ex.Message);
        Assert.Contains("500", ex.Message);
        Assert.Contains("xxxx", ex.ResponseBody);
    }

    [Fact]
    public async Task ExceptionFactory_Extracts_CorrelationId_From_Message()
    {
        // Arrange
        var error = @"
    {
        ""error"": {
            ""code"": ""SomeError"",
            ""message"": ""Something failed. CorrelationId: abc-123-def""
        }
    }";

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(error)
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        // Assert
        Assert.Contains("abc-123-def", ex.Message);
    }

    #endregion

    [Fact]
    public async Task PostAsync_Sends_Correct_Request_And_Returns_Entity()
    {
        HttpRequestMessage? captured = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            captured = req;

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"1\",\"name\":\"Created\"}")
            };
        });

        var payload = new TestPatchEntity { Id = "1", Name = "Created" };

        var result = await client.PostAsync("orders", payload);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("Created", result.Name);
    }

    [Fact]
    public async Task PutAsync_Uses_IfMatch_Header_And_Returns_Entity()
    {
        HttpRequestMessage? captured = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            captured = req;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"1\",\"name\":\"Updated\"}")
            };
        });

        var payload = new TestPatchEntity { Id = "1", Name = "Updated" };

        var result = await client.PutAsync("orders", "1", payload, "W/\"123\"");

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("If-Match"));
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task DeleteAsync_Sends_Correct_Request()
    {
        HttpRequestMessage? captured = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            captured = req;

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await client.DeleteAsync("orders", "1");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.True(captured.Headers.Contains("If-Match"));
    }

    [Fact]
    public async Task PostAsync_Throws_On_BadRequest()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("invalid")
            };
        });

        await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.PostAsync("orders", new TestPatchEntity()));
    }

    [Fact]
    public async Task DeleteAsync_Throws_On_Unexpected_Status()
    {
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.DeleteAsync("orders", "1"));
    }


    #region Raw URL Tests

    // The README has documented QueryRawAsync<JsonElement> since 1.0, but the old
    // `where TResponse : class` constraint meant it never compiled for consumers.
    [Fact]
    public async Task QueryRawAsync_Accepts_A_Value_Type_Response()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"value\":[{\"no\":\"1000\"}]}")));

        var raw = await client.QueryRawAsync<JsonElement>("salesOrders?$top=5");

        Assert.Equal(JsonValueKind.Object, raw.ValueKind);
        Assert.Equal("1000", raw.GetProperty("value")[0].GetProperty("no").GetString());
    }

    [Fact]
    public async Task QueryRawAsync_Still_Accepts_Reference_Types()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"id\":7,\"name\":\"Seven\"}")));

        var typed = await client.QueryRawAsync<TestRawResponse>("orders/raw");

        Assert.Equal(7, typed.Id);
    }


    [Fact]
    public async Task QueryRawAsync_Preserves_Query_String()
    {
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":1,\"name\":\"x\"}")
            };
        });

        await client.QueryRawAsync<TestRawResponse>("salesOrders?$top=5");

        Assert.NotNull(capturedUrl);
        Assert.Contains("salesOrders?$top=5", capturedUrl);
        Assert.DoesNotContain("%3F", capturedUrl);
        Assert.DoesNotContain("%24", capturedUrl);
    }

    [Fact]
    public async Task QueryRawAsync_Preserves_Path_Separators()
    {
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":1,\"name\":\"x\"}")
            };
        });

        await client.QueryRawAsync<TestRawResponse>("orders/raw");

        Assert.NotNull(capturedUrl);
        Assert.Contains("orders/raw", capturedUrl);
        Assert.DoesNotContain("%2F", capturedUrl);
    }

    [Fact]
    public async Task Query_Preserves_Navigation_Path_But_Encodes_Spaces()
    {
        string? capturedUrl = null;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedUrl = req.RequestUri!.AbsoluteUri;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        });

        await client.QueryAsync<TestEntity>("salesOrders/Sales Lines", "true");

        Assert.NotNull(capturedUrl);
        Assert.Contains("salesOrders/Sales%20Lines", capturedUrl);
    }

    #endregion

    #region Server-Driven Paging Tests

    [Fact]
    public async Task QueryAll_Follows_ODataNextLink()
    {
        var urls = new List<string>();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            urls.Add(req.RequestUri!.AbsoluteUri);

            // First page is short *and* carries a nextLink — the old "short page means
            // done" rule would have truncated the result here.
            var body = urls.Count == 1
                ? "{\"value\":[{\"id\":1}],\"@odata.nextLink\":\"https://test/page2\"}"
                : "{\"value\":[{\"id\":2}]}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        });

        var result = await client.QueryAllAsync<TestEntity>("orders");

        Assert.Equal(2, result.Count);
        Assert.Equal(2, urls.Count);
        Assert.Equal("https://test/page2", urls[1]);
    }

    [Fact]
    public async Task QueryAll_Stops_On_Short_Page_Without_NextLink()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            dataCalls++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"id\":1},{\"id\":2}]}")
            };
        });

        var result = await client.QueryAllAsync<TestEntity>("orders", options: o => o.WithTop(10));

        Assert.Equal(2, result.Count);
        Assert.Equal(1, dataCalls);
    }

    [Fact]
    public async Task QueryAll_Pages_Until_Short_Page()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            dataCalls++;

            // Two full pages of 2, then a short page.
            var body = dataCalls <= 2
                ? "{\"value\":[{\"id\":1},{\"id\":2}]}"
                : "{\"value\":[{\"id\":3}]}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        });

        var result = await client.QueryAllAsync<TestEntity>("orders", options: o => o.WithPageSize(2));

        Assert.Equal(5, result.Count);
        Assert.Equal(3, dataCalls);
    }

    // Top is a result cap here, exactly as its documentation says — the same contract as
    // the fluent builder's Top(). It used to be silently repurposed as the page size.
    [Fact]
    public async Task QueryAll_Honours_Top_As_A_Result_Cap()
    {
        var dataCalls = 0;
        var requests = new List<string>();

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            dataCalls++;
            requests.Add(req.RequestUri!.ToString());

            return TestBase.Json("{\"value\":[{\"id\":1},{\"id\":2}]}");
        }));

        var result = await client.QueryAllAsync<TestEntity>(
            "orders", options: o => o.WithTop(3).WithPageSize(2));

        Assert.Equal(3, result.Count);
        Assert.Equal(2, dataCalls);

        // The second request never overshoots the cap: only one row is still wanted.
        Assert.Contains("$top=2", requests[0]);
        Assert.Contains("$top=1", requests[1]);
    }

    [Fact]
    public async Task QueryAll_Top_Alone_Stops_After_Top_Rows()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;
            return TestBase.Json("{\"value\":[{\"id\":1},{\"id\":2}]}");
        }));

        var result = await client.QueryAllAsync<TestEntity>("orders", options: o => o.WithTop(2));

        Assert.Equal(2, result.Count);
        Assert.Equal(1, dataCalls);
    }

    #endregion

    #region Observer Reporting Tests

    [Fact]
    public async Task Failed_Request_Is_Reported_To_Observer_Exactly_Once()
    {
        var observer = new TestObserver();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"code\":\"Bad\",\"message\":\"nope\"}}")
            };
        }, observer);

        await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Single(observer.Failures);
        Assert.Equal(400, observer.Failures[0].StatusCode);
        Assert.Contains("nope", observer.Failures[0].ResponseBody);
    }

    [Fact]
    public async Task Unauthorized_Retry_Reports_One_Failure_Then_Success()
    {
        var observer = new TestObserver();
        var dataCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            dataCalls++;

            if (dataCalls == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("unauthorized")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        }, observer);

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Single(observer.Failures);
        Assert.Equal(401, observer.Failures[0].StatusCode);
        Assert.Contains("success:200", observer.Events);
    }

    #endregion

    #region Test Helper Classes

    private static readonly string[] IdNameFields = { "id", "name" };

    private static string? ExtractFilter(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var uri = new Uri(url);
        var query = uri.Query.TrimStart('?');

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith("$filter=", StringComparison.OrdinalIgnoreCase))
                continue;

            var encoded = part.Substring("$filter=".Length);
            return Uri.UnescapeDataString(encoded);
        }

        return null;
    }

    #endregion
}