using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class CudaPayload2D : ICudaPayload
    {
        public string ElementType { get; set; } = "float";  // unmanaged types + structs
        public bool AsyncCall { get; set; } = true;

        public IEnumerable<string> DataChunks { get; set; } = [];
    }
}
