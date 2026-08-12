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
                Pointer = imageObj.Pointer.ToString(),
                MimeType = $"image/{format.ToLower()}",
                Base64Data = imageObj.Base64Image(format, keepData)
            };
        }

        public static AudioData BuildAudioData(AudioObj audioObj, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            return new AudioData()
            {
                Info = MediaInfosBuilder.BuildAudioInfo(audioObj),
                Pointer = audioObj.Pointer.ToString(),
                AudioDataFloats = chunkSize <= 0 ? audioObj.Data : [],
                AudioDataFloatChunks = chunkSize > 0 ? audioObj.GetChunks(chunkSize, overlap, keepData) : [],
            };
        }


    }
}
