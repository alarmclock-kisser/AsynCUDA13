using System.Net.Http.Json;
using System.Text.Json;
using AsynCUDA13.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AsynCUDA13.Tests.Api;

internal sealed class ApiTestFactory(string backend = "OpenCL") : WebApplicationFactory<Program>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backend"] = backend,
                ["SwitchBackendIfUnavailable"] = "true",
                ["LoggerSettings:Silent"] = "true",
                ["LoggerSettings:CreateLogFile"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://localhost"
            });
        });
    }

    internal HttpClient CreateApiClient()
        => this.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    internal static Task<T?> ReadAsync<T>(HttpResponseMessage response)
        => response.Content.ReadFromJsonAsync<T>(JsonOptions);

    internal static JsonContent AsJson<T>(T value)
        => JsonContent.Create(value, options: JsonOptions);
}
