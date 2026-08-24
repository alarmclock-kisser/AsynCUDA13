using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class CompilerViewModel : ViewModelBase<RuntimeCompileRequest, RuntimeCompileResponse>
    {
        public CompilerViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
        }

        public RuntimeKernelInfo[]? Kernels { get; set; }
        public RuntimeCompileResponse? LastCompileResponse { get; set; }
        public string? KernelCode { get; set; } = string.Empty;

        public async Task LoadKernelsAsync(bool filterCompiled = true)
        {
            this.Kernels = await this.Api.GetKernelsAsync(filterCompiled);
            await this.NotifyStateChangedAsync(true);
        }

        public async Task OpenCompileDialogAsync()
        {
            await this.NotifyStateChangedAsync(false);
        }

        public async Task CloseCompileDialogAsync()
        {
            await this.NotifyStateChangedAsync();
        }



        public async Task<RuntimeCompileResponse?> CompileKernelAsync(string? kernelCode)
        {
            if (string.IsNullOrEmpty(kernelCode))
            {
                return null;
            }

            this.LastCompileResponse = await this.Api.CompileKernelAsync(kernelCode);
            await this.NotifyStateChangedAsync();
            return this.LastCompileResponse;
        }

        public bool IsKernelCompiled(RuntimeKernelInfo kernel) => !string.IsNullOrEmpty(kernel.PtxPath);
    }
}