using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class ImageInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public int Width { get; set; }
        public int Height { get; set; }
        public int Channels { get; set; } = 4;

        public float OriginalSizeMb { get; set; }


        public string? Pointer { get; set; } = null;
    }
}
