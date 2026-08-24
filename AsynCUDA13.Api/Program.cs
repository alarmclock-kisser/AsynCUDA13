using AsynCUDA13.Api.Services;
using AsynCUDA13.Media;
using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using Microsoft.AspNetCore.SignalR;
using Swashbuckle.AspNetCore.SwaggerUI;
using Newtonsoft.Json;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Setup StaticLogger with appsettings
            string? logDirectory = builder.Configuration.GetValue<string>("LogDirectory");
            bool createLogFile = builder.Configuration.GetValue<bool>("CreateLogFile");
            int maxLogFiles = builder.Configuration.GetValue<int>("MaxLogFiles");
            string? filterPhrase = builder.Configuration.GetValue<string>("FilterPhrase");
            bool? echoToConsole = builder.Configuration.GetValue<bool?>("EchoToConsole", null);
            string[] echoToConsoleKeyPhrases = builder.Configuration.GetValue<string[]>("EchoToConsoleKeyPhrases", ["[[["]);
            string innerExOpeningBracket = builder.Configuration.GetValue<string>("InnerExOpeningBracket", "(");
            string innerExClosingBracket = builder.Configuration.GetValue<string>("InnerExClosingBracket", ")");
            string innerExSeparator = builder.Configuration.GetValue<string>("InnerExSeparator", " ");
            StaticLogger.FilterPhrase = filterPhrase;
            StaticLogger.EchoToConsole = echoToConsole.HasValue ? echoToConsole.Value : null;
            StaticLogger.EchoToConsoleKeyPhrases = echoToConsoleKeyPhrases;
            StaticLogger.InnerExceptionOpeningBracket = innerExOpeningBracket;
            StaticLogger.InnerExceptionClosingBracket = innerExClosingBracket;
            StaticLogger.InnerExceptionSeparator = innerExSeparator;
            StaticLogger.InitializeLogFiles(logDirectory, createLogFile, maxLogFiles);

            // Select the compute backend ({this.RuntimeType} or OpenCL). The chosen backend is registered as both its
            // dedicated service interface (IRuntimeService / IOpenClService) and the interchangeable
            // IRuntimeService, so the rest of the API can depend on IRuntimeService regardless of backend.
            string backend = builder.Configuration.GetValue<string>("Backend") ?? "CUDA";
            bool switchIfUnavailable = builder.Configuration.GetValue<bool>("SwitchBackendIfUnavailable", true);

            string apiName = "AsynCUDA13.API";
            bool? cudaAvailable = null;
            if (string.Equals(backend, "CUDA", StringComparison.OrdinalIgnoreCase))
            {
                cudaAvailable = CudaAvailabilityTester.IsCudaAvailable();
                if (cudaAvailable.Value)
                {
                    builder.Services.AddSingleton<IRuntimeService, CudaService>();
                    StaticLogger.LogInfo("CUDA backend selected and available. --> " + apiName);
                }
            }

            if (switchIfUnavailable || string.Equals(backend, "OpenCL", StringComparison.OrdinalIgnoreCase))
            {
                if (cudaAvailable == false)
                {
                    StaticLogger.LogWarning("CUDA backend is not available. Switching to OpenCL backend.");
                }
                builder.Services.AddSingleton<IRuntimeService, OpenClService>();
                apiName = "AsynCL.API";
                StaticLogger.LogInfo("OpenCL backend selected. --> " + apiName);
            }
            else
            {
                throw new InvalidOperationException($"The specified backend '{backend}' is not available and switching to an alternative backend is disabled.");
            }

            // Add services to the container.
            builder.Services.AddSingleton<AudioCollection>();
            builder.Services.AddSingleton<ImageCollection>();
            builder.Services.AddSingleton<IAssetProvider, AssetProvider>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("WebApp", policy =>
                {
                    var origins = builder.Configuration.GetSection("CORS:AllowedOrigins").Get<string[]>() ?? [];
                    policy.WithOrigins(origins)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            });

            builder.Services.AddControllers()
                            .AddJsonOptions(options =>
                            {
                                options.JsonSerializerOptions.UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonNode;
                            });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
            });
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = $"{apiName} v1", Version = "v1" });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", $"{apiName} v1");
                    options.RoutePrefix = "swagger";
                });
            }

            app.UseHttpsRedirection();

            app.UseCors("WebApp");

            app.UseAuthorization();

            // Initialize LogBroadcaster with the HubContext
            var hubContext = app.Services.GetRequiredService<IHubContext<Hubs.LogHub>>();
            Hubs.LogBroadcaster.SetHubContext(hubContext);
            Hubs.LogBroadcaster.SubscribeToLogger();

            app.MapControllers();
            app.MapHub<Hubs.LogHub>("/logHub")
                .RequireCors("WebApp");

            app.Run();
        }
    }
}
