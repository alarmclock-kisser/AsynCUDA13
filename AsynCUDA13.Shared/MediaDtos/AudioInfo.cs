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
        public string Name { get; set; } = string.Empty;

        public string MediaType { get; set; } = "audio";

        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitDepth { get; set; }

        public string Length { get; set; } = "0";
        public float DurationSeconds { get; set; }

        public float? Bpm { get; set; } = null;


        public string? Pointer { get; set; } = null;

        public bool OnGpu => !string.IsNullOrEmpty(this.Pointer) && !this.Pointer.Equals("null", StringComparison.OrdinalIgnoreCase) && !this.Pointer.Equals(IntPtr.Zero.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
