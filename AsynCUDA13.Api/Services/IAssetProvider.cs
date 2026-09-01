using AsynCUDA13.Media;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.MediaDtos;

namespace AsynCUDA13.Api.Services
{
    public interface IAssetProvider
    {
        ImageObj? GetImage(Guid id);
        ImageObj? GetImage(string name);
        AudioObj? GetAudio(Guid id);
        AudioObj? GetAudio(string name);

        IMediaInfo GetImageInfo(ImageObj image);
        IMediaInfo GetAudioInfo(AudioObj audio);
        IMediaInfo? GetImageInfo(Guid imageId);
        IMediaInfo? GetAudioInfo(Guid audioId);

        IMediaObj? CreateFromInfo(IMediaInfo info, bool tryAdd = true, bool disposeIfFailedToAdd = true, bool emptyData = false, long? pointer = 0);

        Guid? VerifyAssetId(Guid assetId);
        Guid[] VerifyAssetIds(IEnumerable<Guid> ids);

        Guid? GetAssetIdByPointer(long pointer);
        Guid[] GetAssetIdsByPointers(IEnumerable<long> pointers);
    }
}