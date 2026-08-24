using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RuntimeContextController : ApiControllerBase
    {
        public RuntimeContextController(IRuntimeService backend)
            : base(backend)
        {
            
        }


        [HttpGet("backend")]
        public ActionResult<string> GetBackend()
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = "Runtime not available",
                    Detail = "The runtime is not available. Please ensure that the runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                return this.Ok(this.RuntimeType);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving runtime backend",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("status")]
        public ActionResult<RuntimeContextInfo> GetContextStatus()
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not available",
                    Detail = $"The {this.RuntimeType} runtime is not available. Please ensure that the {this.RuntimeType} runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                var contextInfo = RuntimeInfosBuilder.BuildRuntimeContextInfo(this.backend);
                return this.Ok(contextInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error retrieving {this.RuntimeType} context status",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("initialize")]
        public ActionResult<RuntimeInitializeResponse?> InitializeContext([FromBody] RuntimeInitializeRequest request)
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = "Runtime not available",
                    Detail = "The runtime is not available. Please ensure that the runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                bool success = this.backend.Initialize(request.DeviceId ?? 0);
                if (!success)
                {
                    var pd = new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} context initialization failed",
                        Detail = $"Failed to initialize {this.RuntimeType} context for device ID {request.DeviceId}.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var contextInfo = RuntimeInfosBuilder.BuildRuntimeContextInfo(this.backend);
                var response = RuntimeResponsesBuilder.BuildInitializeResponse(this.backend, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error initializing {this.RuntimeType} context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("initialize/{deviceId}")]
        public ActionResult<RuntimeContextInfo> InitializeContext(int deviceId = 0)
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not available",
                    Detail = $"The {this.RuntimeType} runtime is not available. Please ensure that the {this.RuntimeType} runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            try
            {
                bool success = this.backend.Initialize(deviceId);
                if (!success)
                {
                    var pd = new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} context initialization failed",
                        Detail = $"Failed to initialize {this.RuntimeType} context for device ID {deviceId}.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var contextInfo = RuntimeInfosBuilder.BuildRuntimeContextInfo(this.backend);
                return this.Ok(contextInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error initializing {this.RuntimeType} context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("dispose-context")]
        public ActionResult<RuntimeDisposeResponse?> DisposeContext([FromBody] RuntimeDisposeRequest request)
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not available",
                    Detail = $"The {this.RuntimeType} runtime is not available. Please ensure that the {this.RuntimeType} runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                if (request.FreeAllBuffersBeforeDispose)
                {
                    this.backend.FreeAllMemory();
                }
                this.backend.Dispose();
                if (this.backend.Online)
                {
                    var pd = new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} context disposal failed",
                        Detail = $"Failed to dispose of the {this.RuntimeType} context.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var response = RuntimeResponsesBuilder.BuildDisposeResponse(this.backend, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error disposing {this.RuntimeType} context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("dispose")]
        public ActionResult Dispose()
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not available",
                    Detail = $"The {this.RuntimeType} runtime is not available. Please ensure that the {this.RuntimeType} runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                this.backend.Dispose();
                return this.Ok(new { message = $"{this.RuntimeType} context disposed successfully." });
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error disposing {this.RuntimeType} context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


    }
}
