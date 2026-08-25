using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.JSInterop;
using System.Linq;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class HomeViewModel : ViewModelBase<RuntimeInitializeRequest, RuntimeInitializeResponse>
    {
        public HomeViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {

        }


        public RuntimeContextInfo? ContextInfo
        {
            get
            {
                return this._contextInfo;
            }
            private set
            {
                this.SelectedDevice = this.ContextInfo?.DeviceInfo != null
                    ? this.Devices?.FirstOrDefault(d => d.DeviceId == this.ContextInfo.DeviceInfo.DeviceId)
                    : null;
                this._contextInfo = value;
            }
        }
        public RuntimeDeviceInfo[]? Devices { get; set; }

        public RuntimeDeviceInfo? SelectedDevice { get; set; }
        public bool FreeBuffersAtDispose { get; set; } = false;


        public async Task LoadDevicesAsync()
        {
            await this.NotifyStateChangedAsync(true);

            if (this.Api.IsCudaAvailable == false)
            {
                this.UpdateInfoMessage("CUDA is not available on this system. Please ensure that you have a compatible NVIDIA GPU and the necessary drivers installed.", "error", false, null, true);
                return;
            }

            this.Devices = await this.Api.GetRuntimeDevicesAsync();
            this.SelectedDevice = this.Devices?.FirstOrDefault(d => d.DeviceId == this.ContextInfo?.DeviceInfo?.DeviceId) ?? this.Devices?.FirstOrDefault();

            await this.NotifyStateChangedAsync(false);
        }

        public async Task HandleInitializeButton()
        {
            if (this.IsCudaAvailable == false)
            {
                return;
            }

            if (this.IsBackendInitialized)
            {
                await this.DisposeBackendAsync();
            }
            else
            {
                if (this.SelectedDevice == null)
                {
                    // Info Message Popup
                    this.UpdateInfoMessage("Please select a CUDA device before initializing.", "warning", true, null, true);
                    return;
                }

                await this.InitializeDeviceAsync(this.SelectedDevice.DeviceId ?? -1, this.SelectedDevice.DeviceName);
            }
        }

        public async Task InitializeDeviceAsync(int deviceId, string deviceName)
        {
            var response = await this.Api.InitializeRuntimeAsync(deviceId, deviceName);
            if (response?.Success == true)
            {
                if (this.ContextInfo?.DeviceInfo?.DeviceId.HasValue == true)
                {
                    this.SelectedDevice = this.Devices?.FirstOrDefault(d => d.DeviceId == this.ContextInfo.DeviceInfo.DeviceId);
                }

                await this.UpdateInfoMessageAsync($"CUDA context initialized successfully on device: {deviceName}.", "success", true, 3);
            }
            else
            {
                await this.UpdateInfoMessageAsync("Failed to initialize CUDA context.", "error", true, 10);
            }
            await this.NotifyStateChangedAsync();
        }

        public async Task DisposeBackendAsync()
        {
            int? previousDeviceId = this.ContextInfo?.DeviceInfo?.DeviceId;

            var response = await this.Api.DisposeRuntimeAsync(this.FreeBuffersAtDispose);
            if (response?.Success == true)
            {
                await this.UpdateInfoMessageAsync("CUDA context disposed successfully.", "success", true, 3);
            }
            else
            {
                await this.UpdateInfoMessageAsync("Failed to dispose CUDA context.", "error", true, 10);
            }
            await this.NotifyStateChangedAsync();
        }

        public string GetMemInfoSizeKb(RuntimeMemInfo memInfo, int decimals = 2)
        {
            return (long.Parse(memInfo.TotalSize) / 1024.0).ToString($"F{decimals}");
        }
    }
}
