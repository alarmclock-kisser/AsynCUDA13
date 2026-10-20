using System.Globalization;
using System.Net;
using AsynCUDA13.WebApp.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsynCUDA13.Tests.WebApp;

[TestClass]
public sealed class CadCameraCheckboxRenderingTests
{
    [TestMethod]
    [DataRow("de", false, "Kamera unter Boden erlauben")]
    [DataRow("de", true, "Kamera unter Boden erlauben")]
    [DataRow("en", false, "Allow camera below ground")]
    [DataRow("en", true, "Allow camera below ground")]
    public async Task Render_UsesLanguageServiceAndReflectsViewModel(string language, bool allowed, string label)
    {
        // Arrange
        await using var context = new CadViewModelTestContext();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            context.Language.SetLanguage(language);
            context.ViewModel.OnCameraBelowGroundChanged(allowed);
            await using var renderer = new HtmlRenderer(context.Services, context.Services.GetRequiredService<ILoggerFactory>());

            // Act
            string html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<Cad>(ParameterView.Empty)).ToHtmlString());

            // Assert
            string decoded = WebUtility.HtmlDecode(html);
            decoded.ShouldContain(label);
            decoded.ShouldContain("for=\"cad-allow-below-ground\"");
            decoded.ShouldContain("aria-describedby=\"cad-allow-below-ground-hint\"");
            string input = decoded.Split("<input").Single(part => part.StartsWith(" id=\"cad-allow-below-ground\""));
            input[..input.IndexOf('>')].Contains("checked", StringComparison.Ordinal).ShouldBe(allowed);
            decoded.ShouldNotContain("CadAllowCameraBelowGroundHint");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
