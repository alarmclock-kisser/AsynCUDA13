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

        public RuntimeKernelInfo[]? CompiledKernels { get; set; }
        public RuntimeMemInfo[]? MemoryInfos { get; set; }

        public RuntimeKernelInfo? SelectedKernelInfo { get; set; }
        public bool CanExecute => this.SelectedKernelInfo?.ArgumentsCount >= 0;

        public RuntimeExecuteRequest ExecuteRequest { get; set; } = new();
        public RuntimeExecuteResponse? ExecuteResponse { get; set; } = null;

        public string? SelectedKernelName
        {
            get;
            set
            {
                this.SelectedKernelInfo = this.CompiledKernels?.FirstOrDefault(k => k.FunctionName.Equals(value, StringComparison.OrdinalIgnoreCase));
                this.ExecuteRequest.KernelInfo = this.SelectedKernelInfo;
                field = value;
            }
        }


        public async Task LoadKernelsAndMemoryInfosAsync()
        {
            this.MemoryInfos = await this.Api.GetMemoryListAsync();
            this.CompiledKernels = await this.Api.GetKernelsAsync(true);
            await this.NotifyStateChangedAsync();
        }

        public async Task OnSelectedKernelChanged()
        {
            if (this.SelectedKernelInfo == null)
            {
                await this.PutInfoMessageAsync("No kernel selected. Please select a kernel to execute.", "warning", true, 5);
            }

            await this.NotifyStateChangedAsync(false);
        }


        public RuntimeMemInfo[] GetPointersForArgumentType(string argType)
        {
            if (string.IsNullOrWhiteSpace(argType) || this.MemoryInfos == null || this.MemoryInfos.Length == 0)
            {
                return [];
            }

            return this.MemoryInfos.Where(m => m.ElementType.Equals(argType.Replace("*", "").Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        }





        public async Task ExecuteKernelAsync()
        {
            if (this.SelectedKernelInfo == null)
            {
                await this.UpdateInfoMessageAsync("No kernel selected. Please select a kernel to execute.", "warning", true, 5, true);
                return;
            }

            this.ExecuteResponse = await this.Api.ExecuteGenericKernelAsync(this.SelectedKernelInfo.FunctionName, this.ExecuteRequest.ArgumentValues, false);
        }
    }
}