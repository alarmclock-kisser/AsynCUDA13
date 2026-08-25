using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.WebApp.Components.Dialogs;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class MemoryViewModel : ViewModelBase<RuntimePushRequest, RuntimePushResponse>
    {
        public MemoryViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {

        }

        public record AssetItem(Guid Id, string Name, DateTime CreatedAt, bool IsAudio);

        public RuntimeMemInfo[] MemoryInfos { get; private set; } = [];

        public ImageInfo[] ImageInfos { get; private set; } = [];
        public AudioInfo[] AudioInfos { get; private set; } = [];

        public List<AssetItem> AssetDropDownItems { get; set; } = [];

        public Guid? SelectedAssetId { get; set; }
        public string? SelectedAssetName => this.GetAssetName();
        public bool SelectedAssetIsAudio => this.IsAssetAudio();

        public bool? AssetOnHost { get; set; } = null;  // true = on host, false = not on host, null = none selected
        public bool? AssetOnGpu { get; set; } = null;  // true = on GPU, false = not on GPU, null = none selected
        public bool AssetPushable => this.SelectedAssetId.HasValue && this.AssetOnHost.HasValue && this.AssetOnHost.Value;  // true = can push to GPU (is on host), false = cannot push to GPU (is on GPU or none selected)
        public bool AssetPullable => this.SelectedAssetId.HasValue && this.AssetOnGpu.HasValue && this.AssetOnGpu.Value;  // true = can pull from GPU (is on GPU), false = cannot pull from GPU (is on host or none selected)

        public string? SelectedIndexPointer { get; set; }
        public int ChunkSize { get; set; } = 0;
        public float Overlap { get; set; } = 0.5f;
        public string ImageFormat { get; set; } = "JPEG";
        public bool KeepData { get; set; } = false;
        public bool KeepBuffer { get; set; } = false;


        // Setup data
        public async Task LoadMemoryListAsync()
        {
            this.MemoryInfos = this.IsBackendInitialized
                ? await this.Api.GetMemoryListAsync()
                : [];
            await this.LoadAssetsAsync();
        }

        private async Task LoadAssetsAsync()
        {
            this.ImageInfos = await this.Api.GetImageInfosAsync();
            this.AudioInfos = await this.Api.GetAudioInfosAsync();
            this.AssetDropDownItems = await this.GetAssetDropDownItemsAsync();
            await this.NotifyStateChangedAsync(true);
        }


        // Event handlers
        public async Task OnSelectedAssetChangedAsync()
        {
            this.AssetOnHost = await this.IsAssetOnHostAsync();
            this.AssetOnGpu = await this.IsAssetOnGpuAsync();
            await this.NotifyStateChangedAsync(false);
        }


        // Dialog handling
        public async Task OpenPushDialogAsync()
        {
            if (this.SelectedAssetId == null || this.SelectedAssetId.Equals(Guid.Empty))
            {
                await this.UpdateInfoMessageAsync("No asset selected for pushing.", "error", true, 5, true);
                return;
            }

            this.ChunkSize = 0;
            this.Overlap = 0.5f;
            this.KeepData = false;
            await this.OpenDialogAsync(null);
            await this.NotifyStateChangedAsync(false);
        }

        public async Task ClosePushDialogAsync()
        {
            this.ShowDialog = false;
            this.SelectedAssetId = null;

            await this.CloseDialogAsync();
            var dialog = this.Dialog as CudaPushDialog;
            this.ChunkSize = dialog?.ChunkSizeValue ?? 0;
            this.Overlap = dialog?.OverlapValue ?? 0.5f;
            this.ImageFormat = dialog?.ImageFormatValue ?? "JPEG";
            this.KeepData = dialog?.KeepDataValue ?? false;

            await this.LoadMemoryListAsync();
        }
        

        // Handlers
        public async Task HandlePushAsync(Guid? assetId = null)
        {
            assetId ??= this.SelectedAssetId;
            if (assetId == null || assetId.Equals(Guid.Empty))
            {
                return;
            }

            int chunkSize = this.ChunkSize;
            float overlap = this.Overlap;
            bool keepData = this.KeepData;

            var result = await this.PushAssetAsync(assetId.Value.ToString(), keepData);

            if (!string.IsNullOrEmpty(result))
            {
                await this.PutInfoMessageAsync($"Asset pushed successfully. IndexPointer: {result}", "success", true, 3);
            }
            else
            {
                await this.PutInfoMessageAsync("Failed to push asset.", "error", true, 5);
            }

            await this.ClosePushDialogAsync();
        }

        public async Task HandlePullAsync(string? assetIdOrPtr = null)
        {
            assetIdOrPtr ??= this.SelectedAssetId?.ToString() ?? this.SelectedIndexPointer;
            if (string.IsNullOrEmpty(assetIdOrPtr))
            {
                await this.PutInfoMessageAsync("No asset or index pointer selected for pulling.", "error", true, 5);
                return;
            }

            await this.PullAssetAsync(assetIdOrPtr);
            await this.PutInfoMessageAsync($"Memory pulled successfully for asset / index pointer: {assetIdOrPtr}", "success", true, 3);
        }

        public async Task<string?> FreeMemoryAsync(string? indexPointerOrId = null)
        {
            indexPointerOrId ??= this.SelectedAssetId?.ToString() ?? this.SelectedIndexPointer;
            if (string.IsNullOrEmpty(indexPointerOrId))
            {
                await this.PutInfoMessageAsync("No index pointer or asset ID selected for freeing memory.", "error", true, 5);
                return null;
            }

            var result = await this.Api.FreeMemoryAsync(indexPointerOrId);
            await this.LoadMemoryListAsync();
            return result;
        }



        // Helpers (public, sync)
        public bool? IsAssetOnHost(Guid? assetId = null)
        {
            assetId ??= this.SelectedAssetId;
            if (assetId == null || assetId.Equals(Guid.Empty))
            {
                return null;
            }
            return this.MemoryInfos.Any(m => m.Id.Equals(assetId.Value));
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


        // Helpers (private, async)
        private async Task<List<AssetItem>> GetAssetDropDownItemsAsync(bool requireOnHost = true, bool orderLatestFirst = true)
        {
            ConcurrentDictionary<Guid, AssetItem> assetItems = [];

            assetItems = new ConcurrentDictionary<Guid, AssetItem>(
                this.ImageInfos.AsParallel().Where(i => !requireOnHost || !i.OnGpu).Select(i => new KeyValuePair<Guid, AssetItem>(i.Id, new AssetItem(i.Id, i.Name, i.CreatedAt, false)))
                .Concat(this.AudioInfos.AsParallel().Where(a => !requireOnHost || !a.OnGpu).Select(a => new KeyValuePair<Guid, AssetItem>(a.Id, new AssetItem(a.Id, a.Name, a.CreatedAt, true))))
            );

            return assetItems.Values.OrderByDescending(a => orderLatestFirst ? a.CreatedAt : DateTime.MinValue).ToList();
        }

        private async Task<string?> PullAssetAsync(string indexPointerOrId, bool? freeBuffer = null)
        {
            var response = await this.Api.PullAsync(indexPointerOrId, true, freeBuffer ?? !this.KeepBuffer);
            if (response == null || !response.Success)
            {
                await this.PutInfoMessageAsync($"Failed to pull memory for index pointer or asset ID: {indexPointerOrId}", "error", true, 5);
                return null;
            }

            await this.LoadMemoryListAsync();
            return response?.MemoryInfoReference?.IndexPointer;
        }

        private async Task<string?> PushAssetAsync(string assetIdOrName, bool? keepData = null)
        {
            bool? isAudio = await this.Api.IsAssetAudioAsync(assetIdOrName);
            if (isAudio == null)
            {
                await this.PutInfoMessageAsync($"Asset '{assetIdOrName}' not found.", "error", true, 5);
                return null;
            }

            var result = await this.Api.PushAsync(assetIdOrName, true, isAudio == true ? this.ChunkSize : 0, this.Overlap, isAudio == true ? "wav" : this.ImageFormat, keepData ?? this.KeepData);
            await this.LoadMemoryListAsync();
            return result?.MemoryInfo?.IndexPointer;
        }

        private async Task<bool?> IsAssetOnHostAsync(Guid? assetId = null)
        {
            assetId ??= this.SelectedAssetId;
            if (assetId == null || assetId.Equals(Guid.Empty))
            {
                return null;
            }
            string? ptr = await this.Api.GetIndexPointerForAssetIdAsync(assetId.Value);

            return string.IsNullOrEmpty(ptr) || ptr.Equals("0") || ptr.Equals(Guid.Empty.ToString()) || ptr.Equals(IntPtr.Zero.ToString());
        }

        private async Task<bool?> IsAssetOnGpuAsync(Guid? assetId = null)
        {
            assetId ??= this.SelectedAssetId;
            if (assetId == null || assetId.Equals(Guid.Empty))
            {
                return null;
            }
            string? ptr = await this.Api.GetIndexPointerForAssetIdAsync(assetId.Value);

            return !string.IsNullOrEmpty(ptr) && !ptr.Equals("0") && !ptr.Equals(Guid.Empty.ToString()) && !ptr.Equals(IntPtr.Zero.ToString());
        }


        // Helpers (private, sync)
        private string? GetAssetName(Guid? assetId = null)
        {
            assetId ??= this.SelectedAssetId;
            if (assetId == null || assetId.Equals(Guid.Empty))
            {
                return null;
            }

            var image = this.ImageInfos.FirstOrDefault(i => i.IdMatch(assetId.Value));
            if (image != null)
            {
                return image.Name;
            }

            var audio = this.AudioInfos.FirstOrDefault(a => a.IdMatch(assetId.Value));
            if (audio != null)
            {
                return audio.Name;
            }

            return null;
        }

        private bool IsAssetAudio(Guid? assetId = null)
        {
            assetId ??= this.SelectedAssetId;
            if (assetId == null || assetId.Equals(Guid.Empty))
            {
                return false;
            }
            var audio = this.AudioInfos.FirstOrDefault(a => a.IdMatch(assetId.Value));
            return audio != null;
        }

    }
}