using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    public class CudaKernelController : ApiControllerBase
    {
        private readonly ICudaService cuda;

        public CudaKernelController(ICudaService cuda)
        {
            this.cuda = cuda;
        }

        [HttpGet("kernels")]
        public ActionResult<IEnumerable<CudaKernelInfo>?> GetKernels(bool filterCompiled = true)
        {
            try
            {
                var infos = CudaInfosBuilder.BuildCudaKernelInfos(this.cuda, filterCompiled: filterCompiled);
                if (infos.Length <= 0)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "No kernels found",
                        Detail = "No CUDA kernels were found in the current context.",
                        Status = 404
                    };
                    return this.NotFound(pd);
                }

                return this.Ok(infos);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving kernels",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("compile")]
        public async Task<ActionResult<CudaCompileResponse>?> CompileKernelAsync([FromBody] CudaCompileRequest request)
        {
            if (!this.cuda.Online || this.cuda.Compiler == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA service offline",
                    Detail = "The CUDA service is currently offline. Please ensure that the CUDA runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                string? ptxPath = request.AsyncCall
                    ? await Task.Run(() => this.cuda.Compiler.CompileKernel(request.KernelName, request.Silent))
                    : this.cuda.Compiler.CompileKernel(request.KernelName, request.Silent);
                if (string.IsNullOrEmpty(ptxPath))
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Compilation failed",
                        Detail = $"The kernel '{request.KernelName}' could not be compiled.",
                        Status = 400
                    };
                    return this.BadRequest(pd);
                }

                var info = CudaInfosBuilder.BuildCudaKernelInfo(this.cuda, request.KernelName);
                var response = CudaResponsesBuilder.BuildCudaCompileResponse(info, (int) (DateTime.Now - started).TotalMilliseconds);
                if (response == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Compilation response error",
                        Detail = $"The compilation response for kernel '{request.KernelName}' could not be built.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error compiling kernel",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("execute-generic")]
        public async Task<ActionResult<CudaExecuteResponse>?> ExecuteGenericKernelAsync([FromBody] CudaExecuteRequest request)
        {
            if (!this.cuda.Online || this.cuda.Compiler == null || this.cuda.Launcher == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA service offline",
                    Detail = "The CUDA service is currently offline. Please ensure that the CUDA runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                object[] args = DataParser.ParseArgumentValues(request.ArgumentValues, request.KernelInfo);

                this.cuda.SetCurrent();
                var result = await this.cuda.Launcher.ExecuteGenericKernelAsync(request.KernelInfo.FunctionName, args);

                if (request.UnloadAfterExecution)
                {
                    this.cuda.Compiler.UnloadKernel();
                    if (!string.IsNullOrEmpty(this.cuda.Compiler.KernelName))
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Kernel unload error",
                            Detail = $"The kernel '{this.cuda.Compiler.KernelName}' could not be unloaded after execution.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }
                }

                var response = CudaResponsesBuilder.BuildCudaExecuteResponse(request.KernelInfo, result.HasValue, result ?? -1);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error executing kernel",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("execute-linear")]
        public async Task<ActionResult<CudaExecuteResponse>?> ExecuteLinearKernelAsync([FromBody] CudaExecuteRequest request)
        {
            if (!this.cuda.Online || this.cuda.Compiler == null || this.cuda.Launcher == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA service offline",
                    Detail = "The CUDA service is currently offline. Please ensure that the CUDA runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                object[] args = DataParser.ParseArgumentValues(request.ArgumentValues, request.KernelInfo);

                var pointer = args.FirstOrDefault(a => a.GetType() == typeof(IntPtr)) as IntPtr?;
                if (pointer == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Pointer argument missing",
                        Detail = "The kernel execution requires a pointer argument, but none was provided.",
                        Status = 400
                    };
                    return this.BadRequest(pd);
                }

                var mem = this.cuda.RegisteredMemory.FirstOrDefault(m => m.IndexPointer == pointer);
                if (mem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Pointer not registered",
                        Detail = $"The pointer '{pointer}' is not registered in the CUDA service.",
                        Status = 400
                    };
                    return this.BadRequest(pd);
                }

                nint length = (nint) mem.TotalLength;

                this.cuda.SetCurrent();
                var resultPtr = await this.cuda.Launcher.ExecuteLinearKernelAsync(request.KernelInfo.FunctionName, pointer.Value, args, length);
                if (resultPtr == null || resultPtr == IntPtr.Zero)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Kernel execution failed",
                        Detail = $"The kernel '{request.KernelInfo.FunctionName}' execution failed.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                if (request.UnloadAfterExecution)
                {
                    this.cuda.Compiler.UnloadKernel();
                    if (!string.IsNullOrEmpty(this.cuda.Compiler.KernelName))
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Kernel unload error",
                            Detail = $"The kernel '{this.cuda.Compiler.KernelName}' could not be unloaded after execution.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }
                }

                var response = CudaResponsesBuilder.BuildCudaExecuteResponse(request.KernelInfo, resultPtr.HasValue, resultPtr, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error executing kernel",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }

        }


    }
}
