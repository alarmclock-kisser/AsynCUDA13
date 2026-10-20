using System.Net;
using AsynCUDA13.Client;
using AsynCUDA13.Shared.Client;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Localization;
using AsynCUDA13.Shared.Options;
using AsynCUDA13.WebApp.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;

namespace AsynCUDA13.Tests.WebApp;

internal sealed class CadViewModelTestContext : IAsyncDisposable
{
    private readonly HttpClient httpClient = new(new BackendHandler());
    internal Mock<IJSRuntime> JavaScript { get; } = new();
    internal ServiceProvider Services { get; }
    internal CadViewModel ViewModel => this.Services.GetRequiredService<CadViewModel>();
    internal LanguageService Language => this.Services.GetRequiredService<LanguageService>();

    internal CadViewModelTestContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton(this.JavaScript.Object);
        var logger = Mock.Of<IRollingFileMemoryLogger>(value => value.Settings == new RollingFileMemoryLoggerOptions());
        services.AddSingleton(provider => new ApiClient(
            new ApiClientConfiguration { ApiBaseUrl = "https://unit.test", LogLevel = 0 },
            provider.GetRequiredService<LanguageService>(), logger, this.httpClient));
        services.AddSingleton<CadViewModel>();
        this.Services = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await this.Services.DisposeAsync();
        this.httpClient.Dispose();
    }

    private sealed class BackendHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"OpenCL\"") });
    }
}
