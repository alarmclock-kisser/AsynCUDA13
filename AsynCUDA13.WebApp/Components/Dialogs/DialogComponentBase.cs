using Microsoft.AspNetCore.Components;
using Radzen.Blazor;

namespace AsynCUDA13.WebApp.Components.Dialogs
{
    /// <summary>
    /// Schnittstelle für Dialog-Komponenten, die von RadzenDialog verwendet werden.
    /// </summary>
    public interface IRadzenDialog<T, TResult> where T : class
    {
        Type TInput { get; }
        Type TOutput { get; }

        T? DialogRequest { get; }

        TResult? DialogResult { get; }
        bool? DialogResultSuccessfullySet { get; }

        bool Visible { get; set; }
        EventCallback<bool> VisibleChanged { get; set; }

        string Title { get; set; }
        string Width { get; set; }
        string Height { get; set; }
        bool ShowCloseButton { get; set; }
        string Class { get; set; }
        string Style { get; set; }

        Task OpenDialogAsync(T? request= null, bool createRequestIfNull = true);
        Task CloseDialogAsync();
        void ResetDialogResult();

        EventCallback<T> OnOpen { get; set; }
        EventCallback<TResult> OnClose { get; set; }
    }

    /// <summary>
    /// Basisklasse für Dialog-Komponenten, die von RadzenDialog verwendet werden.
    /// </summary>
    /// <typeparam name="TRequest">Der Typ des Ergebnisses des Dialogs</typeparam>
    public class DialogComponentBase<TRequest, TResult> : ComponentBase, IRadzenDialog<TRequest, TResult> where TRequest : class where TResult : class
    {
        public Type TInput { get; } = typeof(TRequest);
        public Type TOutput { get; } = typeof(TResult);

        public TRequest? DialogRequest { get; set; }

        public TResult? DialogResult { get; private set; } = null;

        public bool? DialogResultSuccessfullySet { get; private set; } = null;

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
            this.DialogRequest = dialogInput == null ? null : dialogInput;
            this.OnOpen = new EventCallback<TRequest>(this, openEvent);
            this.OnClose = new EventCallback<TResult>(this, closeEvent);
        }


        protected async Task ToggleDialogAsync(bool close = true)
        {
            this.Visible = !close;
            await this.VisibleChanged.InvokeAsync(this.Visible);
        }

        public async Task OpenDialogAsync(TRequest? request= null, bool createRequestIfNull = true)
        {
            this.DialogRequest = request == null && createRequestIfNull ? Activator.CreateInstance<TRequest>() : request;
            this.Visible = true;
            await this.VisibleChanged.InvokeAsync(this.Visible);
            await this.OnOpen.InvokeAsync();
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
