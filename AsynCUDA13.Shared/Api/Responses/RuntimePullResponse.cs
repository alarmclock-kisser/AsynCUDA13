using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class RuntimePullResponse
    {
        public RuntimeMemInfo? MemoryInfoReference { get; set; } = null;


        public ISimdPayload? Payload { get; set; } = null;  // ServerSided or failed if null

        public bool Success { get; set; } = false;


        public int ElapsedMs { get; set; } = -1;    // -1 if failed

    }
}
