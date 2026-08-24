using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public interface ISimdPayload
    {
        string ElementType { get; set; }

        bool AsyncCall { get; set; }


    }
}
