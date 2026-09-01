using AsynCUDA13.Media;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.MediaDtos;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class MediaInfosBuilder
    {
        public static IMediaInfo BuildImageInfo(ImageObj imageObj)
        {
            return new ImageInfo()
            {
                Id = imageObj.Id,
                FilePath = imageObj.FilePath,
                CreatedAt = imageObj.CreatedAt,
                Name = imageObj.Name,
                Pointer = imageObj.Pointer.ToString(),
                Width = imageObj.Width,
                Height = imageObj.Height,
                Channels = imageObj.Channels,
                BitDepth = imageObj.Bitdepth,
                OriginalSizeMb = imageObj.SizeMb,
                Meta = imageObj.Meta
            };
        }

        public static IMediaInfo BuildAudioInfo(AudioObj audioObj)
        {
            return new AudioInfo()
            {
                Id = audioObj.Id,
                FilePath = audioObj.FilePath,
                CreatedAt = audioObj.CreatedAt,
                Name = audioObj.Name,
                Pointer = audioObj.Pointer.ToString(),
                ChunkSize = audioObj.ChunkSize,
                Overlap = audioObj.Overlap,
                Length = audioObj.Length.ToString(),
                SampleRate = audioObj.SampleRate,
                Channels = audioObj.Channels,
                BitDepth = audioObj.BitDepth,
                DurationSeconds = (float) audioObj.Duration.TotalSeconds,
                Bpm = null
            };
        }

    }
}
