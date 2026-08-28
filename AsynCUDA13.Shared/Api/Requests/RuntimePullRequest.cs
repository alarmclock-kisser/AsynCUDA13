using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Api.Requests
{
    public class RuntimePullRequest
    {
        public string IndexPointerOrId { get; set; } = string.Empty;    // Guid Id or IntPtr IndexPointer from CudaMem-obj 
        public bool AsyncCall { get; set; } = true; // async pull
        public bool FreeAfterPull { get; set; } = false;  // free buffer after pull

        public bool EnsureReferencedAssetsUpdatedOrCreated { get; set; } = true;

    }
}
