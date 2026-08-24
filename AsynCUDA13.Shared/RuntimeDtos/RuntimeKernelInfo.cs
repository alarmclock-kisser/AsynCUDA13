using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.RuntimeDtos
{
    public class RuntimeKernelInfo
    {
        public string FunctionName { get; set; } = string.Empty;    // Empty if not compiled yet
        public string SourcePath { get; set; } = string.Empty;  // Empty if not saved as .cu
        public string? PtxPath { get; set; } = null;    // Null if not compiled

        public string KernelCode { get; set; } = string.Empty;  // Empty if .cu file exists + is readable

        public string[] ArgumentNames { get; set; } = [];
        public string[] ArgumentTypes { get; set; } = [];

    }
}
