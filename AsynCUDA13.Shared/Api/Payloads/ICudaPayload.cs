using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Payloads
{
    public interface ICudaPayload
    {
        string ElementType { get; set; }

        bool AsyncCall { get; set; }


    }
}
