using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.AspNetCore.Components;
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
            if (string.IsNullOrEmpty(this.KernelCode))
            {
                await this.UpdateInfoMessageAsync("Kernel code is empty. Please provide valid kernel code.", "warning", true, 5, true);
                return;
            }

            if (this.Dialog == null)
            {
                return;
            }

            // Wire up the close event for cleanup
            this.Dialog.OnClose = EventCallback.Factory.Create<RuntimeCompileResponse>(this, this.HandleCompileResultAsync);

            await this.HandleCompileKernelAsync();
        }

        public async Task HandleCompileKernelAsync()
        {
            if (string.IsNullOrEmpty(this.KernelCode))
            {
                await this.UpdateInfoMessageAsync("Kernel code is empty. Please provide valid kernel code.", "warning", true, 5, true);
                return;
            }

            this.LastCompileResponse = await this.Api.CompileKernelAsync(this.KernelCode);
            await this.OpenDialogAsync(this.LastCompileResponse, this.HandleCompileResultAsync);
        }

        private async Task HandleCompileResultAsync()
        {
            await this.LoadKernelsAsync();
        }

        public bool IsKernelCompiled(RuntimeKernelInfo kernel) => !string.IsNullOrEmpty(kernel.PtxPath);
    }
}