using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.Shared.MediaDtos
{
    public class AudioData : IMediaData
    {
        public required IMediaInfo Info { get; set; }

        public string? Pointer => this.Info?.Pointer;

        public Single[] AudioDataFloats { get; set; } = [];
        public Single[][] AudioDataFloatChunks { get; set; } = [];

        public int ChunkSize => this.AudioDataFloatChunks.Length != 0 ? this.AudioDataFloatChunks.FirstOrDefault()?.Length ?? 0 : 0;
        public Single DataSizeMb => this.AudioDataFloats.Length != 0 ? this.AudioDataFloats.LongCount() * sizeof(Single) / 1024f / 1024f : this.AudioDataFloatChunks.Length != 0 ? this.AudioDataFloatChunks.Sum(chunk => chunk.Length) * sizeof(Single) / 1024f / 1024f : 0f;

        public string MimeType { get; set; } = "audio/wav";
        public string Base64Data { get; set; } = string.Empty;
        public bool OnGpu => !string.IsNullOrEmpty(this.Pointer) && !this.Pointer.Equals("null", StringComparison.OrdinalIgnoreCase) && !this.Pointer.Equals(IntPtr.Zero.ToString(), StringComparison.OrdinalIgnoreCase);

        public Boolean IdMatch(string id, Boolean requireOnGpu = false)
        {
            return this.Info.IdMatch(id, requireOnGpu);
        }
    }
}
