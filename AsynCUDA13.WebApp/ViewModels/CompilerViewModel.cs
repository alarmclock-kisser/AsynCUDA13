using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class CompilerViewModel : ViewModelBase
    {
        public CompilerViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
        }

        public CudaKernelInfo[]? Kernels { get; set; }
        public CudaCompileResponse? LastCompileResponse { get; set; }
        public string? KernelCode { get; set; } = string.Empty;

        public async Task LoadKernelsAsync(bool filterCompiled = true)
        {
            this.Kernels = await this.Api.GetKernelsAsync(filterCompiled);
            this.NotifyStateChanged();
        }

        public async Task<CudaCompileResponse?> CompileKernelAsync(string? kernelCode)
        {
            if (string.IsNullOrEmpty(kernelCode))
            {
                return null;
            }
            this.LastCompileResponse = await this.Api.CompileKernelAsync(kernelCode);
            this.NotifyStateChanged();
            return this.LastCompileResponse;
        }

        public bool IsKernelCompiled(CudaKernelInfo kernel) => !string.IsNullOrEmpty(kernel.PtxPath);
    }
}