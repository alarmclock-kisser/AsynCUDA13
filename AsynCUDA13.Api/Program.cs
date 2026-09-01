using AsynCUDA13.Api.Services;
using AsynCUDA13.Media;
using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerUI;
using Newtonsoft.Json;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Utils;

namespace AsynCUDA13.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure the DI-based rolling file memory logger from the "LoggerSettings" appsettings section.
            builder.Services.Configure<RollingFileMemoryLoggerOptions>(builder.Configuration.GetSection("LoggerSettings"));
            builder.Services.AddSingleton<IRollingFileMemoryLogger, RollingFileMemoryLogger>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<RollingFileMemoryLoggerOptions>>().Value;
                return new RollingFileMemoryLogger(options, setGlobally: true);
            });

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
                }
            }
            else if (switchIfUnavailable || string.Equals(backend, "OpenCL", StringComparison.OrdinalIgnoreCase))
            {
                builder.Services.AddSingleton<IRuntimeService, OpenClService>();
                apiName = "AsynCL.API";
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

            // Start the background log writer when the application starts and flush/save on shutdown.
            var logger = app.Services.GetRequiredService<IRollingFileMemoryLogger>();
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                logger.LogInfo($"{apiName} started.");
                logger.StartBackgroundWriter(app.Lifetime.ApplicationStopping);
            });
            app.Lifetime.ApplicationStopped.Register(() =>
            {
                try
                {
                    logger.SaveToRepository(forceSave: true);
                    logger.LogInfo($"{apiName} stopped. Logs saved.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving logs on shutdown: {ex.Message}");
                }
            });

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

            // Initialize LogBroadcaster with the HubContext and the DI logger's event.
            var hubContext = app.Services.GetRequiredService<IHubContext<Hubs.LogHub>>();
            Hubs.LogBroadcaster.SetHubContext(hubContext);
            Hubs.LogBroadcaster.SubscribeToLogger(logger);

            app.MapControllers();
            app.MapHub<Hubs.LogHub>("/logHub")
                .RequireCors("WebApp");

            app.Run();
        }
    }
}
