using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Serialization;
using ManagedCuda.VectorTypes;
using Microsoft.AspNetCore.Mvc;
using OpenTK.Mathematics;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RuntimeFourierController : ApiControllerBase
    {
        private readonly AudioCollection audios;


        public RuntimeFourierController(IRuntimeService cuda, AudioCollection audios)
            : base(cuda)
        {
            this.audios = audios;
        }



        [HttpPost("CuFFT")]
        public async Task<ActionResult<RuntimeFourierResponse?>> RunCudaFourierAsync([FromBody] RuntimeFourierRequest request)
        {
            if (!this.backend.Online || this.backend.Fourier == null)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} Service Offline",
                    Detail = $"The {this.RuntimeType} service is currently offline. Please initialize a device.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;

            try
            {
                var inputMem = this.backend.RegisteredMemory.FirstOrDefault(m => m.IndexPointer.ToString().Equals(request.MemoryInfo.IndexPointer));
                if (inputMem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Input Memory Not Found",
                        Detail = $"No registered memory found for index pointer: {request.MemoryInfo.IndexPointer}",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                var inputMemInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, inputMem.IndexPointer.ToString());
                if (inputMemInfo == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Input Memory Info Not Found",
                        Detail = $"Failed to build input memory info for {inputMem.IndexPointer}",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                bool inverse = request.Inverse ?? inputMem.ElementType == typeof(float) || inputMem.ElementType == typeof(Double);

                var resultMemPtr = request.AsyncCall ?
                    (inverse ? await this.backend.Fourier.PerformIfftAsync(inputMem.IndexPointer, request.KeepInputBuffer) : await this.backend.Fourier.PerformFftAsync(inputMem.IndexPointer, request.KeepInputBuffer)) :
                    (inverse ? this.backend.Fourier.PerformIfft(inputMem.IndexPointer, request.KeepInputBuffer) : this.backend.Fourier.PerformFft(inputMem.IndexPointer, request.KeepInputBuffer));
                if (resultMemPtr == IntPtr.Zero)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "{this.RuntimeType} Fourier Transform Failed",
                        Detail = "The {this.RuntimeType} Fourier transform operation failed.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var outputMem = this.backend.RegisteredMemory.FirstOrDefault(m => m.IndexPointer == resultMemPtr);
                if (outputMem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Output Memory Not Found",
                        Detail = $"No registered memory found for result index pointer: {resultMemPtr}",
                        Status = 204
                    };
                    return this.StatusCode(204, pd);
                }

                ISimdPayload? payload = null;
                if (request.AutoPullResult)
                {
                    if (outputMem.ElementType == typeof(float))
                    {
                        payload = outputMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<float>(outputMem.IndexPointer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<float>(outputMem.IndexPointer) ?? []);
                    }
                    else if (outputMem.ElementType == typeof(Double))
                    {
                        payload = outputMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<Double>(outputMem.IndexPointer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<Double>(outputMem.IndexPointer) ?? []);
                    }
                    else if (outputMem.ElementType == typeof(float2))
                    {
                        payload = outputMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<float2>(outputMem.IndexPointer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<float2>(outputMem.IndexPointer) ?? []);
                    }
                    else
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Unsupported Element Type",
                            Detail = $"The element type '{outputMem.ElementType}' is not supported for automatic pulling.",
                            Status = 400
                        };
                        return this.StatusCode(400, pd);
                    }
                }

                var response = RuntimeResponsesBuilder.BuildFourierResponse(inputMemInfo, RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, outputMem.IndexPointer.ToString()), payload, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error during {this.RuntimeType} Fourier Transform",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("CuFFT/audio")]
        public async Task<ActionResult<RuntimeFourierResponse?>> RunCudaFourierOnAudioAsync([FromBody] string audioIdOrName, [FromQuery] int chunkSize = 0, [FromQuery] float overlap = 0.5f, [FromQuery] bool autoPullResult = false, [FromQuery] bool keepDataOrBuffer = false)
        {
            if (!this.backend.Online || this.backend.Fourier == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "{this.RuntimeType} Service Offline",
                    Detail = "The {this.RuntimeType} service is currently offline. Please initialize a device.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;
            try
            {
                var audio = this.audios[audioIdOrName] ?? this.audios[Guid.TryParse(audioIdOrName, out var guid) ? guid : Guid.Empty];
                if (audio == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Audio Not Found",
                        Detail = $"No audio found with ID: {audioIdOrName}",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                IRuntimeMem? audioMem = null;
                // Optionally push
                if ((audio.Pointer <= 0 || audio.Pointer == IntPtr.Zero) && audio.Data.LongLength > 0)
                {
                    audioMem = chunkSize > 0 ? await this.backend.Register.PushChunksAsync(audio.GetChunks(chunkSize, overlap, keepDataOrBuffer)) : await this.backend.Register.PushDataAsync(audio.Data);
                    if (!keepDataOrBuffer)
                    {
                        audio.Data = [];
                    }
                }
                else
                {
                    audioMem = this.backend[(IntPtr) audio.Pointer];
                }
                if (audioMem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Audio Memory Not Found",
                        Detail = $"No registered memory found for audio with ID: {audioIdOrName}",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                audio.Pointer = audioMem.IndexPointer;

                var inputMemInfo = RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, audioMem.IndexPointer.ToString());
                if (inputMemInfo == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Input Memory Info Not Found",
                        Detail = $"Failed to build input memory info for audio with ID: {audioIdOrName}",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var resultMemPtr = (audioMem.ElementType == typeof(float2) || audioMem.ElementType == typeof(Vector2)) ? await this.backend.Fourier.PerformIfftAsync(audioMem.IndexPointer, keepDataOrBuffer) : await this.backend.Fourier.PerformFftAsync(audioMem.IndexPointer, keepDataOrBuffer);
                var resultMem = this.backend[resultMemPtr];
                if (resultMem == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "Output Memory Not Found",
                        Detail = $"No registered memory found for result index pointer: {resultMemPtr}",
                        Status = 204
                    };
                    return this.StatusCode(204, pd);
                }

                audio.Pointer = resultMem.IndexPointer;

                ISimdPayload? payload = null;
                if (autoPullResult)
                {
                    if (resultMem.ElementType == typeof(float))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<float>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<float>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
                    }
                    else if (resultMem.ElementType == typeof(Double))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<Double>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<Double>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
                    }
                    else if (resultMem.ElementType == typeof(float2))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<float2>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<float2>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
                    }
                    else if (resultMem.ElementType == typeof(Vector2))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.backend.Register.PullDataAsync<Vector2>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.backend.Register.PullChunksAsync<Vector2>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
                    }
                    else
                    {
                        var pd = new ProblemDetails
                        {
                            Title = "Unsupported Element Type",
                            Detail = $"The element type '{resultMem.ElementType}' is not supported for automatic pulling.",
                            Status = 400
                        };
                        return this.StatusCode(400, pd);
                    }
                }

                var response = RuntimeResponsesBuilder.BuildFourierResponse(inputMemInfo, RuntimeInfosBuilder.BuildRuntimeMemoryInfo(this.backend, resultMem.IndexPointer.ToString()), payload, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error during {this.RuntimeType} Fourier Transform on Audio",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


    }
}
