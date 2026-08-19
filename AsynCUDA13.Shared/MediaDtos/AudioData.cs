using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class AudioData
    {
        public required AudioInfo Info { get; set; }

        public string? Pointer { get; set; } = null;

        public float[] AudioDataFloats { get; set; } = [];
        public float[][] AudioDataFloatChunks { get; set; } = [];

        public int ChunkSize => this.AudioDataFloatChunks.Any() ? this.AudioDataFloatChunks.FirstOrDefault()?.Length ?? 0 : 0;
        public float DataSizeMb => this.AudioDataFloats.Any() ? this.AudioDataFloats.LongCount() * sizeof(float) / 1024f / 1024f : this.AudioDataFloatChunks.Any() ? this.AudioDataFloatChunks.Sum(chunk => chunk.Length) * sizeof(float) / 1024f / 1024f : 0f;

    }
}
