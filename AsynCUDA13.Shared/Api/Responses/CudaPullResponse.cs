using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class CudaPullResponse
    {
        public required CudaMemInfo MemoryInfoReference { get; set; }   // Get reference DTO before pull or free of buffer(s)


        public ICudaPayload? Payload { get; set; } = null;  // ServerSided or failed if null

        public bool Success { get; set; } = false;


        public int ElapsedMs { get; set; } = -1;    // -1 if failed

    }
}
