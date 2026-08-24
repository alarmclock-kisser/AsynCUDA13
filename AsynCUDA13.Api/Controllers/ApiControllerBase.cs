using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    /// <summary>
    /// Base controller for all API controllers that automatically logs ProblemDetails responses.
    /// </summary>
    public abstract class ApiControllerBase : ControllerBase
    {
        protected readonly IRuntimeService backend;
        protected readonly ICudaService? cuda;
        protected readonly IOpenClService? opencl;
        protected readonly string RuntimeType = "N/A";

        protected bool IsBackendAvailable => this.backend.TotalAvailableDeviceProperties.Count > 0;

        protected ApiControllerBase(IRuntimeService backend)
        {
            this.backend = backend;
            if (backend is ICudaService cudaService)
            {
                this.backend = cudaService;
                this.RuntimeType = "CUDA";
            }
            else if (backend is IOpenClService openClService)
            {
                this.opencl = openClService;
                this.RuntimeType = "OpenCL";
            }
        }

        /// <summary>
        /// Overrides StatusCode to automatically log ProblemDetails responses.
        /// </summary>
        public override ObjectResult StatusCode(int statusCode, object? value)
        {
            if (value is ProblemDetails pd)
            {
                StaticLogger.Log($"ProblemDetails: Title={pd.Title}, Detail={pd.Detail}, Status={pd.Status}");
            }
            return base.StatusCode(statusCode, value);
        }
    }
}