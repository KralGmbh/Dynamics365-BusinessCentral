using Dynamics365.BusinessCentral.Errors;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Dynamics365.BusinessCentral.Tests;

public class BusinessCentralExceptionFactoryTests
{
    [Fact]
    public async Task CreateAsync_Parses_OData_Error_Details()
    {
        const string body = "{\"error\":{\"code\":\"Test.Code\",\"message\":\"Invalid data. CorrelationId: abc-123.\"}}";

        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://test/resource")
        };

        var exception = await BusinessCentralExceptionFactory.CreateAsync(response, CancellationToken.None);

        var validationException = Assert.IsType<BusinessCentralValidationException>(exception);
        Assert.Equal(HttpStatusCode.BadRequest, validationException.StatusCode);
        Assert.Equal("Test.Code", validationException.ODataErrorCode);
        Assert.Equal("abc-123", validationException.CorrelationId);
        Assert.Equal(body, validationException.ResponseBody);
        Assert.Contains("OData Code: Test.Code", validationException.Message);
        Assert.Contains("CorrelationId: abc-123", validationException.Message);
    }

    [Fact]
    public async Task CreateAsync_Uses_Default_Message_When_OData_Message_Missing()
    {
        const string body = "{\"unexpected\":true}";

        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(body),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://test/resource")
        };

        var exception = await BusinessCentralExceptionFactory.CreateAsync(response, CancellationToken.None);

        var serverException = Assert.IsType<BusinessCentralServerException>(exception);
        Assert.Equal(HttpStatusCode.InternalServerError, serverException.StatusCode);
        Assert.Equal(body, serverException.ResponseBody);
        Assert.Contains("500", serverException.Message);
        Assert.Contains("InternalServerError", serverException.Message);
    }
}
