using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CudaMemoryController : ControllerBase
    {
        private readonly ICudaService cuda;


        public CudaMemoryController(ICudaService cuda)
        {
            this.cuda = cuda;
        }


        [HttpGet("memory-list")]
        public ActionResult<IEnumerable<CudaMemInfo>?> GetMemoryList()
        {
            try
            {
                if (!this.cuda.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }

                var memoryList = CudaInfosBuilder.BuildCudaMemoryInfos(this.cuda);
                if (!memoryList.Any())
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "No CUDA memory found",
                        Detail = "No CUDA memory objects were found on this system.",
                        Status = 404
                    });
                }

                return this.Ok(memoryList);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving CUDA memory",
                    Detail = ex.Message,
                    Status = 500
                };

                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("memory-info/{indexPointerOrId}")]
        public ActionResult<CudaMemInfo?> GetMemoryInfo(string indexPointerOrId)
        {
            try
            {
                if (!this.cuda.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }
                var memoryInfo = CudaInfosBuilder.BuildCudaMemoryInfos(this.cuda, indexPointerOrId).FirstOrDefault();
                if (memoryInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "CUDA memory not found",
                        Detail = $"No CUDA memory object found for index/pointer/ID: {indexPointerOrId}.",
                        Status = 404
                    });
                }

                return this.Ok(memoryInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving CUDA memory info",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


        [HttpDelete("memory-free/{indexPointerOrId}")]
        public ActionResult<string?> FreeMemory(string indexPointerOrId)
        {
            try
            {
                if (!this.cuda.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }

                IntPtr.TryParse(indexPointerOrId, out var ptr);
                if (ptr == IntPtr.Zero && this.cuda.RegisteredMemory.Count > 0)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid pointer",
                        Detail = $"The provided index/pointer/ID '{indexPointerOrId}' is not a valid pointer.",
                        Status = 400
                    });
                }

                var memoryInfo = CudaInfosBuilder.BuildCudaMemoryInfos(this.cuda, indexPointerOrId).FirstOrDefault();
                if (memoryInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "CUDA memory not found",
                        Detail = $"No CUDA memory object found for index/pointer/ID: {indexPointerOrId}.",
                        Status = 404
                    });
                }

                if (ptr == IntPtr.Zero)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Invalid pointer",
                        Detail = $"The provided index/pointer/ID '{indexPointerOrId}' is not a valid pointer.",
                        Status = 400
                    };

                    return this.StatusCode(400, pd);
                }

                string freed = this.cuda.FreeMemory(ptr).ToString();

                return this.Ok(freed);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error freeing CUDA memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("memory-all-memory")]
        public async Task<ActionResult<string?>> FreeAllMemoryAsync()
        {
            try
            {
                if (!this.cuda.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }

                string freed = (await Task.WhenAll(this.cuda.RegisteredMemory.Select(m => this.cuda.FreeMemoryAsync(m.IndexPointer)))).Sum().ToString();

                return this.Ok(freed);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error freeing CUDA memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


        [HttpPost("push")]
        public async Task<ActionResult<CudaPushResponse>?> PushAsync(CudaPushRequest request)
        {
            var response = new CudaPushResponse();

            try
            {
                if (!this.cuda.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }

                if (request.Payload == null)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid request",
                        Detail = "The request payload is null.",
                        Status = 400
                    });
                }
                var startDate = DateTime.Now;

                Type elementType = Type.GetType(request.ElementType, throwOnError: true, ignoreCase: true)!;
                object? data = request.Payload is CudaPayload1D p1
                    ? await InvokeGenericAsync(typeof(DataParser), nameof(DataParser.ParseAsync), elementType, p1)
                    : request.Payload is CudaPayload2D p2
                        ? await InvokeGenericAsync(typeof(DataParser), nameof(DataParser.ParseAsync), elementType, p2)
                        : throw new ArgumentException("Unsupported payload type.");
                if (data == null)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid request",
                        Detail = "The request payload could not be parsed.",
                        Status = 400
                    });
                }
                if (request.Payload is CudaPayload1D { Data.Length: 0 } || request.Payload is CudaPayload2D { DataChunks: null })
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid request",
                        Detail = "The request payload is empty.",
                        Status = 400
                    });
                }

                CudaMem? mem = request.Payload is CudaPayload1D
                    ? await InvokeGenericAsync(this.cuda, nameof(ICudaService.PushDataAsync), elementType, data) as CudaMem
                    : request.Payload is CudaPayload2D
                        ? await InvokeGenericAsync(this.cuda, nameof(ICudaService.PushChunksAsync), elementType, data) as CudaMem
                        : throw new ArgumentException("Unsupported payload type.");
                if (mem == null)
                {
                    return this.StatusCode(500, new ProblemDetails
                    {
                        Title = "CUDA push failed",
                        Detail = "The CUDA push operation failed.",
                        Status = 500
                    });
                }

                CudaMemInfo? memInfo = CudaInfosBuilder.BuildCudaMemoryInfo(this.cuda, mem.IndexPointer.ToString());
                if (memInfo == null)
                {
                    return this.StatusCode(500, new ProblemDetails
                    {
                        Title = "CUDA memory info not found",
                        Detail = $"No CUDA memory info found for index/pointer/ID: {mem.IndexPointer}.",
                        Status = 500
                    });
                }

                response.MemoryInfo = memInfo;
                response.ElapsedMs = (int) (DateTime.Now - startDate).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error pushing CUDA memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }

            return this.Ok(response);
        }

        [HttpPost("pull")]
        public async Task<ActionResult<CudaPullResponse>?> PullAsync(CudaPullRequest request)
        {
            try
            {
                if (!this.cuda.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }
                if (request.IndexPointerOrId == null)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid request",
                        Detail = "The request index/pointer/ID is null.",
                        Status = 400
                    });
                }

                var startDate = DateTime.Now;

                // Try get CudaMem-object for the given index/pointer/ID
                CudaMemInfo? memInfo = CudaInfosBuilder.BuildCudaMemoryInfo(this.cuda, request.IndexPointerOrId);
                if (memInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "CUDA memory not found",
                        Detail = $"No CUDA memory object found for index/pointer/ID: {request.IndexPointerOrId}.",
                        Status = 404
                    });
                }

                // Get Type T
                Type t = Type.GetType(memInfo.ElementType, true, true) ?? throw new ArgumentException($"Element type '{memInfo.ElementType}' could not be found.");

                // Create DTO response
                CudaPullResponse response = new()
                {
                    MemoryInfoReference = memInfo
                };

                bool isChunked = memInfo.Count != 1;
                if (!IntPtr.TryParse(memInfo.IndexPointer, out var indexPointer) || indexPointer == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"The CUDA memory pointer '{memInfo.IndexPointer}' is invalid.");
                }
                object? data = await InvokeGenericAsync(
                    this.cuda,
                    isChunked ? nameof(ICudaService.PullChunksAsync) : nameof(ICudaService.PullDataAsync),
                    t,
                    indexPointer,
                    request.FreeAfterPull);
                if (data == null)
                {
                    throw new InvalidOperationException("The CUDA pull operation returned no data.");
                }

                response.Payload = await InvokeGenericAsync(
                    typeof(DataSerializer),
                    nameof(DataSerializer.SerializeAsync),
                    t,
                    data,
                    true,
                    isChunked) as ICudaPayload ?? throw new InvalidOperationException("Failed to serialize CUDA pull data.");

                response.ElapsedMs = (int) (DateTime.Now - startDate).TotalMilliseconds;

                return this.Ok(response);
            }

            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error pulling CUDA memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        private static async Task<object?> InvokeGenericAsync(object target, string methodName, Type elementType, params object[] arguments)
        {
            int parameterCount = methodName == nameof(DataSerializer.SerializeAsync) ? arguments.Length - 1 : arguments.Length;
            var methods = target is Type targetType ? targetType.GetMethods() : typeof(ICudaService).GetMethods();
            var method = methods.SingleOrDefault(method => method.Name == methodName && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == parameterCount &&
                (methodName != nameof(DataParser.ParseAsync) || method.GetParameters()[0].ParameterType.IsInstanceOfType(arguments[0])) &&
                (methodName != nameof(DataSerializer.SerializeAsync) || method.GetParameters()[0].ParameterType.GetGenericArguments()[0].IsGenericType == (arguments.Length == 3 && (bool)arguments[2])));
            if (method == null)
            {
                throw new InvalidOperationException($"Failed to find generic method '{methodName}'.");
            }

            object? invocationTarget = target is Type ? null : target;
            object[] invocationArguments = arguments.Length == 3 && methodName == nameof(DataSerializer.SerializeAsync) ? arguments[..2] : arguments;
            var task = method.MakeGenericMethod(elementType).Invoke(invocationTarget, invocationArguments) as Task ?? throw new InvalidOperationException($"Generic method '{methodName}' did not return a task.");
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }



    }
}
