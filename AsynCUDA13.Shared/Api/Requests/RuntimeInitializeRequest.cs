using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeInitializeRequest
    {
        public int? DeviceId { get; set; } = null;  // FirstOrDefault device if null
        public string? DeviceName { get; set; } = null; // FirstOrDefault device if null

        public bool ForceReinitialize { get; set; } = false;
    }
}
