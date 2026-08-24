using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class RuntimeInitializeResponse
    {
        public RuntimeContextInfo? ContextInfo { get; set; } = null;

        public Boolean Success => this.ContextInfo != null && this.ContextInfo.DeviceInfo != null;

        public Int32 ElapsedMs { get; set; } = -1;

    }
}
