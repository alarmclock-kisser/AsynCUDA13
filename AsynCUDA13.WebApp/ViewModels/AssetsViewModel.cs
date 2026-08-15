using AsynCUDA13.Client;
using AsynCUDA13.Shared.MediaDtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class AssetsViewModel
    {
        private readonly ApiClient _apiClient;

        public AssetsViewModel(ApiClient apiClient)
        {
            this._apiClient = apiClient;
        }

        public ImageInfo[]? ImageInfos { get; set; }
        public AudioInfo[]? AudiosInfos { get; set; }
        public ImageData[] ImagePreviews { get; set; } = [];
        public ImageData[] AudioPreviews { get; set; } = [];
        public int ImagePreviewSize { get; set; } = 512;
        public int AudioPreviewSize { get; set; } = 512;
        public IBrowserFile? ImageUpload { get; set; }
        public IBrowserFile? AudioUpload { get; set; }
        public string ImageUploadName { get; set; } = "image.png";
        public string AudioUploadName { get; set; } = "audio.wav";

        public async Task LoadAssetsAsync()
        {
            this.ImageInfos = await this._apiClient.GetImagesAsync();
            this.AudiosInfos = await this._apiClient.GetAudiosAsync();

            await this.LoadPreviewsAsync();
        }

        public async Task LoadPreviewsAsync()
        {
            if (this.ImageInfos is not null)
            {
                var imagePreviewTasks = this.ImageInfos.Select(async image =>
                {
                    return await this._apiClient.GetImagePreviewAsync(image.Id.ToString(), this.ImagePreviewSize);
                });
                this.ImagePreviews = (await Task.WhenAll(imagePreviewTasks)).Where(i => i is not null).Cast<ImageData>().ToArray();
            }
            if (this.AudiosInfos is not null)
            {
                var audioPreviewTasks = this.AudiosInfos.Select(async audio =>
                {
                    return await this._apiClient.GetAudioWaveformAsync(audio.Id.ToString(), this.AudioPreviewSize, this.AudioPreviewSize / 4);
                });
                this.AudioPreviews = (await Task.WhenAll(audioPreviewTasks)).Where(i => i is not null).Cast<ImageData>().ToArray();
            }
        }

        public async Task DeleteAssetAsync(string idOrName, bool hasCudaPointer = false, string? indexPointer = null)
        {
            if (hasCudaPointer && !string.IsNullOrEmpty(indexPointer))
            {
                await this._apiClient.FreeMemoryAsync(indexPointer);
            }
            await this._apiClient.DeleteMediaAsync(idOrName);
        }

        public bool HasImageCudaPointer(ImageInfo image) => !string.IsNullOrEmpty(image.Pointer);
        public bool HasAudioCudaPointer(AudioInfo audio) => !string.IsNullOrEmpty(audio.Pointer);

        public async Task ImportImageAsync()
        {
            if (this.ImageUpload == null)
            {
                return;
            }

            using var stream = this.ImageUpload.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var file = new FormFile(memoryStream, 0, memoryStream.Length, "file", this.ImageUpload.Name)
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
            await this._apiClient.UploadMediaAsync(file);
            this.ImageUpload = null;
            await this.LoadAssetsAsync();
        }

        public async Task ImportAudioAsync()
        {
            if (this.AudioUpload == null)
            {
                return;
            }

            using var stream = this.AudioUpload.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var file = new FormFile(memoryStream, 0, memoryStream.Length, "file", this.AudioUpload.Name)
            {
                Headers = new HeaderDictionary(),
                ContentType = "audio/wav"
            };
            await this._apiClient.UploadMediaAsync(file);
            this.AudioUpload = null;
            await this.LoadAssetsAsync();
        }
    }
}