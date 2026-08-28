using AsynCUDA13.Api.Services;
using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.Xml;

namespace AsynCUDA13.Api.Controllers
{
    public class RuntimeKernelController : ApiControllerBase
    {
        private readonly IAssetProvider assetProvider;

        public RuntimeKernelController(IRuntimeService runtime, IAssetProvider assets)
            : base(runtime)
        {
            this.assetProvider = assets;
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
            if (string.IsNullOrEmpty(request.KernelSource))
            {
                var pd = new ProblemDetails
                {
                    Title = "Kernel compile request DTO had no KernelSource.",
                    Detail = "Kernel source was null.",
                    Status = 400
                };
                return this.StatusCode(400, pd);
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

                var info = RuntimeInfosBuilder.BuildRuntimeKernelInfo(this.backend, Path.GetFileNameWithoutExtension(ptxPath));
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
                    Title = $"${this.RuntimeType} service offline",
                    Detail = $"The {this.RuntimeType} service is currently offline. Please ensure that the {this.RuntimeType} runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            if (request.KernelInfo == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "Kernel execution request DTO had no KernelInfo.",
                    Detail = "KernelInfo was null.",
                    Status = 400
                };
                return this.StatusCode(400, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                object[] args = DataParser.ParseArgumentValues(request.ArgumentValues, request.KernelInfo);

                this.backend.SetCurrent();
                var result = request.AsyncCall ? await this.backend.Launcher.ExecuteAsync(request.KernelInfo.FunctionName, args) : this.backend.Launcher.Execute(request.KernelInfo.FunctionName, args);
                if (result == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Kernel execute error",
                        Detail = $"The kernel '{this.backend.Compiler.KernelName}' could not be executed or did not return a response DTO.",
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

                if (request.CreateResultPointerAssetReference)
                {
                    string[] assetPtrs = result.GetAssetPointers(request);
                    Guid[]? assetGuids = this.assetProvider.GetAssetIdsByPointers(assetPtrs.Select(aPtr => long.TryParse(aPtr, out var ptr) ? ptr : 0).Where(ptr => ptr != 0));
                    if (assetGuids.Length <= 0)
                    {
                        assetGuids = null;
                    }

                    foreach (var ptr in result.ResultPointers ?? [])
                    {
                        RuntimeMemInfo? memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, ptr, assetGuids);
                        if (memInfo == null)
                        {
                            continue;
                        }

                        // Set references on the actual memory object in the backend registry
                        var mem = this.backend.RegisteredMemory.FirstOrDefault(m => m.IndexPointer.ToString() == memInfo.IndexPointer);
                        if (mem != null)
                        {
                            if (assetGuids != null && assetGuids.Length > 0)
                            {
                                mem.AssetReferenceIds = assetGuids;
                                mem.AssetReferenceId = assetGuids[0];
                            }
                        }
                    }
                }

                result.KernelInfo = request.KernelInfo;

                return this.Ok(result);
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
                    Title = $"{this.RuntimeType} service offline",
                    Detail = $"The {this.RuntimeType} service is currently offline. Please ensure that the {this.RuntimeType} runtime is available and try again.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            if (request.KernelInfo == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "Kernel execution request DTO had no KernelInfo.",
                    Detail = "KernelInfo was null.",
                    Status = 400
                };
                return this.StatusCode(400, pd);
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

                IntPtr length = checked((IntPtr) mem.TotalLength);

                this.backend.SetCurrent();
                var result = request.AsyncCall ? await this.backend.Launcher.ExecuteAsync(request.KernelInfo.FunctionName, args) : this.backend.Launcher.Execute(request.KernelInfo.FunctionName, pointer.Value, args, length);
                if (result == null)
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

                if (request.CreateResultPointerAssetReference)
                {
                    string[] assetPtrs = result.GetAssetPointers(request);
                    Guid[] assetGuids = this.assetProvider.GetAssetIdsByPointers(assetPtrs.Select(aPtr => long.TryParse(aPtr, out var ptr) ? ptr : 0).Where(ptr => ptr != 0));

                    foreach (var ptr in result.ResultPointers ?? [])
                    {
                        RuntimeMemInfo? memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, ptr, assetGuids);
                        if (memInfo == null || memInfo.AssetReferenceId == null)
                        {
                            continue;
                        }

                        var audio = this.assetProvider.GetAudioInfo(memInfo.AssetReferenceId.Value);
                        var image = this.assetProvider.GetImageInfo(memInfo.AssetReferenceId.Value);
                        if (audio != null)
                        {
                            this.assetProvider.CreateFromInfo(audio);
                        }
                        if (image != null)
                        {
                            this.assetProvider.CreateFromInfo(image);
                        }
                    }
                }

                result.KernelInfo = request.KernelInfo;
                return this.Ok(result);
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
