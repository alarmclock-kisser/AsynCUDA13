using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class CudaCompileRequest
    {
        public required string KernelName { get; set; }  // FileName und zugleich Funktionsname des Kernels

        public required string KernelSource { get; set; }  // .cu file path or raw kernel code string

        public bool Silent { get; set; } = false;  // Suppress logging during compilation

        public bool AsyncCall { get; set; } = true;  // Async compilation
    }
}
