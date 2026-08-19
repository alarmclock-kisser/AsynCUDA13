
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using Microsoft.AspNetCore.SignalR;
using Swashbuckle.AspNetCore.SwaggerUI;
using Newtonsoft.Json;

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

            // Add services to the container.
            builder.Services.AddSingleton<ICudaService, CudaService>();
            builder.Services.AddSingleton<AudioCollection>();
            builder.Services.AddSingleton<ImageCollection>();

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
                c.SwaggerDoc("v1", new() { Title = "AsynCUDA13.API v1", Version = "v1" });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AsynCUDA13.API v1");
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
