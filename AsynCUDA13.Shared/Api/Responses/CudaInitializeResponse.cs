using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class CudaInitializeResponse
    {
        public CudaContextInfo? ContextInfo { get; set; } = null;

        public bool Success => this.ContextInfo != null && this.ContextInfo.DeviceInfo != null;

        public int ElapsedMs { get; set; } = -1;

    }
}
