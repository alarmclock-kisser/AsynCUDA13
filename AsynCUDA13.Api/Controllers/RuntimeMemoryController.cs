using AsynCUDA13.Api.Services;
using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Mvc;
using NAudio.CoreAudioApi;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RuntimeMemoryController : ApiControllerBase
    {
        private readonly IAssetProvider assetProvider;


        public RuntimeMemoryController(IRuntimeService cuda, IAssetProvider assetProvider)
            : base(cuda)
        {
            this.assetProvider = assetProvider;
        }


        [HttpGet("memory-list")]
        public ActionResult<IEnumerable<RuntimeMemInfo>?> GetMemoryList()
        {
            try
            {
                if (!this.backend.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} not initialized",
                        Detail = $"{this.RuntimeType} is not initialized.",
                        Status = 503
                    });
                }

                var memoryList = RuntimeInfosBuilder.BuildRuntimeMemoryInfos(this.backend);
                if (memoryList == null || memoryList.Length <= 0)
                {
                    memoryList = [];
                }

                return this.Ok(memoryList);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error retrieving {this.RuntimeType} memory",
                    Detail = ex.Message,
                    Status = 500
                };

                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("memory-info/{indexPointerOrId}")]
        public ActionResult<RuntimeMemInfo?> GetMemoryInfo(string indexPointerOrId)
        {
            try
            {
                if (!this.backend.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "{this.RuntimeType} not initialized",
                        Detail = "{this.RuntimeType} is not initialized.",
                        Status = 503
                    });
                }
                var memoryInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfos(this.backend, indexPointerOrId)?.FirstOrDefault();
                if (memoryInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} memory not found",
                        Detail = $"No {this.RuntimeType} memory object found for index/pointer/ID: {indexPointerOrId}.",
                        Status = 404
                    });
                }

                return this.Ok(memoryInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error retrieving {this.RuntimeType} memory info",
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
                if (!this.backend.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "{this.RuntimeType} not initialized",
                        Detail = "{this.RuntimeType} is not initialized.",
                        Status = 503
                    });
                }

                IntPtr.TryParse(indexPointerOrId, out var ptr);
                if (ptr == IntPtr.Zero && this.backend.RegisteredMemory.Count > 0)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Invalid pointer",
                        Detail = $"The provided index/pointer/ID '{indexPointerOrId}' is not a valid pointer.",
                        Status = 400
                    });
                }

                var memoryInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfos(this.backend, indexPointerOrId)?.FirstOrDefault();
                if (memoryInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} memory not found",
                        Detail = $"No {this.RuntimeType} memory object found for index/pointer/ID: {indexPointerOrId}.",
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

                string freed = this.backend.FreeMemory(ptr).ToString();

                return this.Ok(freed);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error freeing {this.RuntimeType} memory",
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
                if (!this.backend.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "{this.RuntimeType} not initialized",
                        Detail = "{this.RuntimeType} is not initialized.",
                        Status = 503
                    });
                }

                string freed = (await Task.WhenAll(this.backend.RegisteredMemory.Select(async m => await Task.Run(() =>this.backend.FreeMemory(m.IndexPointer))))).Sum().ToString();

                return this.Ok(freed);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error freeing {this.RuntimeType} memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


        [HttpPost("push")]
        [DisableRequestSizeLimit]
        public async Task<ActionResult<RuntimePushResponse>?> PushAsync(RuntimePushRequest request)
        {
            var response = new RuntimePushResponse();

            try
            {
                if (!this.backend.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "{this.RuntimeType} not initialized",
                        Detail = "{this.RuntimeType} is not initialized.",
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

                dynamic data = request.Payload is SimdPayload1D p1 ? DataParser.ParseAsync(p1, request.ElementType) :
                               request.Payload is SimdPayload2D p2 ? DataParser.ParseAsync(p2, request.ElementType) :
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

                CudaMem? mem = request.Payload is SimdPayload1D pd1 ? await this.backend.Register.PushDataAsync(data) :
                              request.Payload is SimdPayload2D pd2 ? await this.backend.Register.PushChunksAsync(data) :
                              throw new ArgumentException("Unsupported payload type.");
                if (mem == null)
                {
                    return this.StatusCode(500, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} push failed",
                        Detail = $"The {this.RuntimeType} push operation failed.",
                        Status = 500
                    });
                }

                RuntimeMemInfo? memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, mem.IndexPointer.ToString());
                if (memInfo == null)
                {
                    return this.StatusCode(500, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} memory info not found",
                        Detail = $"No {this.RuntimeType} memory info found for index/pointer/ID: {mem.IndexPointer}.",
                        Status = 500
                    });
                }

                response = RuntimeResponsesBuilder.BuildPushResponse(this.backend, memInfo.IndexPointer.ToString(), (int) (DateTime.Now - startDate).TotalMilliseconds);
                response.Success = true;
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error pushing {this.RuntimeType} memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }

            return this.Ok(response);
        }

        [HttpPost("pull")]
        public async Task<ActionResult<RuntimePullResponse>?> PullAsync(RuntimePullRequest request)
        {
            try
            {
                if (!this.backend.Online)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} not initialized",
                        Detail = $"{this.RuntimeType} is not initialized.",
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
                RuntimeMemInfo? memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, request.IndexPointerOrId);
                if (memInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "{this.RuntimeType} memory not found",
                        Detail = $"No {this.RuntimeType} memory object found for index/pointer/ID: {request.IndexPointerOrId}.",
                        Status = 404
                    });
                }

                // Get Type T
                Type t = Type.GetType(memInfo.ElementType, true, true) ?? throw new ArgumentException($"Element type '{memInfo.ElementType}' could not be found.");

                // Create DTO response
                RuntimePullResponse response = new()
                {
                    MemoryInfoReference = memInfo
                };

                // Get the appropriate method for pulling data
                var pullMethod = memInfo.Count == 1
                    ? typeof(IRuntimeRegister).GetMethod(nameof(IRuntimeRegister.PullDataAsync), new Type[] { typeof(IntPtr), typeof(bool) })
                    : typeof(IRuntimeRegister).GetMethod(nameof(IRuntimeRegister.PullChunksAsync), new Type[] { typeof(IntPtr), typeof(bool) });

                if (pullMethod == null)
                {
                    throw new InvalidOperationException($"Could not find pull method for {this.RuntimeType} memory.");
                }

                // Pull data from CUDA
                var genericPullMethod = pullMethod.MakeGenericMethod(t);
                var pointer = new IntPtr(long.Parse(memInfo.Pointers[0]));
                var data = genericPullMethod.Invoke(this.backend, new object[] { pointer, false }) is Task<dynamic> dataTask ? await dataTask : null;
                bool isChunked = memInfo.Count > 1;

                response.Payload = await InvokeGenericAsync(
                    typeof(DataSerializer),
                    nameof(DataSerializer.SerializeAsync),
                    t,
                    data,
                    true,
                    isChunked) as ISimdPayload ?? throw new InvalidOperationException("Failed to serialize {this.RuntimeType} pull data.");

                response.ElapsedMs = (int) (DateTime.Now - startDate).TotalMilliseconds;
                response.Success = true;

                return this.Ok(response);
            }

            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error pulling {this.RuntimeType} memory",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


        [HttpGet("push-asset")]
        public async Task<ActionResult<RuntimePushResponse>?> PushAssetAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = false)
        {
            if (!this.backend.Online)
            {
                return this.StatusCode(503, new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not initialized",
                    Detail = $"{this.RuntimeType} is not initialized.",
                    Status = 503
                });
            }

            var startDate = DateTime.Now;
            try
            {
                Guid.TryParse(assetIdOrName, out var guid);
                var audio = this.assetProvider.GetAudio(guid) ?? this.assetProvider.GetAudio(assetIdOrName);
                var image = this.assetProvider.GetImage(guid) ?? this.assetProvider.GetImage(assetIdOrName);

                if (audio == null && image == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "Asset not found",
                        Detail = $"No audio or image asset found for ID or name: {assetIdOrName}.",
                        Status = 404
                    });
                }

                IRuntimeMem? mem = null;

                if (audio != null)
                {
                    if (chunkSize <= 1)
                    {
                        if (audio.Data == null || audio.Data.Length == 0)
                        {
                            return this.StatusCode(400, new ProblemDetails
                            {
                                Title = "Invalid asset data",
                                Detail = $"Audio asset '{assetIdOrName}' contains no float data.",
                                Status = 400
                            });
                        }
                        // Direktes Pushen des float[] Arrays in den VRAM
                        mem = await this.backend.Register.PushDataAsync(audio.Data);
                        audio.Pointer = mem?.IndexPointer ?? 0;
                    }
                    else
                    {
                        var chunks = audio.GetChunks(chunkSize, overlap, keepData);
                        if (chunks == null || chunks.Length == 0)
                        {
                            return this.StatusCode(400, new ProblemDetails
                            {
                                Title = "Invalid asset data",
                                Detail = $"Failed to slice audio asset '{assetIdOrName}' into chunks.",
                                Status = 400
                            });
                        }
                        // Direktes Pushen der float[][] Chunks in den VRAM
                        mem = await this.backend.Register.PushChunksAsync(chunks);
                        audio.Pointer = mem?.IndexPointer ?? 0;
                    }
                }
                else if (image != null)
                {
                    Byte[] imageBytes = (await image.GetBytesAsync(keepData)).ToArray();
                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        return this.StatusCode(400, new ProblemDetails
                        {
                            Title = "Invalid asset data",
                            Detail = $"Image asset '{assetIdOrName}' contains no byte data.",
                            Status = 400
                        });
                    }
                    // Direktes Pushen des byte[] Bild-Puffers in den VRAM
                    mem = await this.backend.Register.PushDataAsync(imageBytes);
                    image.Pointer = mem?.IndexPointer ?? 0;
                }

                if (mem == null)
                {
                    return this.StatusCode(500, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} push failed",
                        Detail = $"Failed to push asset '{assetIdOrName}' to {this.RuntimeType} VRAM.",
                        Status = 500
                    });
                }

                RuntimeMemInfo? memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, mem.IndexPointer.ToString());
                if (memInfo == null)
                {
                    return this.StatusCode(500, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} memory info not found",
                        Detail = $"No {this.RuntimeType} memory info found for index/pointer: {mem.IndexPointer}.",
                        Status = 500
                    });
                }

                var response = RuntimeResponsesBuilder.BuildPushResponse(this.backend, memInfo.IndexPointer.ToString(), (int) (DateTime.Now - startDate).TotalMilliseconds);
                response.Success = true;

                return this.Ok(response);
            }
            catch (Exception ex)
            {
                return this.StatusCode(500, new ProblemDetails
                {
                    Title = "Internal server error",
                    Detail = ex.Message,
                    Status = 500
                });
            }
        }

        [HttpGet("pull-asset")]
        public async Task<ActionResult<RuntimePullResponse>?> PullAssetAsync(string assetIdOrName, bool keepBuffer = false)
        {
            if (!this.backend.Online)
            {
                return this.StatusCode(503, new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not initialized",
                    Detail = $"{this.RuntimeType} is not initialized.",
                    Status = 503
                });
            }

            var startDate = DateTime.Now;
            try
            {
                Guid.TryParse(assetIdOrName, out var guid);
                var audio = this.assetProvider.GetAudio(guid) ?? this.assetProvider.GetAudio(assetIdOrName);
                var image = this.assetProvider.GetImage(guid) ?? this.assetProvider.GetImage(assetIdOrName);

                if (audio == null && image == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "Asset not found",
                        Detail = $"No audio or image asset found for ID or name: {assetIdOrName}.",
                        Status = 404
                    });
                }

                IntPtr ptr = audio != null ? (nint) audio.Pointer : image != null ? (nint) image.Pointer : IntPtr.Zero;
                if (ptr == IntPtr.Zero)
                {
                    return this.StatusCode(400, new ProblemDetails
                    {
                        Title = "Asset not in VRAM",
                        Detail = $"Asset '{assetIdOrName}' has no valid {this.RuntimeType} memory pointer allocated.",
                        Status = 400
                    });
                }

                IRuntimeMem? mem = this.backend[ptr];
                RuntimeMemInfo? memInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, ptr.ToString());
                if (mem == null || memInfo == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} memory not found",
                        Detail = $"No {this.RuntimeType} memory object found for index/pointer: {ptr}.",
                        Status = 404
                    });
                }

                if (audio != null)
                {
                    if (mem.Count > 1)
                    {
                        // Direktes Pulling der Chunks aus dem VRAM in den Audio-Puffer
                        float[][] chunks = (await this.backend.Register.PullChunksAsync<float>(ptr, keepBuffer))?.ToArray() ?? [];
                        if (chunks == null)
                        {
                            return this.StatusCode(500, new ProblemDetails { Title = $"{this.RuntimeType} pull failed", Detail = "Failed to pull audio chunks from CUDA.", Status = 500 });
                        }
                        await audio.AggregateChunksAsync(chunks, (int) mem.IndexLength);
                    }
                    else
                    {
                        // Direktes Pulling des float[] Arrays aus dem VRAM
                        float[] data = (await this.backend.Register.PullDataAsync<float>(ptr, keepBuffer)) ?? [];
                        if (data == null)
                        {
                            return this.StatusCode(500, new ProblemDetails { Title = $"{this.RuntimeType} pull failed", Detail = "Failed to pull audio data from CUDA.", Status = 500 });
                        }
                        audio.Data = data;
                    }

                    if (!keepBuffer)
                    {
                        audio.Pointer = 0;
                    }
                }
                else if (image != null)
                {
                    // Direktes Pulling der Byte-Daten aus dem VRAM in das Bild
                    Byte[] bytes = (await this.backend.Register.PullDataAsync<Byte>(ptr, keepBuffer)) ?? [];
                    if (bytes == null)
                    {
                        return this.StatusCode(500, new ProblemDetails { Title = $"{this.RuntimeType} pull failed", Detail = "Failed to pull image data from CUDA.", Status = 500 });
                    }
                    await image.SetImageAsync(bytes);

                    if (!keepBuffer)
                    {
                        image.Pointer = 0;
                    }
                }

                // Schlanke Antwort ohne Payload-Ballast zurückgeben
                var response = new RuntimePullResponse
                {
                    MemoryInfoReference = memInfo,
                    ElapsedMs = (int) (DateTime.Now - startDate).TotalMilliseconds,
                    Success = true
                };

                return this.Ok(response);
            }
            catch (Exception ex)
            {
                return this.StatusCode(500, new ProblemDetails
                {
                    Title = "Internal server error",
                    Detail = ex.Message,
                    Status = 500
                });
            }
        }


        [HttpGet("client-push-asset")]
        public async Task<ActionResult<RuntimePushResponse>?> PushAssetClientAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = false)
        {
            if (!this.backend.Online)
            {
                return this.StatusCode(503, new ProblemDetails
                {
                    Title = "{this.RuntimeType} not initialized",
                    Detail = "{this.RuntimeType} is not initialized.",
                    Status = 503
                });
            }

            var startDate = DateTime.Now;
            try
            {
                ISimdPayload? payload = null;

                Guid.TryParse(assetIdOrName, out var guid);
                var audio = this.assetProvider.GetAudio(guid) ?? this.assetProvider.GetAudio(assetIdOrName);
                if (audio != null)
                {
                    payload = chunkSize <= 1 ? await DataSerializer.SerializeAsync(audio.Data) : await DataSerializer.SerializeAsync(audio.GetChunks(chunkSize, overlap, keepData));
                }

                var image = this.assetProvider.GetImage(guid) ?? this.assetProvider.GetImage(assetIdOrName);
                if (image != null)
                {
                    payload = await DataSerializer.SerializeAsync(await image.GetBytesAsync(keepData));
                }

                if (payload == null)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "Asset not found",
                        Detail = $"No audio or image asset found for ID or name: {assetIdOrName}.",
                        Status = 404
                    });
                }

                var pushRequest = new RuntimePushRequest()
                {
                    Payload = payload
                };

                return await this.PushAsync(pushRequest);
            }
            catch (Exception ex)
            {
                return this.StatusCode(500, new ProblemDetails
                {
                    Title = "Internal server error",
                    Detail = ex.Message,
                    Status = 500
                });
            }
        }

        [HttpGet("client-pull-asset")]
        public async Task<ActionResult<RuntimePullResponse>?> PullAssetClientAsync(string assetIdOrName, bool keepBuffer = false)
        {
            if (!this.backend.Online)
            {
                return this.StatusCode(503, new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not initialized",
                    Detail = $"{this.RuntimeType} is not initialized.",
                    Status = 503
                });
            }

            var startDate = DateTime.Now;
            try
            {
                Guid.TryParse(assetIdOrName, out var guid);
                var audio = this.assetProvider.GetAudio(guid) ?? this.assetProvider.GetAudio(assetIdOrName);
                if (audio != null)
                {
                    var pullRequest = new RuntimePullRequest()
                    {
                        IndexPointerOrId = audio.Pointer.ToString(),
                        FreeAfterPull = !keepBuffer
                    };
                    return await this.PullAsync(pullRequest);
                }

                var image = this.assetProvider.GetImage(guid) ?? this.assetProvider.GetImage(assetIdOrName);
                if (image != null)
                {
                    var pullRequest = new RuntimePullRequest()
                    {
                        IndexPointerOrId = image.Pointer.ToString(),
                        FreeAfterPull = !keepBuffer
                    };
                    return await this.PullAsync(pullRequest);
                }

                return this.StatusCode(404, new ProblemDetails
                {
                    Title = "Asset not found",
                    Detail = $"No audio or image asset found for ID or name: {assetIdOrName}.",
                    Status = 404
                });
            }
            catch (Exception ex)
            {
                return this.StatusCode(500, new ProblemDetails
                {
                    Title = "Internal server error",
                    Detail = ex.Message,
                    Status = 500
                });
            }
        }

        private static async Task<object?> InvokeGenericAsync(object target, string methodName, Type elementType, params object[] arguments)
        {
            int parameterCount = methodName == nameof(DataSerializer.SerializeAsync) ? arguments.Length - 1 : arguments.Length;
            var methods = target is Type targetType ? targetType.GetMethods() : typeof(IRuntimeService).GetMethods();
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
