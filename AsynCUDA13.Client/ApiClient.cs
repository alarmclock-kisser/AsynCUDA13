using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text.Json;

namespace AsynCUDA13.Client
{
    public class ApiClient
    {
        private readonly InternalClient internalClient;
        private readonly HttpClient httpClient;
        private JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        public string BaseUrl { get; }


        public ApiClient(string baseUrl)
        {
            this.BaseUrl = baseUrl;
            this.httpClient = new HttpClient()
            {
                BaseAddress = new Uri(this.BaseUrl)
            };
            this.internalClient = new(baseUrl, this.httpClient);
        }


        // LogController
        public async Task<string[]> GetLogListAsync(int nLastMax = 0)
        {
            try
            {
                var logLines = await this.internalClient.LogLinesAsync(nLastMax);
                return logLines.ToArray();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return [];
            }
        }

        public async Task ClearLogAsync()
        {
            try
            {
                await this.internalClient.LogClearAsync();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
            }
        }

        public async Task CommentLogAsync(string comment)
        {
            try
            {
                await this.internalClient.LogCommentAsync(comment);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
            }
        }


        // CudaDeviceController
        public async Task<CudaDeviceInfo[]> GetCudaDevicesAsync()
        {
            try
            {
                var devices = await this.internalClient.DevicesAsync();
                return devices.ToArray();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return [];
            }
        }

