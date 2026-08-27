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

        public bool FilterCompiled { get; set; } = true;




        public async Task LoadKernelsAsync()
        {
            this.Kernels = await this.Api.GetKernelsAsync(this.FilterCompiled);
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



        public async Task  CompileKernelAsync()
        {
            if (string.IsNullOrEmpty(this.KernelCode))
            {
                return;
            }

            this.LastCompileResponse = await this.Api.CompileKernelAsync(this.KernelCode);
            await this.NotifyStateChangedAsync();
            return;
        }

        public bool IsKernelCompiled(RuntimeKernelInfo kernel) => !string.IsNullOrEmpty(kernel.PtxPath);
    }
}