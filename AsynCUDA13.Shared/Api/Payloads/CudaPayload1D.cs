using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class CudaPayload1D : ICudaPayload
    {
        public string ElementType { get; set; } = "float";  // unmanaged types + structs
        public bool AsyncCall { get; set; } = true;

        public string Data { get; set; } = string.Empty;
    }
}
