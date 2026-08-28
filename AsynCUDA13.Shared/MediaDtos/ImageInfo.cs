using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class ImageInfo
    {
        public ImageInfo()
        {
        }

        public Guid Id { get; set; }

        public DateTime CreatedAt { get; set; }
        public string Name { get; set; } = string.Empty;

        public string MediaType { get; set; } = "image";

        public int Width { get; set; }
        public int Height { get; set; }
        public int Channels { get; set; } = 4;
        public int BitDepth { get; set; } = 32;
        public int BitsPerChannel => this.BitDepth / this.Channels;

        public float OriginalSizeMb { get; set; }


        public string? Pointer { get; set; } = null;

        public bool OnGpu => !string.IsNullOrEmpty(this.Pointer) && !this.Pointer.Equals("null", StringComparison.OrdinalIgnoreCase) && !this.Pointer.Equals(IntPtr.Zero.ToString(), StringComparison.OrdinalIgnoreCase);
    
    
        public bool IdMatch(string id, bool requireOnGpu = false)
        {
            return this.Id.ToString().Equals(id, StringComparison.OrdinalIgnoreCase) && (requireOnGpu ? this.OnGpu : true);
        }

        public bool IdMatch(Guid id, bool requireOnGpu = false)
        {
            return this.Id.Equals(id) && (requireOnGpu ? this.OnGpu : true);
        }
    }
}
