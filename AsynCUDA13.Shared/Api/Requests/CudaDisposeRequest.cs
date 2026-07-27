using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class CudaDisposeRequest
    {
        public bool FreeAllBuffersBeforeDispose { get; set; } = true;



    }
}
