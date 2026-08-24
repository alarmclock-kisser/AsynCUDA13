using Microsoft.AspNetCore.Components;

namespace AsynCUDA13.WebApp.ViewModels
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

    public static class ViewModelFunctions
    {
        /// <summary>
        /// Calculates the next power of two based on the current value and the previous value. If the current value is greater than the previous value, it doubles the previous value. If the current value is less than the previous value, it halves the previous value. The result is clamped between the specified minimum and maximum values.
        /// </summary>
        /// <param name="value">The current value.</param>
        /// <param name="oldValue">The previous value.</param>
        /// <param name="min">The minimum allowable value.</param>
        /// <param name="max">The maximum allowable value.</param>
        /// <returns>The next power of two value, clamped between the specified minimum and maximum values.</returns>
        public static int GetPowerOfTwo(int value, int oldValue, int min = 0, int max = 262144)
        {
            if (value > oldValue)
            {
                if (oldValue == 0)
                {
                    return Math.Clamp(1, min, max);
                }
                return Math.Clamp(oldValue * 2, min, max);
            }
            else if (value < oldValue)
            {
                return Math.Clamp(oldValue / 2, min, max);
            }
            else
            {
                return oldValue;
            }
        }
    }
}
