using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class RuntimeRequestsBuilder
    {
        public static RuntimeInitializeRequest BuildCudaInitializeRequest(int? deviceId, string? deviceName = null, bool forceReinitialize = false)
        {
            return new RuntimeInitializeRequest
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                ForceReinitialize = forceReinitialize
            };
        }

        public static RuntimeDisposeRequest BuildCudaDisposeRequest(bool freeBeforeDispose = true)
        {
            return new RuntimeDisposeRequest
            {
                FreeAllBuffersBeforeDispose = freeBeforeDispose
            };
        }

        public static RuntimePushRequest BuildCudaPushRequest(string payload, string elementType, bool asyncCall = true)
        {
            return new RuntimePushRequest
            {
                AsyncCall = asyncCall,
                Payload = new SimdPayload1D
                {
                    Data = payload
                }
            };
        }

        public static RuntimePushRequest BuildCudaPushRequest(IEnumerable<string> payload, string elementType, bool asyncCall = true)
        {
            return new RuntimePushRequest
            {
                AsyncCall = asyncCall,
                Payload = new SimdPayload2D
                {
                    DataChunks = payload
                }
            };
        }

        public static RuntimePullRequest BuildCudaPullRequest(string indexPointerOrId, bool freeAfterPull = true, bool asyncCall = true)
        {
            return new RuntimePullRequest
            {
                IndexPointerOrId = indexPointerOrId,
                FreeAfterPull = freeAfterPull,
                AsyncCall = asyncCall
            };
        }

        public static RuntimeFourierRequest BuildCudaFourierRequest(RuntimeMemInfo memoryInfo, bool? inverse = null, bool asyncCall = true)
        {
            return new RuntimeFourierRequest
            {
                MemoryInfo = memoryInfo,
                Inverse = inverse,
                AsyncCall = asyncCall
            };
        }

        public static RuntimeCompileRequest BuildCudaCompileRequest(string kernelName, string kernelSource, bool silent = false, bool asyncCall = true)
        {
            return new RuntimeCompileRequest
            {
                KernelSource = kernelSource,
                KernelName = kernelName,
                Silent = silent,
                AsyncCall = asyncCall
            };
        }

        public static RuntimeExecuteRequest BuildCudaExecuteRequest(RuntimeKernelInfo kernelInfo, IEnumerable<string>? argumentValues = null, bool asyncCall = true)
        {
            return new RuntimeExecuteRequest
            {
                KernelInfo = kernelInfo,
                ArgumentValues = argumentValues?.ToArray() ?? [],
                AsyncCall = asyncCall
            };
        }



    }
}
