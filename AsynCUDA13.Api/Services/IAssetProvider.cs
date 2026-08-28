using AsynCUDA13.Media;
using AsynCUDA13.Shared.MediaDtos;

namespace AsynCUDA13.Api.Services
{
    public interface IAssetProvider
    {
        ImageObj? GetImage(Guid id);
        ImageObj? GetImage(string name);
        AudioObj? GetAudio(Guid id);
        AudioObj? GetAudio(string name);
        ImageInfo GetImageInfo(ImageObj image);
        AudioInfo GetAudioInfo(AudioObj audio);
        ImageInfo? GetImageInfo(Guid imageId);
        AudioInfo? GetAudioInfo(Guid audioId);

        AudioObj? CreateFromInfo(AudioInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true);
        ImageObj? CreateFromInfo(ImageInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true);
    }
}