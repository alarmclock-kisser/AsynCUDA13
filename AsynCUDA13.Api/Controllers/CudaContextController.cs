using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CudaContextController : ApiControllerBase
    {
        private readonly ICudaService cuda;


        public CudaContextController(ICudaService cuda)
        {
            this.cuda = cuda;
        }


        [HttpGet("status")]
        public ActionResult<CudaContextInfo> GetContextStatus()
        {
            if (!this.cuda.IsCudaAvailable())
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA not available",
                    Detail = "The CUDA runtime is not available. Please ensure that the CUDA runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                var contextInfo = CudaInfosBuilder.BuildCudaContextInfo(this.cuda);
                return this.Ok(contextInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving CUDA context status",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("initialize")]
        public ActionResult<CudaInitializeResponse?> InitializeContext([FromBody] CudaInitializeRequest request)
        {
            if (!this.cuda.IsCudaAvailable())
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA not available",
                    Detail = "The CUDA runtime is not available. Please ensure that the CUDA runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                bool success = this.cuda.Initialize(request.DeviceId ?? 0);
                if (!success)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA context initialization failed",
                        Detail = $"Failed to initialize CUDA context for device ID {request.DeviceId}.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var contextInfo = CudaInfosBuilder.BuildCudaContextInfo(this.cuda);
                var response = CudaResponsesBuilder.BuildInitializeResponse(this.cuda, (int)(DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error initializing CUDA context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("initialize/{deviceId}")]
        public ActionResult<CudaContextInfo> InitializeContext(int deviceId = 0)
        {
            if (!this.cuda.IsCudaAvailable())
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA not available",
                    Detail = "The CUDA runtime is not available. Please ensure that the CUDA runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            try
            {
                bool success = this.cuda.Initialize(deviceId);
                if (!success)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA context initialization failed",
                        Detail = $"Failed to initialize CUDA context for device ID {deviceId}.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var contextInfo = CudaInfosBuilder.BuildCudaContextInfo(this.cuda);
                return this.Ok(contextInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error initializing CUDA context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("dispose-context")]
        public ActionResult<CudaDisposeResponse?> DisposeContext([FromBody] CudaDisposeRequest request)
        {
            if (!this.cuda.IsCudaAvailable())
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA not available",
                    Detail = "The CUDA runtime is not available. Please ensure that the CUDA runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                if (request.FreeAllBuffersBeforeDispose)
                {
                    this.cuda.FreeAllMemory();
                }
                this.cuda.Dispose();
                if (this.cuda.Online)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA context disposal failed",
                        Detail = "Failed to dispose of the CUDA context.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var response = CudaResponsesBuilder.BuildDisposeResponse(this.cuda, (int)(DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error disposing CUDA context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("dispose")]
        public ActionResult Dispose()
        {
            if (!this.cuda.IsCudaAvailable())
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA not available",
                    Detail = "The CUDA runtime is not available. Please ensure that the CUDA runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                this.cuda.Dispose();
                return this.Ok(new { message = "CUDA context disposed successfully." });
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error disposing CUDA context",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


    }
}
