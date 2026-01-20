using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using System.Net;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Tests;

public class ClientTests
{
    #region QueryRawAsync Tests

    [Fact]
    public async Task QueryRawAsync_Returns_Deserialized_Response()
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
                Content = new StringContent("{\"id\":123,\"name\":\"Test\"}")
            };
        });

        // Act
        var result = await client.QueryRawAsync<TestRawResponse>("orders/raw");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(123, result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task QueryRawAsync_Throws_On_Invalid_Json()
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
                Content = new StringContent("invalid json")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        // Assert
        Assert.Contains("Failed to deserialize", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task QueryRawAsync_Throws_On_Null_Response()
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
                Content = new StringContent("null")
            };
        });

        // Act
        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryRawAsync<TestRawResponse>("orders/raw"));

        // Assert
        Assert.Contains("Failed to deserialize", ex.Message);
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
        // Arrange
        string? capturedBody = null;
        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            capturedBody = req.Content == null ? null : req.Content.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var payload = new TestPatchEntity { Id = "test-id", Name = "Updated" };

        // Act
        var result = await client.PatchAsync<TestPatchEntity>("orders", "test-id", payload);

        // Assert
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;
        Assert.Equal("test-id", root.GetProperty("Id").GetString());
        Assert.Equal("Updated", root.GetProperty("Name").GetString());
        Assert.Null(result);
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

    #endregion

    #region Test Helper Classes

    private sealed class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestRawResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestPatchEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

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