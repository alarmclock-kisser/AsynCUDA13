using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Client;
using AsynCUDA13.Shared.Localization;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Options;
using AsynCUDA13.WebApp.Components;
using AsynCUDA13.WebApp.ViewModels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Radzen;
using System.Globalization;
using System.Reflection;

namespace AsynCUDA13.WebApp
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
                return new RollingFileMemoryLogger(options);
            });

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddRadzenComponents();

            // Add localization services
            builder.Services.AddLocalization();
            builder.Services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedLanguages = new[] { "de-DE", "en-GB" };
                var supportedCultures = supportedLanguages.Select(lang => new CultureInfo(lang)).ToList();
                options.SetDefaultCulture(supportedCultures[0].Name);
                options.AddSupportedCultures(supportedCultures.Select(c => c.Name).ToArray());
                options.AddSupportedUICultures(supportedCultures.Select(c => c.Name).ToArray());

                // Use browser's language preference, fallback to German
                options.RequestCultureProviders.Insert(0, new AcceptLanguageHeaderRequestCultureProvider());
            });
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<LanguageService>();

            // ApiClientConfiguration (Singleton, liest ApiBaseUrl aus appsettings.json)
            string apiBaseUrl = builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "https://localhost:7186";
            int apiClientLogLevel = builder.Configuration.GetValue<int>("ApiClientLogLevel", 4); // Default to LogLevel.Warning if not specified
            builder.Services.AddSingleton<ApiClientConfiguration>(sp => new()
            {
                ApiBaseUrl = apiBaseUrl,
                LogLevel = apiClientLogLevel
            });

            // ApiClient (Singleton)
            builder.Services.AddSingleton<ApiClient>();

            // ViewModels (Scoped)
            builder.Services.AddScoped<HomeViewModel>();
            builder.Services.AddScoped<AssetsViewModel>();
            builder.Services.AddScoped<MemoryViewModel>();
            builder.Services.AddScoped<CompilerViewModel>();
            builder.Services.AddScoped<ExecuteViewModel>();
            builder.Services.AddScoped<FractalsViewModel>();

            var app = builder.Build();

            // Set UI context for the DI logger and start/stop background logging with the application lifetime.
            var logger = app.Services.GetRequiredService<IRollingFileMemoryLogger>();
            var syncContext = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            logger.SetUiContext(syncContext);

            app.Lifetime.ApplicationStarted.Register(() =>
            {
                logger.LogInfo("AsynCUDA13.WebApp started.");
                logger.StartBackgroundWriter(app.Lifetime.ApplicationStopping);
            });
            app.Lifetime.ApplicationStopped.Register(() =>
            {
                try
                {
                    logger.SaveToRepository(forceSave: true);
                    logger.LogInfo("AsynCUDA13.WebApp stopped. Logs saved.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving logs on shutdown: {ex.Message}");
                }
            });

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            // Configure request localization
            app.UseRequestLocalization();

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
