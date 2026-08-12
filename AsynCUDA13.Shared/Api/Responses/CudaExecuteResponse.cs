using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class CudaExecuteResponse
    {
        public IntPtr? ResultPointer { get; set; } = null;  // Optional pointer to the result data in device memory

        public bool Success { get; set; } = false;  // True if execution succeeded

        public required CudaKernelInfo? KernelInfo { get; set; }

        public int ElapsedMs { get; set; } = -1;  // -1 if failed
    }
}