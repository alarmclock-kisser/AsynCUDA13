using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.RuntimeDtos
{
    public class RuntimeUsageInfo
    {
        public int ActiveThreads { get; set; } = 0;
        public int IdleThreads { get; set; } = 0;
        public string TotalAllocatedBytes { get; set; } = "0";
        public int TotalAllocations { get; set; } = 0;
    }
}
