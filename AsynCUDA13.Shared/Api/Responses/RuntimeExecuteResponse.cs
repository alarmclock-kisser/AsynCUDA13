using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Responses
{
    public class RuntimeExecuteResponse
    {
        public string[]? ResultPointers { get; set; } = null;  // Optional pointer to the result data in device memory

        public bool Success { get; set; } = false;  // True if execution succeeded

        public RuntimeKernelInfo? KernelInfo { get; set; } = null;

        public int ElapsedMs { get; set; } = -1;  // -1 if failed


        public string[] GetAssetPointers(RuntimeExecuteRequest request)
        {
            return request.PointerArguments?.Where(a => !this.ResultPointers?.Contains(a) == true).ToArray() ?? [];
        }
    }
}