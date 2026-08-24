using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class ExecuteViewModel : ViewModelBase<RuntimeExecuteRequest, RuntimeExecuteResponse>
    {
        public ExecuteViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
        }

        public string GetArgumentValue(int index)
        {
            return (uint) index < (uint) this.ArgumentValues.Length ? this.ArgumentValues[index] : string.Empty;
        }

        public RuntimeKernelInfo[]? CompiledKernels { get; set; }
        public RuntimeMemInfo[]? MemoryInfos { get; set; }

        public string? SelectedKernelName { get; set; }
        public string? SelectedIndexPointer { get; set; }
        public bool IsInPlace { get; set; } = false;
        public string[] ArgumentValues { get; private set; } = [];

        public async Task LoadKernelsAsync()
        {
            this.CompiledKernels = await this.Api.GetKernelsAsync(true);
            this.NotifyStateChanged();
        }

        public void PrepareArguments(RuntimeKernelInfo? kernel)
        {
            this.ArgumentValues = kernel?.ArgumentNames.Select((_, index) =>
                index < this.ArgumentValues.Length ? this.ArgumentValues[index] : string.Empty).ToArray() ?? [];
        }

        public void SetArgumentValue(int index, string? value)
        {
            if ((uint) index < (uint) this.ArgumentValues.Length)
            {
                this.ArgumentValues[index] = value ?? string.Empty;
            }
        }

        public async Task LoadMemoryListAsync()
        {
            this.MemoryInfos = await this.Api.GetMemoryListAsync();
            this.NotifyStateChanged();
        }

        public RuntimeKernelInfo? GetSelectedKernel()
        {
            if (string.IsNullOrEmpty(this.SelectedKernelName))
            {
                return null;
            }

            return this.CompiledKernels?.FirstOrDefault(k => k.FunctionName.Equals(this.SelectedKernelName, StringComparison.OrdinalIgnoreCase));
        }

        public RuntimeMemInfo[] GetAvailablePointersForKernel(RuntimeKernelInfo kernel)
        {
            if (this.MemoryInfos == null || kernel.ArgumentTypes == null || kernel.ArgumentTypes.Length == 0)
            {
                return [];
            }

            // Filter pointers by the element type of the first argument
            var firstArgType = kernel.ArgumentTypes.FirstOrDefault();
            if (string.IsNullOrEmpty(firstArgType))
            {
                return [];
            }

            return this.MemoryInfos
                .Where(m => m.ElementType.Equals(firstArgType, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public async Task<string?> ExecuteKernelAsync(string[]? args = null)
        {
            var kernel = this.GetSelectedKernel();
            if (kernel == null)
            {
                return null;
            }

            var response = await this.Api.ExecuteGenericKernelAsync(this.SelectedKernelName ?? string.Empty, args ?? this.ArgumentValues, false);
            return response?.ResultPointer?.ToString();
        }
    }
}