using AsynCUDA13.Shared.Api.Payloads;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimePushRequest
    {
        public string ElementType => this.Payload.ElementType;
        public required ISimdPayload Payload { get; set; }  // 1D or 2D serialized data

        public bool AsyncCall { get; set; } = true; // async push


    }
}
