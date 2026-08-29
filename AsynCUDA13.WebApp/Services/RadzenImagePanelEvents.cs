using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen.Blazor;

namespace AsynCUDA13.WebApp.Services
{
    /// <summary>
    /// Hilfsklasse zur Verwaltung und Erstellung von Events für das RadzenImagePanel.
    /// </summary>
    public static class RadzenImagePanelEvents
    {
        /// <summary>
        /// Erstellt eine Standard-Konfiguration für ein interaktives Image Panel.
        /// </summary>
        public static RadzenImagePanelOptions CreateDefaultOptions()
        {
            return new RadzenImagePanelOptions
            {
                CanDrag = true,
                CanScrollZoom = true,
                UseCtrlForAlternativeAction = true
            };
        }
    }
}
