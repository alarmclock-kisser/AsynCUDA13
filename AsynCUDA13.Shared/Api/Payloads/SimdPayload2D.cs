using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class SimdPayload2D : ISimdPayload
    {
        public string ElementType { get; set; } = "float";  // unmanaged types + structs
        public bool AsyncCall { get; set; } = true;

        public IEnumerable<string> DataChunks { get; set; } = [];
    }
}
