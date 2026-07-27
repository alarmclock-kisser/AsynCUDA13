using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class CudaDisposeResponse
    {
        public bool Success { get; set; } = false;
        public string? FreedMemoryBytes { get; set; } = null;   // Null if none were freed before dispose

        public int ElapsedMs { get; set; } = -1;


    }
}
