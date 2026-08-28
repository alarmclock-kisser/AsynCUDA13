using Microsoft.AspNetCore.Components;
using Radzen.Blazor;

namespace AsynCUDA13.WebApp.Components.Dialogs
{
    /// <summary>
    /// Basisklasse für Dialog-Komponenten, die von RadzenDialog verwendet werden.
    /// </summary>
    /// <typeparam name="TRequest">Der Typ des Ergebnisses des Dialogs</typeparam>
    public class DialogComponentBase<TRequest, TResult> : ComponentBase, IRadzenDialog<TRequest, TResult> where TRequest : class where TResult : class
    {
        public Type TInput { get; } = typeof(TRequest);
        public Type TOutput { get; } = typeof(TResult);

        public TRequest? DialogRequest { get; set; }

        public TResult? DialogResult { get; set; } = null;

        public bool? DialogResultSuccessfullySet { get; set; } = null;

        // Sichtbarkeit des Dialogs
        [Parameter]
        public bool Visible { get; set; } = false;

        [Parameter]
        public EventCallback<bool> VisibleChanged { get; set; }

        // Titel des Dialogs
        [Parameter]
        public string Title { get; set; } = "Dialog";

        // Breite des Dialogs
        [Parameter]
        public string Width { get; set; } = "50vw";

        // Höhe des Dialogs
        [Parameter]
        public string Height { get; set; } = "50vh";

        // Ob der Schließen-Button angezeigt wird
        [Parameter]
        public bool ShowCloseButton { get; set; } = true;

        // CSS-Klasse für den Dialog
        [Parameter]
        public string Class { get; set; } = "";

        // Inline-Stile für den Dialog
        [Parameter]
        public string Style { get; set; } = "";

        [Parameter]
        public EventCallback<TRequest> OnOpen { get; set; }

        [Parameter]
        public EventCallback<TResult> OnClose { get; set; }

        protected DialogComponentBase()
        {

        }

        public DialogComponentBase(TRequest? dialogInput = null, Func<TRequest>? openEvent = null, Func<TResult>? closeEvent = null)
        {
            this.DialogRequest = dialogInput ?? null;
            this.OnOpen = new EventCallback<TRequest>(this, openEvent);
            this.OnClose = new EventCallback<TResult>(this, closeEvent);
        }


        protected async Task ToggleDialogAsync(bool close = true)
        {
            this.Visible = !close;
            await this.VisibleChanged.InvokeAsync(this.Visible);
        }

        public async Task OpenDialogAsync(TRequest? request= null, TResult? response = null)
        {
            this.DialogRequest = request;
            this.DialogResult = response;
            await this.VisibleChanged.InvokeAsync(this.Visible);
            await this.OnOpen.InvokeAsync();
            this.StateHasChanged();
        }

        public async Task CloseDialogAsync()
        {
            if (!this.Visible)
            {
                return;
            }

            this.Visible = false;
            await this.VisibleChanged.InvokeAsync(this.Visible);
            await this.OnClose.InvokeAsync();
            this.StateHasChanged();
        }

        protected bool SetDialogResult(TResult? result)
        {
            this.DialogResult = result;
            this.DialogResultSuccessfullySet = result != null;
            return result != null;
        }

        public void ResetDialogResult()
        {
            this.DialogResult = null;
            this.DialogResultSuccessfullySet = null;
        }
    }
}
