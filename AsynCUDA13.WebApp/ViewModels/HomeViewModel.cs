using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.JSInterop;
using System.Linq;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        public HomeViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
            this.IsCudaAvailable = CudaAvailabilityTester.IsCudaAvailable();
        }

        public readonly bool IsCudaAvailable;

        public CudaDeviceInfo[]? Devices { get; set; }

        public CudaDeviceInfo? SelectedDevice { get; set; }
        public bool FreeBuffersAtDispose { get; set; } = false;


        public bool IsInitialized => this.ContextInfo?.Online == true;

        public async Task LoadDevicesAsync()
        {
            if (!this.IsCudaAvailable)
            {
                this.UpdateInfoMessage("CUDA is not available on this system. Please ensure that you have a compatible NVIDIA GPU and the necessary drivers installed.", "error", false, null, true);
                return;
            }

            this.Devices = await this.Api.GetCudaDevicesAsync();
            await this.NotifyStateChangedAsync();
        }

        public async Task HandleInitializeButton()
        {
            if (!this.IsCudaAvailable)
            {
                return;
            }

            if (this.IsInitialized)
            {
                await this.DisposeCudaAsync();
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
            var response = await this.Api.InitializeCudaAsync(deviceId, deviceName);
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

        public async Task DisposeCudaAsync()
        {
            var response = await this.Api.DisposeCudaAsync(this.FreeBuffersAtDispose);
            if (response?.Success == true)
            {
                this.SelectedDevice = null;
                await this.UpdateInfoMessageAsync("CUDA context disposed successfully.", "success", true, 3);
            }
            else
            {
                await this.UpdateInfoMessageAsync("Failed to dispose CUDA context.", "error", true, 10);
            }
            await this.NotifyStateChangedAsync();
        }

        public string GetMemInfoSizeKb(CudaMemInfo memInfo, int decimals = 2)
        {
            return (long.Parse(memInfo.TotalSize) / 1024.0).ToString($"F{decimals}");
        }
    }
}
