using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class RuntimePushResponse
    {
        public RuntimeMemInfo? MemoryInfo { get; set; } = null;    // Failed if null

        public bool Success { get; set; } = false;

        public int ElapsedMs { get; set; } = -1;


    }
}
