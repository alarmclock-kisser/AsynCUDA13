using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.WebApp.Components;
using AsynCUDA13.WebApp.ViewModels;
using Microsoft.AspNetCore.Components.Web;
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

            // ApiClient (Singleton, liest ApiBaseUrl aus appsettings.json)
            string apiBaseUrl = builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "https://localhost:7186";
            builder.Services.AddSingleton(new ApiClient(apiBaseUrl));

            // ViewModels (Singleton)
            builder.Services.AddSingleton<HomeViewModel>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();

            StaticLogger.SetUiContext(SynchronizationContext.Current ?? new SynchronizationContext());
        }
    }
}
