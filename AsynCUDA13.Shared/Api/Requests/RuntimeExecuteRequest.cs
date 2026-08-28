using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeExecuteRequest
    {
        public RuntimeKernelInfo? KernelInfo { get; set; } = null;

        public IEnumerable<string> ArgumentValues { get; set; } = [];   // Arg values ToString() and in correct order

        public bool AsyncCall { get; set; } = true; // Async execution

        public bool UnloadAfterExecution { get; set; } = false; // Unload kernel after execution
    }
}