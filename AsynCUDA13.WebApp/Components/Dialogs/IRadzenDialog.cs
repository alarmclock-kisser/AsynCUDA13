using Microsoft.AspNetCore.Components;

namespace AsynCUDA13.WebApp.Components.Dialogs
{
    /// <summary>
    /// Schnittstelle für Dialog-Komponenten, die von RadzenDialog verwendet werden.
    /// </summary>
    public interface IRadzenDialog<T, TResult> where T : class where TResult : class
    {
        Type TInput { get; }
        Type TOutput { get; }

        T? DialogRequest { get; set; }

        TResult? DialogResult { get; set; }
        bool? DialogResultSuccessfullySet { get; set; }

        bool Visible { get; set; }
        EventCallback<bool> VisibleChanged { get; set; }

        string Title { get; set; }
        string Width { get; set; }
        string Height { get; set; }
        bool ShowCloseButton { get; set; }
        string Class { get; set; }
        string Style { get; set; }

        Task OpenDialogAsync(T? request = null, TResult? response = null);
        Task CloseDialogAsync();
        void ResetDialogResult();

        EventCallback<T> OnOpen { get; set; }
        EventCallback<TResult> OnClose { get; set; }
    }
}
