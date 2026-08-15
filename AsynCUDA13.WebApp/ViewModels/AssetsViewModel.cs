using AsynCUDA13.Client;
using AsynCUDA13.Shared.MediaDtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
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

        public ImageInfo[]? Images { get; set; }
        public AudioInfo[]? Audios { get; set; }
        public IBrowserFile? ImageUpload { get; set; }
        public IBrowserFile? AudioUpload { get; set; }
        public byte[]? ImageUploadData { get; set; }
        public byte[]? AudioUploadData { get; set; }
        public string ImageUploadName { get; set; } = "image.png";
        public string AudioUploadName { get; set; } = "audio.wav";

        public async Task LoadAssetsAsync()
        {
            this.Images = await this._apiClient.GetImagesAsync();
            this.Audios = await this._apiClient.GetAudiosAsync();
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
            await this.ImportAsync(this.ImageUploadData, this.ImageUploadName, "image/png");
            this.ImageUploadData = null;
        }

        public async Task ImportAudioAsync()
        {
            await this.ImportAsync(this.AudioUploadData, this.AudioUploadName, "audio/wav");
            this.AudioUploadData = null;
        }

        private async Task ImportAsync(byte[]? data, string fileName, string contentType)
        {
            if (data is not { Length: > 0 })
            {
                return;
            }

            await using var stream = new MemoryStream(data);
            var file = new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
            await this._apiClient.UploadMediaAsync(file);
            await this.LoadAssetsAsync();
        }
    }
}