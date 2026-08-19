using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.CudaDtos
{
    public class CudaContextInfo
    {
        public CudaDeviceInfo? DeviceInfo { get; set; } = null; // Null if no device info is selected (CUDA offline)


        public CudaUsageInfo? UsageInfo { get; set; } = null;   // Null if CUDA is not initialized
        public CudaMemInfo[]? MemoryInfos { get; set; } = null; // Null if CUDA is not initialized
        public CudaKernelInfo[]? KernelInfos { get; set; } = null;  // Null if CUDA is not initialized


        public bool Online => this.DeviceInfo != null && this.DeviceInfo.DeviceId >= 0;

    }
}
