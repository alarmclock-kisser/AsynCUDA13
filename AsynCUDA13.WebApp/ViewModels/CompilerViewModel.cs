using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class CompilerViewModel
    {
        private readonly ApiClient _apiClient;

        public CompilerViewModel(ApiClient apiClient)
        {
            this._apiClient = apiClient;
        }

        public CudaKernelInfo[]? Kernels { get; set; }
        public CudaCompileResponse? LastCompileResponse { get; set; }
        public string? KernelCode { get; set; } = string.Empty;

        public async Task LoadKernelsAsync(bool filterCompiled = true)
        {
            this.Kernels = await this._apiClient.GetKernelsAsync(filterCompiled);
        }

        public async Task<CudaCompileResponse?> CompileKernelAsync(string kernelCode)
        {
            this.LastCompileResponse = await this._apiClient.CompileKernelAsync(kernelCode);
            return this.LastCompileResponse;
        }

        public bool IsKernelCompiled(CudaKernelInfo kernel) => !string.IsNullOrEmpty(kernel.PtxPath);
    }
}