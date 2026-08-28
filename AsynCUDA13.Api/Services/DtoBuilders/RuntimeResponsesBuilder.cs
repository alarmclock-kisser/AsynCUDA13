using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class RuntimeResponsesBuilder
    {
        public static RuntimeInitializeResponse BuildInitializeResponse(IRuntimeService service, int elapsedMs = -1)
        {
            return new RuntimeInitializeResponse
            {
                ContextInfo = RuntimeInfosBuilder.BuildRuntimeContextInfo(service),
                ElapsedMs = elapsedMs
            };
        }

        public static RuntimeDisposeResponse BuildDisposeResponse(IRuntimeService service, int elapsedMs = -1)
        {
            return new RuntimeDisposeResponse
            {
                Success = !service.Online,
                FreedMemoryBytes = service.TotalAllocatedBytes.ToString(),
                ElapsedMs = elapsedMs
            };
        }

        public static RuntimePushResponse BuildPushResponse(IRuntimeService service, string indexPointerOrId, int elapsedMs = -1)
        {
            var memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(service, indexPointerOrId);
            return new RuntimePushResponse
            {
                MemoryInfo = memInfo,
                Success = memInfo != null,
                ElapsedMs = elapsedMs
            };
        }

        public static RuntimePullResponse BuildPullResponse(RuntimeMemInfo memoryInfoRef, ISimdPayload? payload, int elapsedMs = -1)
        {
            return new RuntimePullResponse
            {
                MemoryInfoReference = memoryInfoRef,
                Payload = payload,
                ElapsedMs = elapsedMs
            };
        }

        public static RuntimeFourierResponse BuildFourierResponse(RuntimeMemInfo inputMemoryInfoRef, RuntimeMemInfo? outputMemoryInfoRef = null, ISimdPayload? outputPayload = null, int elapsedMs = -1)
        {
            return new RuntimeFourierResponse
            {
                InputMemoryInfoReference = inputMemoryInfoRef,
                OutputMemoryInfoReference = outputMemoryInfoRef,
                OutputPayload = outputPayload,
                ElapsedMs = elapsedMs
            };
        }

        public static RuntimeCompileResponse BuildCompileResponse(RuntimeKernelInfo? kernelInfo, int elapsedMs = -1)
        {
            return new RuntimeCompileResponse
            {
                KernelInfo = kernelInfo,
                ElapsedMs = elapsedMs
            };
        }

        public static RuntimeExecuteResponse BuildExecuteResponse(RuntimeKernelInfo kernelInfo, bool success, IntPtr? resultPtr = null, int elapsedMs = -1)
        {
            return new RuntimeExecuteResponse
            {
                Success = success,
                KernelInfo = kernelInfo,
                ResultPointer = resultPtr.ToString(),
                ElapsedMs = elapsedMs
            };
        }

    }
}
