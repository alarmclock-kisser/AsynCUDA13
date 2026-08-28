using AsynCUDA13.Shared.Api.Payloads;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimePushRequest
    {
        public Guid? AssetId { get; set; } = null;
        public string _elementType { get; init; } = "void";
        public string ElementType => this.Payload?.ElementType ?? this._elementType;
        public ISimdPayload? Payload { get; set; } = null;  // 1D or 2D serialized data

        public bool AsyncCall { get; set; } = true; // async push


    }
}
