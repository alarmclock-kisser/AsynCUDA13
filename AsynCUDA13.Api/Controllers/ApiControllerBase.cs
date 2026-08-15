using AsynCUDA13.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    /// <summary>
    /// Base controller for all API controllers that automatically logs ProblemDetails responses.
    /// </summary>
    public abstract class ApiControllerBase : ControllerBase
    {
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