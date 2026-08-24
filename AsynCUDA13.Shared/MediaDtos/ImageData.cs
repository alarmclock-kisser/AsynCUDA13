using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class ImageData
    {
        public required ImageInfo Info { get; set; }

        public string? Pointer => this.Info.Pointer;

        public string MimeType { get; set; } = "image/png";
        public string Base64Data { get; set; } = string.Empty;
        public string Base64Image => $"data:{this.MimeType};base64,{this.Base64Data}";

        public float DataSizeMb => this.Base64Data.LongCount() * 4f / 3f / 1024f / 1024f;

        public bool IdMatch(string id, bool requireOnGpu = false)
        {
            return this.Info.IdMatch(id, requireOnGpu);
        }
    }
}
