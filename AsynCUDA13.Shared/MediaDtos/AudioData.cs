using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class AudioData
    {
        public required AudioInfo Info { get; set; }

        public string? Pointer => this.Info?.Pointer;

        public Single[] AudioDataFloats { get; set; } = [];
        public Single[][] AudioDataFloatChunks { get; set; } = [];

        public Int32 ChunkSize => this.AudioDataFloatChunks.Length != 0 ? this.AudioDataFloatChunks.FirstOrDefault()?.Length ?? 0 : 0;
        public Single DataSizeMb => this.AudioDataFloats.Length != 0 ? this.AudioDataFloats.LongCount() * sizeof(Single) / 1024f / 1024f : this.AudioDataFloatChunks.Length != 0 ? this.AudioDataFloatChunks.Sum(chunk => chunk.Length) * sizeof(Single) / 1024f / 1024f : 0f;


        public Boolean IdMatch(string id, Boolean requireOnGpu = false)
        {
            return this.Info.IdMatch(id, requireOnGpu);
        }
    }
}
