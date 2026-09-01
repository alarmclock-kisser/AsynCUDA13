using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Utils;

namespace AsynCUDA13.Tests;

internal static class HardwareTestGuard
{
    internal static CudaService CreateCudaService(IRollingFileMemoryLogger logger)
    {
        if (!CudaAvailabilityTester.IsCudaAvailable())
        {
            Assert.Inconclusive("CUDA 13 runtime is not available on this machine.");
        }

        try
        {
            if (CudaService.DeviceCount <= 0)
            {
                Assert.Inconclusive("No CUDA-capable device is available.");
            }

            var service = new CudaService(logger);
            if (!service.Initialize(0))
            {
                service.Dispose();
                Assert.Inconclusive("CUDA device 0 could not be initialized.");
            }

            return service;
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            Assert.Inconclusive($"CUDA 13 hardware setup is unavailable: {ex.Message}");
            throw;
        }
    }

    internal static OpenClService CreateOpenClService(IRollingFileMemoryLogger logger)
    {
        try
        {
            var service = new OpenClService(logger);
            if (service.DeviceCount <= 0)
            {
                service.Dispose();
                Assert.Inconclusive("No OpenCL device or ICD is available.");
            }

            if (!service.Initialize(0))
            {
                service.Dispose();
                Assert.Inconclusive("OpenCL device 0 could not be initialized.");
            }

            return service;
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            Assert.Inconclusive($"OpenCL hardware setup is unavailable: {ex.Message}");
            throw;
        }
    }

    private static bool IsMissingRuntime(Exception ex)
        => ex is DllNotFoundException
            or BadImageFormatException
            or TypeInitializationException
            or InvalidOperationException;
}
