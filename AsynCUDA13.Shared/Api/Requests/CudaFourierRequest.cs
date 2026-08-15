using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class CudaFourierRequest
    {
        public required CudaMemInfo MemoryInfo { get; set; }

        public bool? Inverse { get; set; } = null;  // Automatic if null

        public bool AsyncCall { get; set; } = true;

        public bool KeepInputBuffer { get; set; } = false;

        public bool AutoPullResult { get; set; } = false;
    }
}
