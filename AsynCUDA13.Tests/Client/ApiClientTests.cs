using System.Net;
using System.Text;
using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Client;
using AsynCUDA13.Shared.Localization;
using AsynCUDA13.Shared.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Moq;
using Shouldly;

namespace AsynCUDA13.Tests.Client;

[TestClass]
public sealed class ApiClientTests : TestBase
{
    [TestMethod]
    [DataRow(0, true)]
    [DataRow(4, false)]
    [DataRow(5, false)]
    public void Constructor_AppliesConfigurationAndLogLevel(int logLevel, bool silent)
    {
        // Arrange
        var logger = CreateLogger();
        var configuration = new ApiClientConfiguration { ApiBaseUrl = "https://unit.test", LogLevel = logLevel };

        // Act
        var client = new ApiClient(configuration, CreateLanguageService(), logger, CreateHttpClient(HttpStatusCode.OK, "\"OpenCL\""));

        // Assert
        client.BaseUrl.ShouldBe(configuration.ApiBaseUrl);
        ((int) client.LogLevel).ShouldBe(logLevel);
        logger.Settings.Silent.ShouldBe(silent);
        client.Initialized.ShouldBeFalse();
    }

    [TestMethod]
    public async Task InitializeAsync_WithBackendResponse_SetsRuntimeStateOnlyOnce()
    {
        // Arrange
        var calls = 0;
        var handler = new DelegateHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"OpenCL\"", Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(new HttpClient(handler) { BaseAddress = new Uri("https://unit.test") });

        // Act
        await client.InitializeAsync();
        await client.InitializeAsync();

        // Assert
        client.Initialized.ShouldBeTrue();
        client.BackendType.ShouldBe("OpenCL");
        client.IsCudaAvailable.ShouldBeNull();
        client.L.Runtime.ShouldBe("OpenCL");
        calls.ShouldBe(1);
    }

    [TestMethod]
    public async Task GetLogListAsync_ForFrontend_ReturnsLoggerSnapshot()
    {
        // Arrange
        var logger = CreateLogger();
        logger.ClearLogs();
        logger.Log("first");
        logger.Log("second");
        var client = CreateClient(CreateHttpClient(HttpStatusCode.OK, "[]"), logger);

        // Act
        var lines = await client.GetLogListAsync(frontendLog: true, nLastMax: 1);

        // Assert
        lines.ShouldHaveSingleItem().ShouldContain("second");
    }

    [TestMethod]
    public async Task InitializeAsync_WhenServerFails_ThrowsApiException()
    {
        // Arrange
        var client = CreateClient(CreateHttpClient(HttpStatusCode.InternalServerError, "failure"));

        // Act & Assert
        await Should.ThrowAsync<Exception>(() => client.InitializeAsync());
        client.Initialized.ShouldBeFalse();
    }

    private ApiClient CreateClient(HttpClient httpClient, RollingFileMemoryLogger? logger = null)
        => new(
            new ApiClientConfiguration { ApiBaseUrl = "https://unit.test", LogLevel = 4 },
            CreateLanguageService(),
            logger ?? CreateLogger(),
            httpClient);

    private static RollingFileMemoryLogger CreateLogger()
        => new(new RollingFileMemoryLoggerOptions { Silent = true, CreateLogFile = false });

    private static LanguageService CreateLanguageService()
        => new(Mock.Of<IStringLocalizer<SharedResources>>(), new HttpContextAccessor());

    private static HttpClient CreateHttpClient(HttpStatusCode statusCode, string content)
        => new(new DelegateHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://unit.test")
        };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }
}
