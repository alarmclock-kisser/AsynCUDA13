using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Collections;

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
        protected readonly IRollingFileMemoryLogger logger;
        protected readonly string RuntimeType = "N/A";

        protected bool IsBackendAvailable => this.backend.TotalAvailableDeviceProperties.Count > 0;

        protected ApiControllerBase(IRuntimeService backend, IRollingFileMemoryLogger logger)
        {
            this.logger = logger;
            this.backend = backend;
            if (backend is ICudaService cudaService)
            {
                this.backend = cudaService;
                this.cuda = cudaService;
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
                this.logger.Log($"ProblemDetails: Title={pd.Title}, Detail={pd.Detail}, Status={pd.Status}");
            }
            return base.StatusCode(statusCode, value);
        }

        public override OkObjectResult Ok(object? value)
        {
            string actionName = this.ControllerContext?.ActionDescriptor?.ActionName ?? "UnknownAction";
            string controllerName = this.ControllerContext?.ActionDescriptor?.ControllerName ?? "UnknownController";
            string endpointPrefix = $"[{controllerName}::{actionName}]";

            string log = "No return value evaluated yet.";

            try
            {
                log = value switch
                {
                    null => "returned nothing",

                    // Prüfung auf Liste/Array (DTOs)
                    IEnumerable<object> array when array.Any() => $"returned {((ICollection) array).Count} DTOs",
                    IEnumerable<object> => "returned nothing",

                    // Prüfung auf ProblemDetails (wie im Ansatz begonnen)
                    ProblemDetails pd => $"ProblemDetails: Title={pd.Title}, Detail={pd.Detail}, Status={pd.Status}",

                    // Prüfung auf einfache Werte (Primitive, Strings, etc.)
                    string s => string.IsNullOrEmpty(s) ? "returned nothing" : s,
                    var v when v.GetType().IsPrimitive || v is decimal => $"returned <{v.GetType().Name}> '{v.ToString()}'" ?? "returned nothing",

                    // Fallback für einzelne JSON-Objekte / DTOs
                    _ => "returned some DTO"
                };
            }
            catch (Exception ex)
            {
                this.logger.Log(ex);
            }

            this.logger.Log($"{endpointPrefix} {log}");

            return base.Ok(value);
        }
    }
}