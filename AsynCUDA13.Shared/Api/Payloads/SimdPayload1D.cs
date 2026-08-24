using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public class SimdPayload1D : ISimdPayload
    {
        public string ElementType { get; set; } = "float";  // unmanaged types + structs
        public Boolean AsyncCall { get; set; } = true;

        public string Data { get; set; } = string.Empty;
    }
}
