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

        [HttpDelete("free-all-memory")]
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

                dynamic data = request.Payload is CudaPayload1D p1 ? DataParser.ParseAsync(p1, request.ElementType) :
                               request.Payload is CudaPayload2D p2 ? DataParser.ParseAsync(p2, request.ElementType) :
                               throw new ArgumentException("Unsupported payload type.");
                if (data == null)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid request",
                        Detail = "The request payload could not be parsed.",
                        Status = 400
                    });
                }

                CudaMem? mem = request.Payload is CudaPayload1D pd1 ? await this.cuda.PushDataAsync(data) :
                              request.Payload is CudaPayload2D pd2 ? await this.cuda.PushChunksAsync(data) :
                              throw new ArgumentException("Unsupported payload type.");
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

                // Get the appropriate method for pulling data
                var pullMethod = memInfo.Count == 1
                    ? typeof(CudaService).GetMethod(nameof(CudaService.PullDataAsync), new Type[] { typeof(IntPtr), typeof(bool) })
                    : typeof(CudaService).GetMethod(nameof(CudaService.PullChunksAsync), new Type[] { typeof(IntPtr), typeof(bool) });

                if (pullMethod == null)
                {
                    throw new InvalidOperationException("Could not find pull method for CUDA memory.");
                }

                // Pull data from CUDA
                var genericPullMethod = pullMethod.MakeGenericMethod(t);
                var pointer = new IntPtr(long.Parse(memInfo.Pointers[0]));
                var dataTask = genericPullMethod.Invoke(this.cuda, new object[] { pointer, false }) as Task<dynamic>;
                var data = dataTask != null ? await dataTask : null;
                bool isChunked = memInfo.Count > 1;

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
                (methodName != nameof(DataSerializer.SerializeAsync) || method.GetParameters()[0].ParameterType.GetGenericArguments()[0].IsGenericType == (arguments.Length == 3 && (bool) arguments[2])));
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
