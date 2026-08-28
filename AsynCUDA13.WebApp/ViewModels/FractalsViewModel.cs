using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class FractalsViewModel : ViewModelBase<RuntimeExecuteRequest, RuntimeExecuteResponse>
    {

        public FractalsViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
        }
        public RuntimeKernelInfo[]? CompiledKernels { get; set; }
        public RuntimeMemInfo[]? MemoryInfos { get; set; }
        public List<string> TakenArgTypePointers { get; set; } = new();
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
    }
}