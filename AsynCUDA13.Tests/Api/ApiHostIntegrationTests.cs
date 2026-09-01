using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace AsynCUDA13.Tests.Api;

[TestClass]
public sealed class ApiHostIntegrationTests
{
    [TestMethod]
    [DataRow("OpenCL")]
    [DataRow("CUDA")]
    public async Task RuntimeContextEndpoints_UseConfiguredOrFallbackBackend(string backend)
    {
        // Arrange
        await using var factory = new ApiTestFactory(backend);
        using var client = factory.CreateApiClient();

        // Act
        var backendResponse = await client.GetAsync("/api/RuntimeContext/backend");
        var statusResponse = await client.GetAsync("/api/RuntimeContext/status");

        // Assert
        backendResponse.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        statusResponse.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        if (backendResponse.IsSuccessStatusCode)
        {
            var runtimeName = await backendResponse.Content.ReadAsStringAsync();
            runtimeName.ShouldBeOneOf("CUDA", "OpenCL");
        }
    }

    [TestMethod]
    [DataRow("OpenCL")]
    [DataRow("CUDA")]
    public async Task LogEndpoints_RoundTripCommentAndClear(string backend)
    {
        // Arrange
        await using var factory = new ApiTestFactory(backend);
        using var client = factory.CreateApiClient();
        string comment = $"api-test-{Guid.NewGuid():N}";

        // Act
        var postResponse = await client.PostAsJsonAsync("/log-comment", comment);
        var lines = await client.GetFromJsonAsync<string[]>("/log-lines");
        var clearResponse = await client.DeleteAsync("/log-clear");
        var clearedLines = await client.GetFromJsonAsync<string[]>("/log-lines");

        // Assert
        postResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        lines.ShouldNotBeNull();
        lines.ShouldContain(line => line.Contains(comment, StringComparison.Ordinal));
        clearResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        clearedLines.ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow("OpenCL")]
    [DataRow("CUDA")]
    public async Task LogFileEndpoint_ReturnsNamedPlainTextAttachment(string backend)
    {
        // Arrange
        await using var factory = new ApiTestFactory(backend);
        using var client = factory.CreateApiClient();

        // Act
        var response = await client.GetAsync("/log-file");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("application.log");
    }
}
