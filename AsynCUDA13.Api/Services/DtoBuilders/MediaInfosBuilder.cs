using AsynCUDA13.Media;
using AsynCUDA13.Shared.MediaDtos;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class MediaInfosBuilder
    {
        public static ImageInfo BuildImageInfo(ImageObj imageObj)
        {
            return new ImageInfo()
            {
                Id = imageObj.Id,
                Name = imageObj.Name,
                Width = imageObj.Width,
                Height = imageObj.Height,
                Channels = imageObj.Channels,
                OriginalSizeMb = imageObj.SizeMb
            };
        }

        public static AudioInfo BuildAudioInfo(AudioObj audioObj)
        {
            return new AudioInfo()
            {
                Id = audioObj.Id,
                Name = audioObj.Name,
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
