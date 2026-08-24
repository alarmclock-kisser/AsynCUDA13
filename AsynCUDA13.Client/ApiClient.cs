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

        public bool IsSignalRConnected => this._hubConnection?.State == HubConnectionState.Connected;


        public ApiClient(string baseUrl, int logLevel = (int) LogLevel.Information)
        {
            this.BaseUrl = baseUrl;
            this.httpClient = new HttpClient()
            {
                BaseAddress = new Uri(this.BaseUrl),
                Timeout = TimeSpan.FromMinutes(10),
                MaxResponseContentBufferSize = int.MaxValue
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

        public async Task<CudaPushResponse?> PushAsync(string assetIdOrName, bool serverSided = true, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            DateTime started = DateTime.Now;
            CudaPushResponse? response = null;
            try
            {
                Guid? verifiedAssetId = await this.VerifyAssetIdExistsAsync(assetIdOrName);
                bool? isAudioAsset = await this.IsAssetAudioAsync(verifiedAssetId);
                if (isAudioAsset == null)
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Asset '{assetIdOrName}' does not exist or could not determine if it is an audio asset. PushAsync() aborted.");
                    }
                    return null;
                }

                if (serverSided)
                {
                    response = await this.internalClient.PushAssetAsync(assetIdOrName, chunkSize, overlap, keepData);
                }

                else
                {
                    ICudaPayload? payload = null;

                    if (isAudioAsset == false)
                    {
                        var imageData = await this.internalClient.ImageDataAsync(assetIdOrName, format, keepData);
                        payload = await DataSerializer.SerializeAsync(imageData?.Base64Data ?? "", true);
                    }
                    else
                    {
                        var audioData = await this.internalClient.AudioDataAsync(assetIdOrName, chunkSize, overlap, keepData);
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

                    response = await this.internalClient.PushAsync(request);
                }
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : PushAsync() (elapsed={DateTime.Now - started}), {(response != null ? "returned DTO" : "returned NULL")}");
                }
            }

            return response;
        }

        public async Task<CudaPullResponse?> PullAsync(string indexPointerOrId, bool serverSided = true, bool freeBuffer = true)
        {
            DateTime started = DateTime.Now;
            CudaPullResponse? response = null;
            try
            {


                if (serverSided)
                {
                    response = await this.internalClient.PullAssetAsync(indexPointerOrId, !freeBuffer);
                }
                else
                {
                    var request = new CudaPullRequest()
                    {
                        IndexPointerOrId = indexPointerOrId,
                        AsyncCall = true,
                        FreeAfterPull = freeBuffer
                    };
                    response = await this.internalClient.PullAsync(request);
                }
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
                    await StaticLogger.LogAsync($"[ApiClient] : PullAsync() (elapsed={DateTime.Now - started}), {(response != null ? "returned DTO" : "returned NULL")}");
                }
            }

            return response;
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

        public async Task<ImageInfo[]> GetImageInfosAsync(string? nameSearch = null)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var images = await this.internalClient.ImagesAsync();
                if (!string.IsNullOrEmpty(nameSearch))
                {
                    images = images.Where(i => i.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                count = images.Count();
                return images.ToArray();
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }
                return [];
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetImageInfosAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<AudioInfo[]> GetAudioInfosAsync(string? nameSearch = null)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var audios = await this.internalClient.AudiosAsync();
                if (!string.IsNullOrEmpty(nameSearch))
                {
                    audios = audios.Where(a => a.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                count = audios.Count();
                return audios.ToArray();
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }
                return [];
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAudioInfosAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<Guid[]> GetAllAssetIdsAsync(string? nameSearch = null, bool fromAudios = true, bool fromImages = true)
        {
            DateTime started = DateTime.Now;
            try
            {
                List<Guid> assetIds = [];
                if (fromAudios)
                {
                    assetIds.AddRange((await this.GetAudioInfosAsync(nameSearch)).Select(a => a.Id));
                }
                if (fromImages)
                {
                    assetIds.AddRange((await this.GetImageInfosAsync(nameSearch)).Select(i => i.Id));
                }

                return assetIds.Distinct().ToArray();
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAllAssetIdsAsync() (elapsed={DateTime.Now - started})");
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




        // Accessibility helpers


        public async Task<Guid?> GetAssetIdForIndexPointerAsync(string indexPointer)
        {
            DateTime started = DateTime.Now;

            try
            {
                CudaMemInfo? mem = await this.GetMemoryInfoAsync(indexPointer);
                if (mem == null || mem.Id.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find a CudaMem-obj allocated with IndexPointer={indexPointer}");
                    }
                    return null;
                }

                Guid? audioId = (await this.GetAudioInfosAsync()).FirstOrDefault(a => !string.IsNullOrEmpty(a.Pointer) && a.Pointer.Equals(mem.Id.ToString(), StringComparison.OrdinalIgnoreCase))?.Id;
                Guid? imageId = (await this.GetImageInfosAsync()).FirstOrDefault(i => !string.IsNullOrEmpty(i.Pointer) && i.Pointer.Equals(mem.Id.ToString(), StringComparison.OrdinalIgnoreCase))?.Id;

                return audioId ?? imageId;
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAssetIdForIndexPointer() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<Guid[]> GetAssetIdsForIndexPointersAsync(IEnumerable<string>? indexPointers = null)
        {
            DateTime started = DateTime.Now;

            try
            {
                Guid[] guids = (indexPointers ?? (await this.GetMemoryListAsync()).Select(i => i.Id)).Select(ip => Guid.TryParse(ip, out var guid) ? guid : Guid.Empty).Where(g => g != Guid.Empty).ToArray();

                List<Guid> assetIds = [];
                assetIds.AddRange((await this.GetAudioInfosAsync()).Where(a => guids.Contains(a.Id)).Select(a => a.Id));
                assetIds.AddRange((await this.GetImageInfosAsync()).Where(i => guids.Contains(i.Id)).Select(i => i.Id));

                return assetIds.Distinct().ToArray();
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetAssetIdsForIndexPointers() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<Guid?> VerifyAssetIdExistsAsync(Guid id)
        {
            DateTime started = DateTime.Now;

            try
            {
                Guid? audioId = (await this.GetAudioInfosAsync()).FirstOrDefault(a => a.Id.Equals(id))?.Id;
                Guid? imageId = (await this.GetImageInfosAsync()).FirstOrDefault(i => i.Id.Equals(id))?.Id;

                return audioId ?? imageId;
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : VerifyAssetIdExistsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<Guid?> VerifyAssetIdExistsAsync(string idOrName)
        {
            DateTime started = DateTime.Now;
            try
            {
                Guid? guid = Guid.TryParse(idOrName, out var parsedGuid) ? parsedGuid : null;
                if (guid.HasValue)
                {
                    return await this.VerifyAssetIdExistsAsync(guid.Value);
                }

                guid = (await this.GetAudioInfosAsync()).FirstOrDefault(a => a.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase))?.Id ?? (await this.GetImageInfosAsync()).FirstOrDefault(i => i.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase))?.Id;
                return guid;
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : VerifyAssetIdExistsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<Guid[]> VerifyAssetIdsExistsAsync(IEnumerable<Guid> ids)
        {
            DateTime started = DateTime.Now;

            try
            {
                var audioIds = (await this.GetAudioInfosAsync()).Where(a => ids.Contains(a.Id)).Select(a => a.Id);
                var imageIds = (await this.GetImageInfosAsync()).Where(i => ids.Contains(i.Id)).Select(i => i.Id);

                return (audioIds.Concat(imageIds)).ToArray();
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : VerifyAssetIdsExistsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<Guid[]> VerifyAssetIdsExistsAsync(IEnumerable<string> idsOrNames)
        {
            DateTime started = DateTime.Now;
            try
            {
                var audioIds = (await this.GetAudioInfosAsync()).Where(a => idsOrNames.Contains(a.Id.ToString()) || idsOrNames.Contains(a.Name)).Select(a => a.Id);
                var imageIds = (await this.GetImageInfosAsync()).Where(i => idsOrNames.Contains(i.Id.ToString()) || idsOrNames.Contains(i.Name)).Select(i => i.Id);
                return (audioIds.Concat(imageIds)).ToArray();
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : VerifyAssetIdsExistsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string?> GetIndexPointerForAssetIdAsync(Guid assetId)
        {
            DateTime started = DateTime.Now;
            try
            {
                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(assetId);
                if (verifiedId != null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId}");
                    }
                    return null;
                }

                string? assetPtr = (await this.GetAudioInfosAsync()).FirstOrDefault(a => a.Id.Equals(verifiedId))?.Pointer ?? (await this.GetImageInfosAsync()).FirstOrDefault(a => a.Id.Equals(verifiedId))?.Pointer;
                if (string.IsNullOrEmpty(assetPtr))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId} that has a Pointer");
                    }
                    return null;
                }

                string? verifiedPtr = (await this.GetMemoryInfoAsync(assetPtr))?.IndexPointer;
                if (string.IsNullOrEmpty(verifiedPtr))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId} that has a Pointer which can be found in any CudaMemInfo-obj allocated");
                    }
                }

                return verifiedPtr;
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetIndexPointerForAssetIdAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string?> GetIndexPointerForAssetIdAsync(string idOrName)
        {
            DateTime started = DateTime.Now;
            try
            {
                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(idOrName);
                if (verifiedId == null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id or Name={idOrName}");
                    }
                    return null;
                }
                return await this.GetIndexPointerForAssetIdAsync(verifiedId.Value);
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetIndexPointerForAssetIdAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string[]> GetIndexPointersForAssetIdsAsync(IEnumerable<Guid> assetIds)
        {
            DateTime started = DateTime.Now;
            try
            {
                var verifiedIds = await this.VerifyAssetIdsExistsAsync(assetIds);
                if (verifiedIds?.Any(g => g.Equals(Guid.Empty)) == true || verifiedIds?.Length != assetIds.Count())
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"One or more provided assetIds do not exist as an Asset (ImageObj/AudioObj) or equal Guid.Empty, which is invalid: Ids=[{string.Join(" ,", assetIds.Where(i => !verifiedIds.Contains(i)))}]");
                    }
                    return [];
                }

                var assetPtrs = (await this.GetAudioInfosAsync()).Where(a => verifiedIds.Contains(a.Id)).Select(i => i.Pointer ?? "0").Concat((await this.GetImageInfosAsync()).Where(a => verifiedIds.Contains(a.Id)).Select(i => i.Pointer ?? "0"));
                if (assetPtrs.Count() != verifiedIds.Count())
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Lengths mismatching for verifiedIds and assetPtrs ({verifiedIds.Length} != {assetPtrs.Count()})");
                        return [];
                    }
                    return [];
                }

                var verifiedPtrs = (await this.GetMemoryListAsync()).Where(m => assetPtrs.Contains(m.IndexPointer)).Select(i => i.IndexPointer);
                if (verifiedPtrs.Count() != assetPtrs.Count())
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Lengths mismatching for verifiedPtrs and assetPtrs ({verifiedPtrs.Count()} != {assetPtrs.Count()})");
                        return [];
                    }
                    return [];
                }

                return verifiedPtrs.ToArray();
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetIndexPointesrForAssetIdsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string[]> GetIndexPointersForAssetIdsAsync(IEnumerable<string> idsOrNames)
        {
            DateTime started = DateTime.Now;
            try
            {
                var verifiedIds = await this.VerifyAssetIdsExistsAsync(idsOrNames);
                if (verifiedIds?.Any(g => g.Equals(Guid.Empty)) == true || verifiedIds?.Length != idsOrNames.Count())
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"One or more provided idsOrNames do not exist as an Asset (ImageObj/AudioObj) or equal Guid.Empty, which is invalid: Ids=[{string.Join(" ,", idsOrNames.Select(i => Guid.TryParse(i, out var g) ? g : (Guid?)null)?.Where(v => v.HasValue && !verifiedIds.Contains(v.Value)) ?? [])}]");
                    }
                    return [];
                }
                return await this.GetIndexPointersForAssetIdsAsync(verifiedIds);
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return [];
                }

                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : GetIndexPointersForAssetIdsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<bool?> IsAssetAudioAsync(Guid? assetId)
        {
            DateTime started = DateTime.Now;
            try
            {
                if (assetId == null || assetId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Null or empty assetId provided: {assetId}");
                    }
                    return null;
                }

                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(assetId.Value);
                if (verifiedId == null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId}");
                    }
                    return null;
                }
                bool isAudio = (await this.GetAudioInfosAsync()).Any(a => a.IdMatch(verifiedId.Value));
                return isAudio;
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : IsAssetAudioAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<bool?> IsAssetAudioAsync(string? assetIdOrName)
        {
            DateTime started = DateTime.Now;
            try
            {
                if (string.IsNullOrEmpty(assetIdOrName))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Null or empty assetIdOrName provided: {assetIdOrName}");
                    }
                    return null;
                }

                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(assetIdOrName);
                if (verifiedId == null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await StaticLogger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id or Name={assetIdOrName}");
                    }
                    return null;
                }

                bool isAudio = (await this.GetAudioInfosAsync()).Any(a => a.IdMatch(verifiedId.Value));
                return isAudio;
            }
            catch (ApiException apiEx)
            {
                if (apiEx.StatusCode == 404 || apiEx.StatusCode == 204)
                {
                    return null;
                }
                throw;
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
                    await StaticLogger.LogAsync($"[ApiClient] : IsAssetAudioAsync() (elapsed={DateTime.Now - started})");
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
