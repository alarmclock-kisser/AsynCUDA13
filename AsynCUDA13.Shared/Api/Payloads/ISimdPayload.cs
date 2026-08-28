using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AsynCUDA13.Shared.Api.Payloads
{
    [Newtonsoft.Json.JsonConverter(typeof(NewtonsoftSimdPayloadConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(SystemTextSimdPayloadConverter))]
    public interface ISimdPayload
    {
        string ElementType { get; set; }

        bool AsyncCall { get; set; }


    }
}
