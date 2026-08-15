using AsynCUDA13.Client;
using AsynCUDA13.Shared.CudaDtos;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class MemoryViewModel
    {
        private readonly ApiClient _apiClient;

        public MemoryViewModel(ApiClient apiClient)
        {
            this._apiClient = apiClient;
        }

        public CudaMemInfo[]? MemoryInfos { get; set; }
        public CudaContextInfo? ContextInfo { get; private set; }
        public bool IsCudaInitialized => this.ContextInfo?.DeviceInfo != null;

        public string? SelectedIndexPointer { get; set; }
        public int ChunkSize { get; set; } = 0;
        public float Overlap { get; set; } = 0.5f;
        public bool KeepData { get; set; } = false;
        public bool KeepBuffer { get; set; } = false;

        public async Task LoadMemoryListAsync()
        {
            this.MemoryInfos = await this._apiClient.GetMemoryListAsync();
        }

        public async Task<CudaMemInfo?> GetMemoryInfoAsync(string indexPointerOrId)
        {
            return await this._apiClient.GetMemoryInfoAsync(indexPointerOrId);
        }

        public async Task<string?> PushAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            var response = await this._apiClient.PushAsync(assetIdOrName, chunkSize, overlap, format, keepData);
            return response?.MemoryInfo?.IndexPointer;
        }

        public async Task PullAsync(string indexPointerOrId, bool freeBuffer = true)
        {
            await this._apiClient.PullAsync(indexPointerOrId, freeBuffer);
            await this.LoadMemoryListAsync();
        }

        public async Task<string?> FreeMemoryAsync(string indexPointerOrId)
        {
            var result = await this._apiClient.FreeMemoryAsync(indexPointerOrId);
            await this.LoadMemoryListAsync();
            return result;
        }

        public async Task<string?> PushAssetAsync(string assetIdOrName, bool keepData, bool isAudio)
        {
            var result = await this.PushAsync(assetIdOrName, isAudio ? this.ChunkSize : 0, this.Overlap, isAudio ? "wav" : "png", keepData);
            await this.LoadMemoryListAsync();
            return result;
        }

        public string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} kB";
            return $"{bytes} B";
        }

        public bool Is1D(CudaMemInfo memInfo)
        {
            return memInfo.Pointers.Length <= 1;
        }
    }
}