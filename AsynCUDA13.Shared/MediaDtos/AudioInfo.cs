using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class AudioInfo
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

    }
}
