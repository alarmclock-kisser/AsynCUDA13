using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using System.Linq;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class HomeViewModel
    {
        private readonly ApiClient _apiClient;

        public HomeViewModel(ApiClient apiClient)
        {
            this._apiClient = apiClient;
        }

        public bool IsCudaAvailable => CudaAvailabilityTester.IsCudaAvailable();

        public CudaDeviceInfo[]? Devices { get; set; }

        public CudaDeviceInfo? SelectedDevice { get; set; }

        public CudaContextInfo? ContextInfo { get; set; }

        public bool IsInitialized => this.ContextInfo != null && this.ContextInfo.DeviceInfo != null && this.ContextInfo.DeviceInfo.DeviceId.HasValue;

        public async Task LoadDevicesAsync()
        {
            if (!this.IsCudaAvailable)
            {
                return;
            }

            this.Devices = await this._apiClient.GetCudaDevicesAsync();
        }

        public async Task InitializeDeviceAsync(int deviceId, string deviceName)
            {
                var response = await this._apiClient.InitializeCudaAsync(deviceId, deviceName);
                if (response?.Success == true)
                {
                    this.ContextInfo = await this._apiClient.GetCudaContextInfo();
                    if (this.ContextInfo?.DeviceInfo?.DeviceId.HasValue == true)
                    {
                        this.SelectedDevice = this.Devices?.FirstOrDefault(d => d.DeviceId == this.ContextInfo.DeviceInfo.DeviceId);
                    }
                }
            }

        public async Task DisposeCudaAsync()
            {
                var response = await this._apiClient.DisposeCudaAsync(true);
                if (response?.Success == true)
                {
                    this.ContextInfo = null;
                    this.SelectedDevice = null;
                }
            }

        public string GetMemInfoSizeKb(CudaMemInfo memInfo, int decimals = 2)
        {
            return (long.Parse(memInfo.TotalSize) / 1024.0).ToString($"F{decimals}");
        }
    }
}
