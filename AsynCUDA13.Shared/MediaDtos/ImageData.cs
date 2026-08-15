using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class ImageData : IMediaData
    {
        public required ImageInfo Info { get; set; }

        public string? Pointer { get; set; } = null;

        public string MimeType { get; set; } = "image/png";
        public string Base64Data { get; set; } = string.Empty;

        public float DataSizeMb => this.Base64Data.LongCount() * 4f / 3f / 1024f / 1024f;


    }
}
