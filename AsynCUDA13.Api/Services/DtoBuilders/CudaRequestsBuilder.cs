using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class CudaRequestsBuilder
    {
        public static CudaInitializeRequest BuildCudaInitializeRequest(int? deviceId, string? deviceName = null, bool forceReinitialize = false)
        {
            return new CudaInitializeRequest
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                ForceReinitialize = forceReinitialize
            };
        }

        public static CudaDisposeRequest BuildCudaDisposeRequest(bool freeBeforeDispose = true)
        {
            return new CudaDisposeRequest
            {
                FreeAllBuffersBeforeDispose = freeBeforeDispose
            };
        }

        public static CudaPushRequest BuildCudaPushRequest(string payload, string elementType, bool asyncCall = true)
        {
            return new CudaPushRequest
            {
                AsyncCall = asyncCall,
                ElementType = elementType,
                Payload = new CudaPayload1D
                {
                    Data = payload
                }
            };
        }

        public static CudaPushRequest BuildCudaPushRequest(IEnumerable<string> payload, string elementType, bool asyncCall = true)
        {
            return new CudaPushRequest
            {
                AsyncCall = asyncCall,
                ElementType = elementType,
                Payload = new CudaPayload2D
                {
                    DataChunks = payload
                }
            };
        }

        public static CudaPullRequest BuildCudaPullRequest(string indexPointerOrId, bool freeAfterPull = true, bool asyncCall = true)
        {
            return new CudaPullRequest
            {
                IndexPointerOrId = indexPointerOrId,
                FreeAfterPull = freeAfterPull,
                AsyncCall = asyncCall
            };
        }

        public static CudaFourierRequest BuildCudaFourierRequest(CudaMemInfo memoryInfo, bool? inverse = null, bool asyncCall = true)
        {
            return new CudaFourierRequest
            {
                MemoryInfo = memoryInfo,
                Inverse = inverse,
                AsyncCall = asyncCall
            };
        }

        public static CudaCompileRequest BuildCudaCompileRequest(string kernelName, string kernelSource, bool silent = false, bool asyncCall = true)
        {
            return new CudaCompileRequest
            {
                KernelSource = kernelSource,
                KernelName = kernelName,
                Silent = silent,
                AsyncCall = asyncCall
            };
        }

        public static CudaExecuteRequest BuildCudaExecuteRequest(CudaKernelInfo kernelInfo, IEnumerable<string>? argumentValues = null, bool asyncCall = true)
        {
            return new CudaExecuteRequest
            {
                KernelInfo = kernelInfo,
                ArgumentValues = argumentValues ?? [],
                AsyncCall = asyncCall
            };
        }



    }
}
