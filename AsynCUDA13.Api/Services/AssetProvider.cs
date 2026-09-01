using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Shared.Interfaces;
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

        public IMediaInfo GetImageInfo(ImageObj image)
        {
            return MediaInfosBuilder.BuildImageInfo(image);
        }

        public IMediaInfo GetAudioInfo(AudioObj audio)
        {
            return MediaInfosBuilder.BuildAudioInfo(audio);
        }

        public IMediaInfo? GetImageInfo(Guid imageId)
        {
            var obj = this.images[imageId];
            return obj != null ? this.GetImageInfo(obj) :  null;
        }

        public IMediaInfo? GetAudioInfo(Guid audioId)
        {
            var obj = this.audios[audioId];
            return obj != null ? this.GetAudioInfo(obj) :  null;
        }

        public IMediaObj? CreateFromInfo(IMediaInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true, bool emptyData = false, long? pointer = 0)
        {
            if (info is AudioInfo audioInfo)
            {
                return this.audios.CreateFromInfo(audioInfo, tryAdd, disposeIfFailedToAdd, emptyData, pointer);
            }
            else if (info is ImageInfo imageInfo)
            {
                return this.images.CreateFromInfo(imageInfo, tryAdd, disposeIfFailedToAdd, emptyData, pointer);
            }
            return null;
        }

        public Guid? VerifyAssetId(Guid id)
        {
            if (id == Guid.Empty)
            {
                return null;
            }

            // Prüfe erst in audios
            if (this.audios[id] is AudioObj audio)
            {
                return audio.Id;
            }

            // Dann prüfe in images
            if (this.images[id] is ImageObj image)
            {
                return image.Id;
            }

            return null; // Nicht gefunden
        }

        public Guid[] VerifyAssetIds(IEnumerable<Guid> ids)
        {
            return ids == null
                ?  []
                : ids
                .Select(this.VerifyAssetId)
                .OfType<Guid>()
                .Where(g => g != Guid.Empty)
                .ToArray();
        }

        public Guid? GetAssetIdByPointer(long pointer)
        {
            if (pointer == 0)
            {
                return null;
            }
            // Prüfe erst in audios
            var audio = this.audios.Audios.FirstOrDefault(a => a.Pointer == pointer);
            if (audio != null)
            {
                return audio.Id;
            }
            // Dann prüfe in images
            var image = this.images.Images.FirstOrDefault(i => i.Pointer == pointer);
            if (image != null)
            {
                return image.Id;
            }
            return null; // Nicht gefunden
        }

        public Guid[] GetAssetIdsByPointers(IEnumerable<long> pointers)
        {
            return pointers == null
                ?  []
                : pointers
                .Select(this.GetAssetIdByPointer)
                .OfType<Guid>()
                .Where(g => g != Guid.Empty)
                .ToArray();
        }
    }
}