using AsynCUDA13.Client;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.WebApp.Components;
using AsynCUDA13.WebApp.Components.Dialogs;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using Radzen;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class AssetsViewModel : ViewModelBase<string, string>
    {
        public AssetsViewModel(ApiClient apiClient, IJSRuntime js, int maxUploadKb = 65536)
            : base(apiClient, js, maxUploadKb)
        {
        }

        public CudaContextInfo? ContextInfo => this._contextInfo;

        public ImageInfo[] ImageInfos { get; set; } = [];
        public AudioInfo[] AudiosInfos { get; set; } = [];
        public ImageData[] ImagePreviews { get; set; } = [];
        public ImageData[] AudioPreviews { get; set; } = [];
        public int ImagePreviewSize { get; set; } = 512;
        public int AudioPreviewSize { get; set; } = 512;

        public IBrowserFile? MediaUpload { get; set; } = null;

        public async Task LoadAssetsAsync()
        {
            this.ImageInfos = await this.Api.GetImageInfosAsync();
            this.AudiosInfos = await this.Api.GetAudioInfosAsync();

            await this.LoadPreviewsAsync();
        }

        public async Task LoadPreviewsAsync()
        {
            this.AudioPreviews = this.AudiosInfos.Length > 0 ? await this.Api.GetAudioWaveformsAsync(this.AudiosInfos.Select(a => a.Id.ToString()).ToArray(), this.AudioPreviewSize) ?? [] : [];
            this.ImagePreviews = this.ImageInfos.Length > 0 ? await this.Api.GetImagePreviewsAsync(this.ImageInfos.Select(i => i.Id.ToString()).ToArray(), this.ImagePreviewSize) ?? [] : [];

            await this.NotifyStateChangedAsync(false);
        }

        public async Task DeleteAssetAsync(string idOrName)
        {
            string? mediaId = (string?) this.AudiosInfos.FirstOrDefault(a => a.Id.ToString() == idOrName || a.Name == idOrName)?.Id.ToString() ??
                                   (string?) this.ImageInfos.FirstOrDefault(i => i.Id.ToString() == idOrName || i.Name == idOrName)?.Id.ToString();
            if (mediaId is null)
            {
                await this.UpdateInfoMessageAsync($"Asset with ID or Name '{idOrName}' not found.", "warning", true, 5, true);
                return;
            }
            bool hasPointer = this.AudiosInfos.Any(a => a.Id.ToString() == mediaId && a.OnGpu) ||
                              this.ImageInfos.Any(i => i.Id.ToString() == mediaId && i.OnGpu);

            string? freed = (this._contextInfo?.Online == true && hasPointer) ? await this.Api.FreeMemoryAsync(idOrName) : null;
            if (!string.IsNullOrWhiteSpace(freed))
            {
                await this.UpdateInfoMessageAsync($"Freed GPU memory for asset '{freed}'.", "info", true, 3);
            }
            else if (hasPointer)
            {
                await this.UpdateInfoMessageAsync($"Asset '{idOrName}' is on GPU but could not free memory. It may be in use.", "warning", true, 5, true);
            }

            await this.Api.DeleteMediaAsync(idOrName);
            await this.LoadAssetsAsync();
        }

        public async Task OnInputFileChange(InputFileChangeEventArgs e)
        {
            var file = e.File;
            if (file == null)
            {
                await this.UpdateInfoMessageAsync("No file selected.", "warning", true, 2, true);
                return;
            }
            try
            {
                using var stream = file.OpenReadStream((long) (this.MaxUploadKb * 1024));
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var fileParameter = new FileParameter(new MemoryStream(bytes), file.Name, file.ContentType);

                string guid = await this.Api.UploadMediaAsync(fileParameter) ?? throw new Exception("Upload failed, no ID returned.");

                this.MediaUpload = null;
                await this.LoadAssetsAsync();
                await this.UpdateInfoMessageAsync("Image uploaded successfully", "success", true, 2, true);
            }
            catch (Exception ex)
            {
                await this.UpdateInfoMessageAsync($"Upload failed: {ex.Message}", "error", true, 4, true);
            }
        }

    }
}