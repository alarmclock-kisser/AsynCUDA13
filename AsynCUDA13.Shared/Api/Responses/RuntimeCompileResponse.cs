using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class RuntimeCompileResponse
    {
        public RuntimeKernelInfo? KernelInfo { get; set; } = null;  // Null if compilation failed (PtxPath ist in KernelInfo enthalten)

        public string? BuildLog { get; set; } = null;

        public Int32 ElapsedMs { get; set; } = -1;  // -1 if failed
    }
}