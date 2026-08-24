using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.RuntimeDtos
{
    public class RuntimeDeviceInfo
    {
        public int? DeviceId { get; set; } = null;  // Null if not available
        public string DeviceName { get; set; } = "N/A"; // N/A if not available

        public Dictionary<string, string>? Properties { get; set; } = null; // PropertyName keys to PropertyValues, null if not available

    }
}
