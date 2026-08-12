using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class CudaResponsesBuilder
    {
        public static CudaInitializeResponse BuildInitializeResponse(ICudaService service, int elapsedMs = -1)
        {
            return new CudaInitializeResponse
            {
                ContextInfo = CudaInfosBuilder.BuildCudaContextInfo(service),
                ElapsedMs = elapsedMs
            };
        }

        public static CudaDisposeResponse BuildDisposeResponse(ICudaService service, int elapsedMs = -1)
        {
            return new CudaDisposeResponse
            {
                FreedMemoryBytes = service.TotalAllocated.ToString(),
                ElapsedMs = elapsedMs
            };
        }

        public static CudaPushResponse BuildPushResponse(ICudaService service, string indexPointerOrId, int elapsedMs = -1)
        {
            return new CudaPushResponse
            {
                MemoryInfo = CudaInfosBuilder.BuildCudaMemoryInfos(service, indexPointerOrId).FirstOrDefault(),
                ElapsedMs = elapsedMs
            };
        }

        public static CudaPullResponse BuildCudaPullResponse(CudaMemInfo memoryInfoRef, ICudaPayload? payload, int elapsedMs = -1)
        {
            return new CudaPullResponse
            {
                MemoryInfoReference = memoryInfoRef,
                Payload = payload,
                ElapsedMs = elapsedMs
            };
        }

        public static CudaFourierResponse BuildCudaFourierResponse(CudaMemInfo inputMemoryInfoRef, CudaMemInfo? outputMemoryInfoRef = null, ICudaPayload? outputPayload = null, int elapsedMs = -1)
        {
            return new CudaFourierResponse
            {
                InputMemoryInfoReference = inputMemoryInfoRef,
                OutputMemoryInfoReference = outputMemoryInfoRef,
                OutputPayload = outputPayload,
                ElapsedMs = elapsedMs
            };
        }

        public static CudaCompileResponse BuildCudaCompileResponse(CudaKernelInfo? kernelInfo, int elapsedMs = -1)
        {
            return new CudaCompileResponse
            {
                KernelInfo = kernelInfo,
                ElapsedMs = elapsedMs
            };
        }

        public static CudaExecuteResponse BuildCudaExecuteResponse(CudaKernelInfo kernelInfo, bool success, nint? resultPtr = null, int elapsedMs = -1)
        {
            return new CudaExecuteResponse
            {
                Success = success,
                KernelInfo = kernelInfo,
                ResultPointer = resultPtr,
                ElapsedMs = elapsedMs
            };
        }

    }
}
