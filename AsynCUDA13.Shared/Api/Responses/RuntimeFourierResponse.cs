using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class RuntimeFourierResponse
    {
        public required RuntimeMemInfo InputMemoryInfoReference { get; set; }  // Get input mem reference DTO before transformation or free of buffer(s)
        public RuntimeMemInfo? OutputMemoryInfoReference { get; set; } = null; // Failed if null


        public string? IndexPointer { get; set; } = null;   // Null if not on device (pulled, on host)
        public ISimdPayload? OutputPayload { get; set; } = null;    // Buffer still on device (not pulled) if null



        public int ElapsedMs { get; set; } = -1;    // -1 if failed

    }
}
