using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AsynCUDA13.WebApp.Services
{
    public class RadzenImagePanelOptions
    {
        public string Width { get; set; } = "100%";
        public string Height { get; set; } = "100%";
        public string Margin { get; set; } = "0px";

        public bool CanDrag { get; set; } = true;
        public bool CanScrollZoom { get; set; } = true;

        // Modifier-Konfiguration
        public bool UseCtrlForAlternativeAction { get; set; } = true;
        public bool UseShiftForAlternativeAction { get; set; } = false;

        // Callbacks für die VM
        public EventCallback<MouseEventArgs> OnClick { get; set; }
        public EventCallback<MouseEventArgs> OnDoubleClick { get; set; }
        public EventCallback<MouseEventArgs> OnMouseDown { get; set; }
        public EventCallback<MouseEventArgs> OnMouseUp { get; set; }
        public EventCallback<MouseEventArgs> OnMouseMove { get; set; }
        public EventCallback<WheelEventArgs> OnWheel { get; set; }

        public bool CanRightclickToCopy { get; set; } = true;
        public bool CanRightclickToDownload { get; set; } = true;
        public bool HasDraggableAnker { get; set; } = true;
    }
}
