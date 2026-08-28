using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Shared.MediaDtos;

namespace AsynCUDA13.Api.Services
{
    public class AssetProvider : IAssetProvider
    {
        private readonly ImageCollection images;
        private readonly AudioCollection audios;

        public AssetProvider(ImageCollection images, AudioCollection audios)
        {
            this.images = images;
            this.audios = audios;
        }

        public ImageObj? GetImage(Guid id)
        {
            return this.images[id];
        }

        public ImageObj? GetImage(string name)
        {
            return this.images[name];
        }

        public AudioObj? GetAudio(Guid id)
        {
            return this.audios[id];
        }

        public AudioObj? GetAudio(string name)
        {
            return this.audios[name, false];
        }

        public ImageInfo GetImageInfo(ImageObj image)
        {
            return MediaInfosBuilder.BuildImageInfo(image);
        }

        public AudioInfo GetAudioInfo(AudioObj audio)
        {
            return MediaInfosBuilder.BuildAudioInfo(audio);
        }

        public ImageInfo? GetImageInfo(Guid imageId)
        {
            var obj = this.images[imageId];
            if (obj != null)
            {
                return this.GetImageInfo(obj);
            }
            return null;
        }

        public AudioInfo? GetAudioInfo(Guid audioId)
        {
            var obj = this.audios[audioId];
            if (obj != null)
            {
                return this.GetAudioInfo(obj);
            }
            return null;
        }

        public AudioObj? CreateFromInfo(AudioInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true)
        {
            return this.audios.CreateFromInfo(info, tryAdd, disposeIfFailedToAdd);
        }

        public ImageObj? CreateFromInfo(ImageInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true)
        {
            return this.images.CreateFromInfo(info, tryAdd, disposeIfFailedToAdd);
        }
    }
}