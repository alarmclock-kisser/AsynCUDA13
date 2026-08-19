using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace AsynCUDA13.Client
{
    public class ApiClient
    {
        private readonly InternalClient internalClient;
        private readonly HttpClient httpClient;
        private readonly JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly SemaphoreSlim signalRConnectionLock = new(1, 1);
        private HubConnection? _hubConnection;

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


        public event Action<DateTime, string>? LogWritten;

                public bool IsSignalRConnected => _hubConnection?.State == HubConnectionState.Connected;


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


        public async Task StartSignalRConnectionAsync(CancellationToken cancellationToken = default)
        {
            await this.signalRConnectionLock.WaitAsync(cancellationToken);
            try
            {
                if (this._hubConnection?.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                {
                    return;
                }

                if (this._hubConnection is not null)
                {
                    await this._hubConnection.DisposeAsync();
                }

                var hubUrl = $"{this.BaseUrl.Replace("/api", "")}/logHub";
                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect()
                    .Build();

                hubConnection.On<DateTime, string>("LogWritten", (timestamp, line) =>
                {
                    this.LogWritten?.Invoke(timestamp, line);
                });

                await hubConnection.StartAsync(cancellationToken);
                this._hubConnection = hubConnection;
            }
            finally
            {
                this.signalRConnectionLock.Release();
            }
        }


        public async Task StopSignalRConnectionAsync()
        {
            await this.signalRConnectionLock.WaitAsync();
            try
            {
                if (this._hubConnection is not null)
                {
                    await this._hubConnection.StopAsync();
                    await this._hubConnection.DisposeAsync();
                    this._hubConnection = null;
                }
            }
            finally
            {
                this.signalRConnectionLock.Release();
            }
        }


        // LogController
        public async Task<string[]> GetLogListAsync(bool frontendLog = false, int nLastMax = 0)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var logLines = frontendLog ? StaticLogger.LogEntries.OrderBy(e => e.Key).TakeLast(nLastMax <= 0 ? StaticLogger.LogEntries.Count : nLastMax).Select(e => e.Value) : await this.internalClient.LogLinesAsync(nLastMax);
                count = logLines.Count();
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetLogListAsync() (elapsed={DateTime.Now - started}, count={count})");
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
            int count = 0;
            try
            {
                var devices = await this.internalClient.DevicesAsync();
                count = devices.Count;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetCudaDevicesAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<CudaDeviceInfo?> GetCudaDeviceAsync(int deviceId)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var device = await this.internalClient.DeviceAsync(deviceId);
                hasValue = device != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetCudaDeviceAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // CudaContextController
        public async Task<CudaContextInfo?> GetCudaContextInfoAsync()
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var contextInfo = await this.internalClient.StatusAsync();
                hasValue = contextInfo != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetCudaContextInfoAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.InitializeAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : InitializeCudaAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.DisposeContextAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : DisposeCudaAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // CudaMemoryController
        public async Task<CudaMemInfo[]> GetMemoryListAsync()
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var memoryList = await this.internalClient.MemoryListAsync();
                count = memoryList.Count;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetMemoryListAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<CudaMemInfo?> GetMemoryInfoAsync(string indexPointerOrId)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var memoryInfo = await this.internalClient.MemoryInfoAsync(indexPointerOrId);
                hasValue = memoryInfo != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetMemoryInfoAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<string?> FreeMemoryAsync(string indexPointerOrId)
        {
            DateTime started = DateTime.Now;
            string value = string.Empty;
            try
            {
                var freed = await this.internalClient.MemoryFreeAsync(indexPointerOrId);
                value = freed ?? string.Empty;
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
                    await StaticLogger.LogAsync($"[ApiClient] : FreeMemoryAsync() (elapsed={DateTime.Now - started}), {(string.IsNullOrEmpty(value) ? "returned NULL" : $"returned '{value}'")}");
                }
            }
        }

        public async Task<string?> FreeAllMemoryAsync()
        {
            DateTime started = DateTime.Now;
            string value = string.Empty;
            try
            {
                var freed = await this.internalClient.FreeAllMemoryAsync();
                value = freed ?? string.Empty;
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
                    await StaticLogger.LogAsync($"[ApiClient] : FreeAllMemoryAsync() (elapsed={DateTime.Now - started}), {(string.IsNullOrEmpty(value) ? "returned NULL" : $"returned '{value}'")}");
                }
            }
        }

        public async Task<CudaPushResponse?> PushAsync(string assetIdOrName, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
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
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : PushAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.PullAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : PullAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.CuFFTAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : PerformFourierTransformAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<CudaFourierResponse?> PerformFourierOnAudioAsync(string audioNameOrId, int chunkSize = 8192, float overlap = 0.5f, bool autoPull = false, bool keepDataOrBuffer = false)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.AudioAsync(chunkSize, overlap, autoPull, keepDataOrBuffer, audioNameOrId);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : PerformFourierOnAudioAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // CudaKernelController
        public async Task<CudaKernelInfo[]> GetKernelsAsync(bool filterCompiled = true)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var kernels = await this.internalClient.KernelsAsync(filterCompiled);
                count = kernels.Count;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetKernelsAsync() (elapsed={DateTime.Now - started}, count={count})");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.CompileAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : CompileKernelAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.ExecuteGenericAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : ExecuteGenericKernelAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.ExecuteLinearAsync(request);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : ExecuteLinearKernelAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // MediaController
        public async Task<string?> UploadMediaAsync(FileParameter fileParameter)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.UploadMediaAsync(fileParameter);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : UploadMediaAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<FileResponse?> DownloadMediaAsync(string idOrName, string format = "png", float normalizeAudio = 1.0f, bool pullIfRequired = true, bool keepBufferWhenPulled = false)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var response = await this.internalClient.DownloadMediaAsync(idOrName, format, normalizeAudio, pullIfRequired, keepBufferWhenPulled);
                hasValue = response != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : DownloadMediaAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<ImageInfo[]> GetImagesAsync()
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var images = await this.internalClient.ImagesAsync();
                count = images.Count();
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetImagesAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<AudioInfo[]> GetAudiosAsync()
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var audios = await this.internalClient.AudiosAsync();
                count = audios.Count();
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudiosAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<ImageData?> GetImageDataAsync(string idOrName, string format = "png", bool keepData = true)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var imageData = await this.internalClient.ImageDataAsync(idOrName, format, keepData);
                hasValue = imageData != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetImageDataAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<AudioData?> GetAudioDataAsync(string idOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var audioData = await this.internalClient.AudioDataAsync(idOrName, chunkSize, overlap, keepData);
                hasValue = audioData != null;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudioDataAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<ImageData?> GetImagePreviewAsync(string idOrName, int maxDimensions = 256)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var result = await this.internalClient.ImagePreviewAsync(idOrName, maxDimensions);
                hasValue = result != null;
                return result;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetImagePreviewAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<ImageData[]?> GetImagePreviewsAsync(string[] idsOrNames, int maxDimensions = 256)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                if (idsOrNames.Length <= 0)
                {
                    if (this.LogLevel >= LogLevel.Debug)
                    {
                        await StaticLogger.LogAsync("No image IDs or names provided for preview retrieval.");
                    }
                    return [];
                }

                ConcurrentDictionary<DateTime, ImageData> result = [];

                var tasks = idsOrNames.Select(async idOrName =>
                {
                    try
                    {
                        var imageData = await this.internalClient.ImagePreviewAsync(idOrName, maxDimensions);
                        if (imageData != null)
                        {
                            if (result.TryAdd(DateTime.Now, imageData))
                            {
                                count++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await StaticLogger.LogAsync(ex);
                    }
                });
                
                await Task.WhenAll(tasks);

                return result.OrderBy(e => e.Key).Select(e => e.Value).ToArray();
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetImagePreviewsAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<ImageData?> GetAudioWaveformAsync(string idOrName, int width = 512, int height = 128)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var result = await this.internalClient.AudioPreviewAsync(idOrName, width, height);
                hasValue = result != null;
                return result;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudioWaveformAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<ImageData[]?> GetAudioWaveformsAsync(string[] idsOrNames, int width = 512, int height = 128)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                if (idsOrNames.Length <= 0)
                {
                    if (this.LogLevel >= LogLevel.Debug)
                    {
                        await StaticLogger.LogAsync("No audio IDs or names provided for waveform retrieval.");
                    }
                    return [];
                }

                ConcurrentDictionary<DateTime, ImageData> result = [];
                var tasks = idsOrNames.Select(async idOrName =>
                {
                    try
                    {
                        var imageData = await this.internalClient.AudioPreviewAsync(idOrName, width, height);
                        if (imageData != null)
                        {
                            if (result.TryAdd(DateTime.Now, imageData))
                            {
                                count++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await StaticLogger.LogAsync(ex);
                    }
                });

                await Task.WhenAll(tasks);
                return result.OrderBy(e => e.Key).Select(e => e.Value).ToArray();
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudioWaveformsAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<bool> DeleteMediaAsync(string idOrName)
        {
            DateTime started = DateTime.Now;
            bool value = false;
            try
            {
                await this.internalClient.DeleteAsync(idOrName);
                value = true;
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
                    await StaticLogger.LogAsync($"[ApiClient] : DeleteMediaAsync() (elapsed={DateTime.Now - started}), {(value ? "returned TRUE" : "returned FALSE")}");
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
