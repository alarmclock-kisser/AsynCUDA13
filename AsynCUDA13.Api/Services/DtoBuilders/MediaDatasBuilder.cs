using AsynCUDA13.Media;
using AsynCUDA13.Shared.MediaDtos;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class MediaDatasBuilder
    {
        public static ImageData BuildImageData(ImageObj imageObj, string format = "bmp", bool keepData = true)
        {
            return new ImageData()
            {
                Info = MediaInfosBuilder.BuildImageInfo(imageObj),
                MimeType = $"image/{format.ToLower()}",
                Base64Data = imageObj.Base64Image(format, keepData)
            };
        }

        public static AudioData BuildAudioData(AudioObj audioObj, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            return new AudioData()
            {
                Info = MediaInfosBuilder.BuildAudioInfo(audioObj),
                AudioDataFloats = chunkSize <= 0 ? audioObj.Data : [],
                AudioDataFloatChunks = chunkSize > 0 ? audioObj.GetChunks(chunkSize, overlap, keepData) : [],
            };
        }

        public static ImageData BuildImagePreview(ImageObj image, int maxDimenions, string format = "jpg")
        {
            format = format.ToLower() switch
            {
                "jpg" or "jpeg" => "jpg",
                "png" => "png",
                "bmp" => "bmp",
                _ => "png"
            };

            return new ImageData()
            {
                Info = MediaInfosBuilder.BuildImageInfo(image),
                MimeType = $"image/{format.ToLower()}",
                Base64Data = image.GetPreview(maxDimenions, format)
            };
        }
        public static ImageData BuildAudioPreview(AudioObj audio, int width, int height, string format = "jpg")
        {
            format = format.ToLower() switch
            {
                "jpg" or "jpeg" => "jpg",
                "png" => "png",
                "bmp" => "bmp",
                _ => "png"
            };

            ImageObj waveform = audio.GenerateWaveform(width, height);

            return new ImageData()
            {
                Info = MediaInfosBuilder.BuildImageInfo(waveform),
                MimeType = $"image/{format.ToLower()}",
                Base64Data = waveform.GetPreview(Math.Max(width, height), format)
            };
        }
    }
}