        public async Task<CudaDeviceInfo?> GetCudaDeviceAsync(int deviceId)
        {
            try
            {
                var device = await this.internalClient.DeviceAsync(deviceId);
                return device;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }


        // CudaContextController
        public async Task<CudaContextInfo?> GetCudaContextInfo()
        {
            try
            {
                var contextInfo = await this.internalClient.StatusAsync();
                return contextInfo;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaInitializeResponse?> InitializeCudaAsync(int deviceId = 0, string deviceName = "")
        {
            var request = new CudaInitializeRequest()
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                ForceReinitialize = true
            };

            try
            {
                var response = await this.internalClient.InitializeAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaDisposeResponse?> DisposeCudaAsync(bool freeBuffers = false)
        {
            var request = new CudaDisposeRequest()
            {
                FreeAllBuffersBeforeDispose = freeBuffers
            };

            try
            {
                var response = await this.internalClient.DisposeContextAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }


        // CudaMemoryController
        public async Task<CudaMemInfo[]> GetMemoryListAsync()
        {
            try
            {
                var memoryList = await this.internalClient.MemoryListAsync();
                return memoryList.ToArray();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return [];
            }
        }

        public async Task<CudaMemInfo?> GetMemoryInfoAsync(string indexPointerOrId)
        {
            try
            {
                var memoryInfo = await this.internalClient.MemoryInfoAsync(indexPointerOrId);
                return memoryInfo;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<string?> FreeMemoryAsync(string indexPointerOrId)
        {
            try
            {
                var freed = await this.internalClient.MemoryFreeAsync(indexPointerOrId);
                return freed;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<string?> FreeAllMemoryAsync()
        {
            try
            {
                var freed = await this.internalClient.FreeAllMemoryAsync();
                return freed;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaPushResponse?> PushAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            ICudaPayload? payload = null;

            var audioData = await this.internalClient.AudioDataAsync(assetIdOrName, chunkSize, overlap, keepData);
            ImageData? imageData = null;
            if (audioData == null)
            {
                imageData = await this.internalClient.ImageDataAsync(assetIdOrName, format, keepData);
                payload = await DataSerializer.SerializeAsync(imageData?.Base64Data ?? "", true);
            }
            else
            {
                if (audioData.AudioDataFloats?.LongLength > 0)
                {
                    payload = await DataSerializer.SerializeAsync(audioData.AudioDataFloats, true);
                }
                else if (audioData.AudioDataFloatChunks?.LongLength > 0)
                {
                    payload = await DataSerializer.SerializeAsync(audioData.AudioDataFloatChunks, true);
                }
            }

            if (payload == null)
            {
                await StaticLogger.LogAsync($"Failed to serialize data for asset '{assetIdOrName}'.");
                return null;
            }

            var request = new CudaPushRequest()
            {
                Payload = payload,
                AsyncCall = true,
            };

            try
            {

                var response = await this.internalClient.PushAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaPullResponse?> PullAsync(string indexPointerOrId, bool freeBuffer = true)
        {
            var request = new CudaPullRequest()
            {
                IndexPointerOrId = indexPointerOrId,
                AsyncCall = true,
                FreeAfterPull = freeBuffer
            };

            try
            {
                var response = await this.internalClient.PullAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }


        // Cuda Fourier Controller
        public async Task<CudaFourierResponse?> PerformFourierTransformAsync(string indexPointerOrId, bool? inverse = null, bool keepBuffer = false)
        {
            var memInfo = await this.GetMemoryInfoAsync(indexPointerOrId);
            if (memInfo == null)
            {
                await StaticLogger.LogAsync($"Memory info not found for index pointer or ID: {indexPointerOrId}");
                return null;
            }

            var request = new CudaFourierRequest()
            {
                MemoryInfo = memInfo,
                Inverse = inverse,
                KeepInputBuffer = keepBuffer,
                AsyncCall = true
            };
            try
            {
                var response = await this.internalClient.CuFFTAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaFourierResponse?> PerformFourierOnAudioAsync(string audioNameOrId, int chunkSize = 8192, float overlap = 0.5f, bool autoPull = false, bool keepDataOrBuffer = false)
        {
            try
            {
                return await this.internalClient.AudioAsync(chunkSize, overlap, autoPull, keepDataOrBuffer, audioNameOrId);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }


        // CudaKernelController
        public async Task<CudaKernelInfo[]> GetKernelsAsync(bool filterCompiled = true)
        {
            try
            {
                var kernels = await this.internalClient.KernelsAsync(filterCompiled);
                return kernels.ToArray();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return [];
            }
        }

        public async Task<CudaCompileResponse?> CompileKernelAsync(string kernelCode, bool silent = false)
        {
            string? kernelName = DataParser.ExtractKernelName(kernelCode);
            if (string.IsNullOrEmpty(kernelName))
            {
                await StaticLogger.LogAsync("Failed to extract kernel name from the provided kernel code.");
                return null;
            }

            var request = new CudaCompileRequest()
            {
                KernelName = kernelName,
                KernelSource = kernelCode,
                AsyncCall = true,
                Silent = silent
            };

            try
            {
                var response = await this.internalClient.CompileAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaExecuteResponse?> ExecuteGenericKernelAsync(string kernelName, string[]? args = null, bool unloadAfterExecute = false)
        {
            CudaKernelInfo? kernelInfo = (await this.internalClient.KernelsAsync(true)).FirstOrDefault(k => k.FunctionName.Equals(kernelName, StringComparison.OrdinalIgnoreCase));
            if (kernelInfo == null)
            {
                await StaticLogger.LogAsync($"Kernel '{kernelName}' not found or not compiled.");
                return null;
            }

            var request = new CudaExecuteRequest()
            {
                KernelInfo = kernelInfo,
                ArgumentValues = args ?? [],
                UnloadAfterExecution = unloadAfterExecute,
                AsyncCall = true
            };

            try
            {
                var response = await this.internalClient.ExecuteGenericAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<CudaExecuteResponse?> ExecuteLinearKernelAsync(string kernelName, string[]? args = null, bool unloadAfterExecute = false)
        {
            CudaKernelInfo? kernelInfo = (await this.internalClient.KernelsAsync(true)).FirstOrDefault(k => k.FunctionName.Equals(kernelName, StringComparison.OrdinalIgnoreCase));
            if (kernelInfo == null)
            {
                await StaticLogger.LogAsync($"Kernel '{kernelName}' not found or not compiled.");
                return null;
            }

            var request = new CudaExecuteRequest()
            {
                KernelInfo = kernelInfo,
                ArgumentValues = args ?? [],
                UnloadAfterExecution = unloadAfterExecute,
                AsyncCall = true
            };

            try
            {
                var response = await this.internalClient.ExecuteLinearAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }


        // MediaController
        public async Task<IMediaInfo?> UploadMediaAsync(IFormFile file)
        {
            try
            {
                var fp = new FileParameter(file.OpenReadStream(), file.FileName, file.ContentType);
                var response = await this.internalClient.UploadMediaAsync(fp);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<FileResponse?> DownloadMediaAsync(string idOrName, string format = "png", float normalizeAudio = 1.0f, bool pullIfRequired = true, bool keepBufferWhenPulled = false)
        {
            try
            {
                return await this.internalClient.DownloadMediaAsync(idOrName, format, normalizeAudio, pullIfRequired, keepBufferWhenPulled);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<ImageInfo[]> GetImagesAsync()
        {
            try
            {
                var images = await this.internalClient.ImagesAsync();
                return images.ToArray();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return [];
            }
        }

        public async Task<AudioInfo[]> GetAudiosAsync()
        {
            try
            {
                var audios = await this.internalClient.AudiosAsync();
                return audios.ToArray();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return [];
            }
        }

        public async Task<ImageData?> GetImageDataAsync(string idOrName, string format = "png", bool keepData = true)
        {
            try
            {
                var imageData = await this.internalClient.ImageDataAsync(idOrName, format, keepData);
                return imageData;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<AudioData?> GetAudioDataAsync(string idOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            try
            {
                var audioData = await this.internalClient.AudioDataAsync(idOrName, chunkSize, overlap, keepData);
                return audioData;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<ImageData?> GetImagePreviewAsync(string idOrName, int maxDimensions = 256)
        {
            try
            {
                return await this.internalClient.ImagePreviewAsync(idOrName, maxDimensions);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<ImageData?> GetAudioWaveformAsync(string idOrName, int width = 512, int height = 128)
        {
            try
            {
                return await this.internalClient.AudioPreviewAsync(idOrName, width, height);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
        }

        public async Task<bool> DeleteMediaAsync(string idOrName)
        {
            try
            {
                await this.internalClient.DeleteAsync(idOrName);
                return true;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return false;
            }
        }

        public async Task ClearAllMediaAsync()
        {
            try
            {
                await this.internalClient.ClearAllAsync();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
            }
        }

    }
}
