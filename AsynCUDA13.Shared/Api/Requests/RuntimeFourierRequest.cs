using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeFourierRequest
    {
        public required RuntimeMemInfo MemoryInfo { get; set; }

        public bool? Inverse { get; set; } = null;  // Automatic if null

        public bool AsyncCall { get; set; } = true;

        public bool KeepInputBuffer { get; set; } = false;

        public bool AutoPullResult { get; set; } = false;
    }
}
