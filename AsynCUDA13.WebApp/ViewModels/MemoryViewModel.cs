using AsynCUDA13.Client;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class MemoryViewModel : ViewModelBase
    {
        public MemoryViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
        }

        public CudaMemInfo[] MemoryInfos { get; private set; } = [];
        public bool IsCudaInitialized => this._contextInfo?.Online == true;

        public string? SelectedIndexPointer { get; set; }
        public int ChunkSize { get; set; } = 0;
        public float Overlap { get; set; } = 0.5f;
        public bool KeepData { get; set; } = false;
        public bool KeepBuffer { get; set; } = false;

        public async Task LoadMemoryListAsync()
        {
            this._contextInfo = await this.Api.GetCudaContextInfoAsync();
            this.MemoryInfos = this.IsCudaInitialized
                ? await this.Api.GetMemoryListAsync()
                : [];
            await this.NotifyStateChangedAsync(false);
        }

        public async Task<CudaMemInfo?> GetMemoryInfoAsync(string indexPointerOrId)
        {
            return await this.Api.GetMemoryInfoAsync(indexPointerOrId);
        }

        public async Task<string?> PushAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            var response = await this.Api.PushAsync(assetIdOrName, chunkSize, overlap, format, keepData);
            return response?.MemoryInfo?.IndexPointer;
        }

        public async Task PullAsync(string indexPointerOrId, bool freeBuffer = true)
        {
            await this.Api.PullAsync(indexPointerOrId, freeBuffer);
            await this.LoadMemoryListAsync();
        }

        public async Task<string?> FreeMemoryAsync(string indexPointerOrId)
        {
            var result = await this.Api.FreeMemoryAsync(indexPointerOrId);
            await this.LoadMemoryListAsync();
            return result;
        }

        public async Task<string?> PushAssetAsync(string assetIdOrName, bool keepData, bool isAudio)
        {
            var result = await this.PushAsync(assetIdOrName, isAudio ? this.ChunkSize : 0, this.Overlap, isAudio ? "wav" : "png", keepData);
            await this.LoadMemoryListAsync();
            return result;
        }

        public string FormatSize(string? bytes)
        {
            return long.TryParse(bytes, out var parsedBytes)
                ? this.FormatSize(parsedBytes)
                : bytes ?? string.Empty;
        }

        public string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return $"{bytes / (1024.0 * 1024.0):F2} MB";
            }

            if (bytes >= 1024)
            {
                return $"{bytes / 1024.0:F2} kB";
            }

            return $"{bytes} B";
        }

        public bool IsOnDevice(CudaMemInfo memInfo)
        {
            return memInfo.Pointers.Any(pointer =>
                long.TryParse(pointer, out var address) && address != 0);
        }
    }
}