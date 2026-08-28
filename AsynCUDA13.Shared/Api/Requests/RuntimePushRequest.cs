using AsynCUDA13.Shared.Api.Payloads;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimePushRequest
    {
        public Guid? AssetId { get; set; } = null;
        public bool KeepData { get; set; } = false;
        public string _elementType { get; init; } = "void";
        public string ElementType => this.Payload?.ElementType ?? this._elementType;
        public ISimdPayload? Payload { get; set; } = null;  // 1D or 2D serialized data

        public string IndexLength { get; set; } = "0";
        public int Stride { get; set; } = 1; // if >= 2 it is a 2D array, otherwise 1D

        public float Overlap { get; set; } = 0.5f; // overlap for 2D arrays, default is 0.5

        public bool AsyncCall { get; set; } = true; // async push


    }
}
