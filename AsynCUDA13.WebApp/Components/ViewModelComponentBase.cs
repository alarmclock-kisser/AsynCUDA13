using AsynCUDA13.WebApp.ViewModels;
using Microsoft.AspNetCore.Components;

namespace AsynCUDA13.WebApp.Components
{
    public abstract class ViewModelComponentBase<TViewModel> : ComponentBase, IDisposable
        where TViewModel : class, IViewModel
    {
        [Inject]
        protected TViewModel VM { get; set; } = default!;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            this.VM.StateChanged += this.OnViewModelStateChanged;
        }

        protected abstract Task LoadViewModelAsync();

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            await this.LoadViewModelAsync();
        }

        private void OnViewModelStateChanged()
        {
            _ = this.InvokeAsync(this.StateHasChanged);
        }

        public void Dispose()
        {
            this.VM.StateChanged -= this.OnViewModelStateChanged;
        }
    }
}
