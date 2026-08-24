using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.RuntimeDtos
{
    public class RuntimeContextInfo
    {
        public string RuntimeType { get; set; } = "N/A"; // "CUDA" or "OpenCL"

        public RuntimeDeviceInfo? DeviceInfo { get; set; } = null; // Null if no device info is selected (CUDA offline)


        public RuntimeUsageInfo? UsageInfo { get; set; } = null;   // Null if CUDA is not initialized
        public RuntimeMemInfo[]? MemoryInfos { get; set; } = null; // Null if CUDA is not initialized
        public RuntimeKernelInfo[]? KernelInfos { get; set; } = null;  // Null if CUDA is not initialized


        public bool Online => this.DeviceInfo != null && this.DeviceInfo.DeviceId >= 0;

    }
}
