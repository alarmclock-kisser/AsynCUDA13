using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class AudioInfo
    {
        public AudioInfo()
        {
        }

        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Name { get; set; } = string.Empty;

        public string MediaType { get; set; } = "audio";

        public Int32 SampleRate { get; set; }
        public Int32 Channels { get; set; }
        public Int32 BitDepth { get; set; }

        public string Length { get; set; } = "0";
        public Single DurationSeconds { get; set; }

        public Single? Bpm { get; set; } = null;


        public string? Pointer { get; set; } = null;

        public Boolean OnGpu => !string.IsNullOrEmpty(this.Pointer) && !this.Pointer.Equals("null", StringComparison.OrdinalIgnoreCase) && !this.Pointer.Equals(IntPtr.Zero.ToString(), StringComparison.OrdinalIgnoreCase);

        public Boolean IdMatch(string id, Boolean requireOnGpu = false)
        {
            return this.Id.ToString().Equals(id, StringComparison.OrdinalIgnoreCase) && (requireOnGpu ? this.OnGpu : true);
        }

        public Boolean IdMatch(Guid id, Boolean requireOnGpu = false)
        {
            return this.Id.Equals(id) && (requireOnGpu ? this.OnGpu : true);
        }

    }
}
