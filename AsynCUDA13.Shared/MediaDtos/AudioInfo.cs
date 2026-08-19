using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class AudioInfo : IMediaInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

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
