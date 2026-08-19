using AsynCUDA13.Client;
using AsynCUDA13.Shared.MediaDtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class AssetsViewModel : ViewModelBase
    {
        public AssetsViewModel(ApiClient apiClient, IJSRuntime js, int maxUploadKb = 16384)
            : base(apiClient, js, maxUploadKb)
        {
        }

        public ImageInfo[] ImageInfos { get; set; } = [];
        public AudioInfo[] AudiosInfos { get; set; } = [];
        public ImageData[] ImagePreviews { get; set; } = [];
        public ImageData[] AudioPreviews { get; set; } = [];
        public int ImagePreviewSize { get; set; } = 512;
        public int AudioPreviewSize { get; set; } = 512;

        public IBrowserFile? MediaUpload { get; set; } = null;

        public async Task LoadAssetsAsync()
        {
            this.ImageInfos = await this.Api.GetImagesAsync();
            this.AudiosInfos = await this.Api.GetAudiosAsync();

            await this.LoadPreviewsAsync();
        }

        public async Task LoadPreviewsAsync()
        {
            if (this.ImageInfos.LongLength > 0)
            {
                var imagePreviewTasks = this.ImageInfos.Select(async image =>
                {
                    return await this.Api.GetImagePreviewAsync(image.Id.ToString(), this.ImagePreviewSize);
                });
                this.ImagePreviews = (await Task.WhenAll(imagePreviewTasks)).Where(i => i is not null).Cast<ImageData>().ToArray();
            }
            if (this.AudiosInfos.LongLength > 0)
            {
                var audioPreviewTasks = this.AudiosInfos.Select(async audio =>
                {
                    return await this.Api.GetAudioWaveformAsync(audio.Id.ToString(), this.AudioPreviewSize, this.AudioPreviewSize / 4);
                });
                this.AudioPreviews = (await Task.WhenAll(audioPreviewTasks)).Where(i => i is not null).Cast<ImageData>().ToArray();
            }

            await this.NotifyStateChangedAsync();
        }

        public async Task DeleteAssetAsync(string idOrName)
        {
            IMediaInfo? mediaInfo = (IMediaInfo?) this.AudiosInfos?.FirstOrDefault(a => a.Id.ToString() == idOrName || a.Name == idOrName) ??
                                   (IMediaInfo?) this.ImageInfos?.FirstOrDefault(i => i.Id.ToString() == idOrName || i.Name == idOrName);
            if (mediaInfo is null)
            {
                await this.UpdateInfoMessageAsync($"Asset with ID or Name '{idOrName}' not found.", "warning", true, 5, true);
                return;
            }

            if (mediaInfo.OnGpu)
            {
                string? freed = await this.Api.FreeMemoryAsync(idOrName);
                if (!string.IsNullOrWhiteSpace(freed))
                {
                    await this.UpdateInfoMessageAsync($"Freed GPU memory for asset '{freed}'.", "info", true, 3);
                }
                else if (this.Api.GetMemoryInfoAsync(idOrName) != null)
                {
                    await this.UpdateInfoMessageAsync($"Failed to free GPU memory for asset '{idOrName}'.", "error", true, 5);
                }
            }

            await this.Api.DeleteMediaAsync(idOrName);
            await this.LoadAssetsAsync();
        }

        public async Task ImportMediaAsync()
        {
            if (this.MediaUpload == null)
            {
                return;
            }

            using var stream = this.MediaUpload.OpenReadStream(this.MaxUploadKb * 1024);
            IFormFile formFile = new FormFile(stream, 0, stream.Length, this.MediaUpload.Name, this.MediaUpload.Name)
            {
                Headers = new HeaderDictionary(),
                ContentType = this.MediaUpload.ContentType
            };

            var mediaInfo = await this.Api.UploadMediaAsync(formFile);

            if (mediaInfo == null)
            {
                await this.UpdateInfoMessageAsync($"Failed to upload media '{this.MediaUpload.Name}'.", "error", true, 5);

            }
            else
            {
                await this.UpdateInfoMessageAsync($"Successfully uploaded media '{mediaInfo.Name}' with ID '{mediaInfo.Id}'.", "success", true, 5);
                await this.LoadAssetsAsync();
            }

            this.MediaUpload = null;
        }
    }
}