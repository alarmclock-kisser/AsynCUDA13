using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Localization;
using AsynCUDA13.WebApp.Components;
using AsynCUDA13.WebApp.ViewModels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Reflection;

namespace AsynCUDA13.WebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

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

            // ApiClient (Singleton, liest ApiBaseUrl aus appsettings.json)
            string apiBaseUrl = builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "https://localhost:7186";
            builder.Services.AddSingleton<ApiClient>(provider => new ApiClient(apiBaseUrl));

            // ViewModels (Singleton)
            builder.Services.AddSingleton<HomeViewModel>();
            builder.Services.AddSingleton<AssetsViewModel>();
            builder.Services.AddSingleton<MemoryViewModel>();
            builder.Services.AddSingleton<CompilerViewModel>();
            builder.Services.AddSingleton<ExecuteViewModel>();

            // Set UI context for StaticLogger before building the app
            var syncContext = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            StaticLogger.SetUiContext(syncContext);

            var app = builder.Build();

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
