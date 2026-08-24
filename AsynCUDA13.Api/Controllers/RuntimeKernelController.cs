using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    public class RuntimeKernelController : ApiControllerBase
    {

        public RuntimeKernelController(IRuntimeService runtime)
            : base(runtime)
        {

        }

        [HttpGet("kernels")]
        public ActionResult<IEnumerable<RuntimeKernelInfo>?> GetKernels(bool filterCompiled = true)
        {
            try
            {
                var infos = RuntimeInfosBuilder.BuildRuntimeKernelInfos(this.backend, filterCompiled);
                if (infos?.Length <= 0)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "No kernels found",
                        Detail = $"No {this.RuntimeType} kernels were found in the current context.",
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
        public async Task<ActionResult<RuntimeCompileResponse>?> CompileKernelAsync([FromBody] RuntimeCompileRequest request)
        {
            if (!this.backend.Online || this.backend.Compiler == null)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} service offline",
                    Detail = $"The {this.RuntimeType} service is currently offline. Please ensure that the {this.RuntimeType} runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                string? ptxPath = request.AsyncCall
                    ? await Task.Run(() => this.backend.Compiler.CompileKernel(request.KernelSource))
                    : this.backend.Compiler.CompileKernel(request.KernelSource);
                if (string.IsNullOrEmpty(ptxPath))
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Compilation failed",
                        Detail = $"The kernel '{request.KernelName}' could not be compiled due to a {this.RuntimeType} availability error. (CudaCompiler or PrimaryContext was not initialized.)",
                        Status = 400
                    };
                    return this.BadRequest(pd);
                }

                var info = RuntimeInfosBuilder.BuildRuntimeKernelInfo(this.backend, request.KernelName);
                var response = RuntimeResponsesBuilder.BuildCompileResponse(info, (int) (DateTime.Now - started).TotalMilliseconds);
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

                if (!ptxPath.EndsWith(".ptx", StringComparison.OrdinalIgnoreCase))
                {
                    response.BuildLog = ptxPath;
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
        public async Task<ActionResult<RuntimeExecuteResponse>?> ExecuteGenericKernelAsync([FromBody] RuntimeExecuteRequest request)
        {
            if (!this.backend.Online || this.backend.Compiler == null || this.backend.Launcher == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "{this.RuntimeType} service offline",
                    Detail = "The {this.RuntimeType} service is currently offline. Please ensure that the {this.RuntimeType} runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                object[] args = DataParser.ParseArgumentValues(request.ArgumentValues, request.KernelInfo);

                this.backend.SetCurrent();
                var result = await Task.Run(() => this.backend.Launcher.Execute(request.KernelInfo.FunctionName, args));

                if (request.UnloadAfterExecution)
                {
                    this.backend.Compiler.UnloadKernel(null);
                    if (!string.IsNullOrEmpty(this.backend.Compiler.KernelName))
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Kernel unload error",
                            Detail = $"The kernel '{this.backend.Compiler.KernelName}' could not be unloaded after execution.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }
                }

                var response = RuntimeResponsesBuilder.BuildExecuteResponse(request.KernelInfo, result.HasValue, result ?? -1);
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
        public async Task<ActionResult<RuntimeExecuteResponse>?> ExecuteLinearKernelAsync([FromBody] RuntimeExecuteRequest request)
        {
            if (!this.backend.Online || this.backend.Compiler == null || this.backend.Launcher == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "{this.RuntimeType} service offline",
                    Detail = "The {this.RuntimeType} service is currently offline. Please ensure that the {this.RuntimeType} runtime is available and try again.",
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

                var mem = this.backend.RegisteredMemory.FirstOrDefault(m => m.IndexPointer == pointer);
                if (mem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Pointer not registered",
                        Detail = $"The pointer '{pointer}' is not registered in the {this.RuntimeType} service.",
                        Status = 400
                    };
                    return this.BadRequest(pd);
                }

                IntPtr length = (IntPtr) mem.TotalLength;

                this.backend.SetCurrent();
                var resultPtr = await Task.Run(() => this.backend.Launcher.Execute(request.KernelInfo.FunctionName, pointer.Value, args, length));
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
                    this.backend.Compiler.UnloadKernel(null);
                    if (!string.IsNullOrEmpty(this.backend.Compiler.KernelName))
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Kernel unload error",
                            Detail = $"The kernel '{this.backend.Compiler.KernelName}' could not be unloaded after execution.",
                            Status = 500
                        };
                        return this.StatusCode(500, pd);
                    }
                }

                var response = RuntimeResponsesBuilder.BuildExecuteResponse(request.KernelInfo, resultPtr.HasValue, resultPtr, (int) (DateTime.Now - started).TotalMilliseconds);
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
