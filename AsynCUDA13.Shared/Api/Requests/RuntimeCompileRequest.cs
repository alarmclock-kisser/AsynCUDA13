using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeCompileRequest
    {
        public string? KernelName { get; set; } = null;  // FileName und zugleich Funktionsname des Kernels

        public string KernelSource { get; set; } = string.Empty;

        public bool Silent { get; set; } = false;  // Suppress logging during compilation

        public bool AsyncCall { get; set; } = true;  // Async compilation
    }
}
