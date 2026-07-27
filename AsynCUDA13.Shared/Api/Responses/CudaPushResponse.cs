using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class CudaPushResponse
    {
        public CudaMemInfo? MemoryInfo { get; set; } = null;    // Failed if null

        public int ElapsedMs { get; set; } = -1;


    }
}
