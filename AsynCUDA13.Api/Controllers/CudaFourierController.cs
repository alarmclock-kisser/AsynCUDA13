using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Serialization;
using ManagedCuda.VectorTypes;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CudaFourierController : ApiControllerBase
    {
        private readonly ICudaService cuda;
        private readonly AudioCollection audios;


        public CudaFourierController(ICudaService cuda, AudioCollection audios)
        {
            this.cuda = cuda;
            this.audios = audios;
        }



        [HttpPost("CuFFT")]
        public async Task<ActionResult<CudaFourierResponse?>> RunCudaFourierAsync([FromBody] CudaFourierRequest request)
        {
            if (!this.cuda.Online || this.cuda.Fourier == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA Service Offline",
                    Detail = "The CUDA service is currently offline. Please initialize a device.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }

            DateTime started = DateTime.Now;

            try
            {
                var inputMem = this.cuda.RegisteredMemory.FirstOrDefault(m => m.IndexPointer.ToString().Equals(request.MemoryInfo.IndexPointer));
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

                var inputMemInfo = CudaInfosBuilder.BuildCudaMemoryInfo(this.cuda, inputMem.IndexPointer.ToString());
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

                bool inverse = request.Inverse ?? inputMem.ElementType == typeof(float) || inputMem.ElementType == typeof(double);

                var resultMemPtr = request.AsyncCall ?
                    (inverse ? await this.cuda.Fourier.PerformIfftAsync(inputMem.IndexPointer, request.KeepInputBuffer) : await this.cuda.Fourier.PerformFftAsync(inputMem.IndexPointer, request.KeepInputBuffer)) :
                    (inverse ? this.cuda.Fourier.PerformIfft(inputMem.IndexPointer, request.KeepInputBuffer) : this.cuda.Fourier.PerformFft(inputMem.IndexPointer, request.KeepInputBuffer));
                if (resultMemPtr == IntPtr.Zero)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA Fourier Transform Failed",
                        Detail = "The CUDA Fourier transform operation failed.",
                        Status = 500
                    };
                    return this.StatusCode(500, pd);
                }

                var outputMem = this.cuda.RegisteredMemory.FirstOrDefault(m => m.IndexPointer == resultMemPtr);
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

                ICudaPayload? payload = null;
                if (request.AutoPullResult)
                {
                    if (outputMem.ElementType == typeof(float))
                    {
                        payload = outputMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.cuda.PullDataAsync<float>(outputMem.IndexPointer) ?? []) : await DataSerializer.SerializeAsync(await this.cuda.PullChunksAsync<float>(outputMem.IndexPointer) ?? []);
                    }
                    else if (outputMem.ElementType == typeof(double))
                    {
                        payload = outputMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.cuda.PullDataAsync<double>(outputMem.IndexPointer) ?? []) : await DataSerializer.SerializeAsync(await this.cuda.PullChunksAsync<double>(outputMem.IndexPointer) ?? []);
                    }
                    else if (outputMem.ElementType == typeof(float2))
                    {
                        payload = outputMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.cuda.PullDataAsync<float2>(outputMem.IndexPointer) ?? []) : await DataSerializer.SerializeAsync(await this.cuda.PullChunksAsync<float2>(outputMem.IndexPointer) ?? []);
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

                var response = CudaResponsesBuilder.BuildCudaFourierResponse(inputMemInfo, CudaInfosBuilder.BuildCudaMemoryInfo(this.cuda, outputMem.IndexPointer.ToString()), payload, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error during CUDA Fourier Transform",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("CuFFT/audio")]
        public async Task<ActionResult<CudaFourierResponse?>> RunCudaFourierOnAudioAsync([FromBody] string audioIdOrName, [FromQuery] int chunkSize = 0, [FromQuery] float overlap = 0.5f, [FromQuery] bool autoPullResult = false, [FromQuery] bool keepDataOrBuffer = false)
        {
            if (!this.cuda.Online || this.cuda.Fourier == null)
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA Service Offline",
                    Detail = "The CUDA service is currently offline. Please initialize a device.",
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

                CudaMem? audioMem = null;
                // Optionally push
                if ((audio.Pointer <= 0 || audio.Pointer == IntPtr.Zero) && audio.Data.LongLength > 0)
                {
                    audioMem = chunkSize > 0 ? await this.cuda.PushChunksAsync(audio.GetChunks(chunkSize, overlap, keepDataOrBuffer)) : await this.cuda.PushDataAsync(audio.Data);
                    if (!keepDataOrBuffer)
                    {
                        audio.Data = [];
                    }
                }
                else
                {
                    audioMem = this.cuda[(nint) audio.Pointer];
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

                var inputMemInfo = CudaInfosBuilder.BuildCudaMemoryInfo(this.cuda, audioMem.IndexPointer.ToString());
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

                var resultMemPtr = audioMem.ElementType == typeof(float2) ? await this.cuda.Fourier.PerformIfftAsync(audioMem.IndexPointer, keepDataOrBuffer) : await this.cuda.Fourier.PerformFftAsync(audioMem.IndexPointer, keepDataOrBuffer);
                var resultMem = this.cuda[resultMemPtr];
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

                ICudaPayload? payload = null;
                if (autoPullResult)
                {
                    if (resultMem.ElementType == typeof(float))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.cuda.PullDataAsync<float>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.cuda.PullChunksAsync<float>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
                    }
                    else if (resultMem.ElementType == typeof(double))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.cuda.PullDataAsync<double>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.cuda.PullChunksAsync<double>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
                    }
                    else if (resultMem.ElementType == typeof(float2))
                    {
                        payload = resultMem.Count <= 1 ? await DataSerializer.SerializeAsync(await this.cuda.PullDataAsync<float2>(resultMem.IndexPointer, keepDataOrBuffer) ?? []) : await DataSerializer.SerializeAsync(await this.cuda.PullChunksAsync<float2>(resultMem.IndexPointer, keepDataOrBuffer) ?? []);
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

                var response = CudaResponsesBuilder.BuildCudaFourierResponse(inputMemInfo, CudaInfosBuilder.BuildCudaMemoryInfo(this.cuda, resultMem.IndexPointer.ToString()), payload, (int) (DateTime.Now - started).TotalMilliseconds);
                return this.Ok(response);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error during CUDA Fourier Transform on Audio",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }


    }
}
