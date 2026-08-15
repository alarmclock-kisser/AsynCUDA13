using AsynCUDA13.Client;
using AsynCUDA13.Shared.CudaDtos;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class ExecuteViewModel
    {
        private readonly ApiClient _apiClient;

        public ExecuteViewModel(ApiClient apiClient)
        {
            this._apiClient = apiClient;
        }

        public string GetArgumentValue(int index)
        {
            return (uint)index < (uint) this.ArgumentValues.Length ? this.ArgumentValues[index] : string.Empty;
        }

        public CudaKernelInfo[]? CompiledKernels { get; set; }
        public CudaMemInfo[]? MemoryInfos { get; set; }

        public string? SelectedKernelName { get; set; }
        public string? SelectedIndexPointer { get; set; }
        public bool IsInPlace { get; set; } = false;
        public string[] ArgumentValues { get; private set; } = [];

        public async Task LoadKernelsAsync()
        {
            this.CompiledKernels = await this._apiClient.GetKernelsAsync(true);
        }

        public void PrepareArguments(CudaKernelInfo? kernel)
        {
            this.ArgumentValues = kernel?.ArgumentNames.Select((_, index) =>
                index < this.ArgumentValues.Length ? this.ArgumentValues[index] : string.Empty).ToArray() ?? [];
        }

        public void SetArgumentValue(int index, string? value)
        {
            if ((uint)index < (uint) this.ArgumentValues.Length)
            {
                this.ArgumentValues[index] = value ?? string.Empty;
            }
        }

        public async Task LoadMemoryListAsync()
        {
            this.MemoryInfos = await this._apiClient.GetMemoryListAsync();
        }

        public CudaKernelInfo? GetSelectedKernel()
        {
            if (string.IsNullOrEmpty(this.SelectedKernelName))
                return null;

            return this.CompiledKernels?.FirstOrDefault(k => k.FunctionName.Equals(this.SelectedKernelName, StringComparison.OrdinalIgnoreCase));
        }

        public CudaMemInfo[] GetAvailablePointersForKernel(CudaKernelInfo kernel)
        {
            if (this.MemoryInfos == null || kernel.ArgumentTypes == null || kernel.ArgumentTypes.Length == 0)
                return [];

            // Filter pointers by the element type of the first argument
            var firstArgType = kernel.ArgumentTypes.FirstOrDefault();
            if (string.IsNullOrEmpty(firstArgType))
                return [];

            return this.MemoryInfos
                .Where(m => m.ElementType.Equals(firstArgType, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public async Task<string?> ExecuteKernelAsync(string[]? args = null)
        {
            var kernel = this.GetSelectedKernel();
            if (kernel == null)
                return null;

            var response = await this._apiClient.ExecuteGenericKernelAsync(this.SelectedKernelName ?? string.Empty, args ?? this.ArgumentValues, false);
            return response?.ResultPointer?.ToString();
        }
    }
}