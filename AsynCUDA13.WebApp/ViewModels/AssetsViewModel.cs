using AsynCUDA13.Client;
using AsynCUDA13.Shared.MediaDtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

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
    }
}