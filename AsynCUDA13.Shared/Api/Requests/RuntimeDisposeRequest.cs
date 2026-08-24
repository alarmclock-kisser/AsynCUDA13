using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimeDisposeRequest
    {
        public bool FreeAllBuffersBeforeDispose { get; set; } = true;



    }
}
