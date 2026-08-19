using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using System.Net;
using System.Text.Json;

namespace AsynCUDA13.Client
{
    public class ApiClient
    {
        private readonly InternalClient internalClient;
        private readonly HttpClient httpClient;
        private readonly JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        private LogLevel _logLevel = LogLevel.Information;
        public LogLevel LogLevel
        {
            get => this._logLevel;
            set
            {
                StaticLogger.Silent = value == LogLevel.Silent;
                this._logLevel = value;
            }
        }
        public string BaseUrl { get; }


        public ApiClient(string baseUrl, int logLevel = (int) LogLevel.Information)
        {
            this.BaseUrl = baseUrl;
            this.httpClient = new HttpClient()
            {
                BaseAddress = new Uri(this.BaseUrl)
            };
            this.internalClient = new(baseUrl, this.httpClient);
            this.LogLevel = (LogLevel) logLevel;

            if ((int) this.LogLevel >= 5)
            {
                StaticLogger.Log($"ApiClient initialized with log level: {this.LogLevel}, URL='{this.BaseUrl}'");
            }
        }


        // LogController
        public async Task<string[]> GetLogListAsync(int nLastMax = 0)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetLogListAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task ClearLogAsync()
        {
            DateTime started = DateTime.Now;
            try
            {
                await this.internalClient.LogClearAsync();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : ClearLogAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task CommentLogAsync(DateTime? capturedAt = null, string comment = "<!!!>")
        {
            DateTime started = DateTime.Now;
            try
            {
                StaticLogger.AddComment(capturedAt, comment);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : CommentLogAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<IFormFile?> DownloadLogFileAsync(int? previousIndex = null)
        {
            DateTime started = DateTime.Now;
            try
            {
                string logFileName = StaticLogger.GetPreviousLogFilePath(previousIndex ?? 0) ?? throw new FileNotFoundException($"Log file not found for the specified index {previousIndex}.");

                using var sr = new StreamReader(logFileName);
                var fileStream = sr.BaseStream;
                var headers = new Dictionary<string, IEnumerable<string>>()
                {
                    { "Content-Disposition", new[] { $"attachment; filename=\"{Path.GetFileName(logFileName)}\"" } },
                    { "Content-Type", new[] { "text/plain" } }
                };

                var formFile = new FormFile(fileStream, 0, fileStream.Length, "logFile", Path.GetFileName(logFileName))
                {
                    Headers = headers as IHeaderDictionary,
                    ContentType = "text/plain"
                };

                return formFile;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : DownloadLogFileAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }


        // CudaDeviceController
        public async Task<CudaDeviceInfo[]> GetCudaDevicesAsync()
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetCudaDevicesAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<CudaDeviceInfo?> GetCudaDeviceAsync(int deviceId)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetCudaDeviceAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }


        // CudaContextController
        public async Task<CudaContextInfo?> GetCudaContextInfoAsync()
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetCudaContextInfoAsync() (elapsed={DateTime.Now - started})");
                }
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

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : InitializeCudaAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<CudaDisposeResponse?> DisposeCudaAsync(bool freeBuffers = false)
        {
            var request = new CudaDisposeRequest()
            {
                FreeAllBuffersBeforeDispose = freeBuffers
            };

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : DisposeCudaAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }


        // CudaMemoryController
        public async Task<CudaMemInfo[]> GetMemoryListAsync()
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetMemoryListAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<CudaMemInfo?> GetMemoryInfoAsync(string indexPointerOrId)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetMemoryInfoAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string?> FreeMemoryAsync(string indexPointerOrId)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : FreeMemoryAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string?> FreeAllMemoryAsync()
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : FreeAllMemoryAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<CudaPushResponse?> PushAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            DateTime started = DateTime.Now;
            try
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

                var response = await this.internalClient.PushAsync(request);
                return response;
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : PushAsync() (elapsed={DateTime.Now - started})");
                }
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

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : PullAsync() (elapsed={DateTime.Now - started})");
                }
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

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : PerformFourierTransformAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<CudaFourierResponse?> PerformFourierOnAudioAsync(string audioNameOrId, int chunkSize = 8192, float overlap = 0.5f, bool autoPull = false, bool keepDataOrBuffer = false)
        {
            DateTime started = DateTime.Now;
            try
            {
                return await this.internalClient.AudioAsync(chunkSize, overlap, autoPull, keepDataOrBuffer, audioNameOrId);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : PerformFourierOnAudioAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }


        // CudaKernelController
        public async Task<CudaKernelInfo[]> GetKernelsAsync(bool filterCompiled = true)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetKernelsAsync() (elapsed={DateTime.Now - started})");
                }
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

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : CompileKernelAsync() (elapsed={DateTime.Now - started})");
                }
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

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : ExecuteGenericKernelAsync() (elapsed={DateTime.Now - started})");
                }
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

            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : ExecuteLinearKernelAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }


        // MediaController
        public async Task<IMediaInfo?> UploadMediaAsync(IFormFile file)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : UploadMediaAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<FileResponse?> DownloadMediaAsync(string idOrName, string format = "png", float normalizeAudio = 1.0f, bool pullIfRequired = true, bool keepBufferWhenPulled = false)
        {
            DateTime started = DateTime.Now;
            try
            {
                return await this.internalClient.DownloadMediaAsync(idOrName, format, normalizeAudio, pullIfRequired, keepBufferWhenPulled);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : DownloadMediaAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<ImageInfo[]> GetImagesAsync()
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetImagesAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<AudioInfo[]> GetAudiosAsync()
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudiosAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<ImageData?> GetImageDataAsync(string idOrName, string format = "png", bool keepData = true)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetImageDataAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<AudioData?> GetAudioDataAsync(string idOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudioDataAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<ImageData?> GetImagePreviewAsync(string idOrName, int maxDimensions = 256)
        {
            DateTime started = DateTime.Now;
            try
            {
                return await this.internalClient.ImagePreviewAsync(idOrName, maxDimensions);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetImagePreviewAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<ImageData?> GetAudioWaveformAsync(string idOrName, int width = 512, int height = 128)
        {
            DateTime started = DateTime.Now;
            try
            {
                return await this.internalClient.AudioPreviewAsync(idOrName, width, height);
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudioWaveformAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<bool> DeleteMediaAsync(string idOrName)
        {
            DateTime started = DateTime.Now;
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
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : DeleteMediaAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task ClearAllMediaAsync()
        {
            DateTime started = DateTime.Now;
            try
            {
                await this.internalClient.ClearAllAsync();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync(ex);
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await StaticLogger.LogAsync($"[ApiClient] : ClearAllMediaAsync() (elapsed={DateTime.Now - started})");
                }
            }

        }

    }
    public enum LogLevel
    {
        /// <summary>
        /// No logging will be performed.
        /// </summary>
        Silent = 0,

        /// <summary>
        /// Logs detailed information, typically of interest only when diagnosing problems.
        /// </summary>
        Trace = 6,

        /// <summary>
        /// Logs information useful for debugging.
        /// </summary>
        Debug = 5,

        /// <summary>
        /// Logs informational messages that highlight the progress of the application.
        /// </summary>
        Information = 4,

        /// <summary>
        /// Logs potentially harmful situations.
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Logs error events that might still allow the application to continue running.
        /// </summary>
        Error = 2,

        /// <summary>
        /// Logs very severe error events that will presumably lead the application to abort.
        /// </summary>
        Critical = 1
    }
}
