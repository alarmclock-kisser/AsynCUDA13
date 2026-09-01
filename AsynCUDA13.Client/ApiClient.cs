using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Client;
using AsynCUDA13.Shared.Localization;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Linq;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Utils;

namespace AsynCUDA13.Client
{
    public class ApiClient
    {
        private readonly IRollingFileMemoryLogger logger;
        private readonly InternalClient internalClient;
        private readonly HttpClient httpClient;
        private readonly JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly SemaphoreSlim signalRConnectionLock = new(1, 1);
        private HubConnection? _hubConnection;
        public readonly LanguageService L;


        private LogLevel _logLevel = LogLevel.Information;
        public LogLevel LogLevel
        {
            get => this._logLevel;
            set
            {
                this.logger.Settings.Silent = value == LogLevel.Silent;
                this._logLevel = value;
            }
        }
        public string BaseUrl { get; }


        public string BackendType { get; private set; } = "N/A";
        public bool? IsCudaAvailable { get; private set; } = null;


        public event Action<DateTime, string>? LogWritten;
        public bool IsSignalRConnected => this._hubConnection?.State == HubConnectionState.Connected;


        public bool Initialized { get; private set; }
        public ApiClient(ApiClientConfiguration configuration, LanguageService languageService, IRollingFileMemoryLogger logger, HttpClient? httpClient = null)
        {
            this.logger = logger;
            this.BaseUrl = configuration.ApiBaseUrl;
            this.L = languageService;
            this.httpClient = httpClient ?? new HttpClient();
            this.httpClient.BaseAddress ??= new Uri(this.BaseUrl);
            this.httpClient.Timeout = TimeSpan.FromMinutes(10);
            this.httpClient.MaxResponseContentBufferSize = int.MaxValue;
            this.internalClient = new(this.BaseUrl, this.httpClient);
            this.LogLevel = (LogLevel) configuration.LogLevel;

            if ((int) this.LogLevel >= 5)
            {
                this.logger.Log($"ApiClient created with log level: {this.LogLevel}, URL='{this.BaseUrl}'");
            }
        }
        public async Task InitializeAsync()
        {
            if (this.Initialized)
            {
                return;
            }

            this.BackendType = await this.internalClient.BackendAsync();
            this.IsCudaAvailable = this.BackendType.Equals("CUDA", StringComparison.OrdinalIgnoreCase) ? CudaAvailabilityTester.IsCudaAvailable() : null;
            this.L.Runtime = this.BackendType;
            this.Initialized = true;

            await this.logger.LogAsync($"ApiClient initialized. BackendType='{this.BackendType}', IsCudaAvailable={this.IsCudaAvailable}");
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
                var frontendLogLines = this.logger.GetLogLines();
                var logLines = frontendLog
                    ? frontendLogLines.TakeLast(nLastMax <= 0 ? frontendLogLines.Count : nLastMax)
                    : await this.internalClient.LogLinesAsync(nLastMax);
                count = logLines.Count();
                return logLines.ToArray();
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetLogListAsync() (elapsed={DateTime.Now - started}, count={count})");
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
                await this.logger.LogAsync(ex);
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : ClearLogAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task CommentLogAsync(DateTime? capturedAt = null, string comment = "<!!!>")
        {
            DateTime started = DateTime.Now;
            try
            {
                this.logger.AddComment(capturedAt, comment: comment);
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : CommentLogAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<IFormFile?> DownloadLogFileAsync(int? previousIndex = null)
        {
            DateTime started = DateTime.Now;
            try
            {
                string logFileName = this.logger.GetPreviousLogFilePath(previousIndex ?? 0) ?? throw new FileNotFoundException($"Log file not found for the specified index {previousIndex}.");

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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : DownloadLogFileAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }


        // BackendDeviceController
        public async Task<RuntimeDeviceInfo[]> GetRuntimeDevicesAsync()
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetRuntimeDevicesAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<RuntimeDeviceInfo?> GetRuntimeDeviceAsync(int deviceId)
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetRuntimeDeviceAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // BackendContextController
        public async Task<RuntimeContextInfo?> GetRuntimeContextInfoAsync()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetRuntimeContextInfoAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<RuntimeInitializeResponse?> InitializeRuntimeAsync(int deviceId = 0, string deviceName = "")
        {
            var request = new RuntimeInitializeRequest()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : InitializeRuntimeAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<RuntimeDisposeResponse?> DisposeRuntimeAsync(bool freeBuffers = false)
        {
            var request = new RuntimeDisposeRequest()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : DisposeRuntimeAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // BackendMemoryController
        public async Task<RuntimeMemInfo[]> GetMemoryListAsync()
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetMemoryListAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<RuntimeMemInfo?> GetMemoryInfoAsync(string indexPointerOrId)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var memInfos = await this.internalClient.MemoryListAsync();
                var memInfo = memInfos.FirstOrDefault(m => m.Id.Equals(indexPointerOrId, StringComparison.OrdinalIgnoreCase) || m.IndexPointer.Equals(indexPointerOrId, StringComparison.OrdinalIgnoreCase));
                hasValue = memInfo != null;
                return memInfo;
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetMemoryInfoAsync(indexPointerOrId='{indexPointerOrId}') (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : FreeMemoryAsync() (elapsed={DateTime.Now - started}), {(string.IsNullOrEmpty(value) ? "returned NULL" : $"returned '{value}'")}");
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : FreeAllMemoryAsync() (elapsed={DateTime.Now - started}), {(string.IsNullOrEmpty(value) ? "returned NULL" : $"returned '{value}'")}");
                }
            }
        }

        public async Task<RuntimePushResponse?> PushAsync(string assetIdOrName, bool serverSided = true, int chunkSize = 0, float overlap = 0.5f, string format = "png", bool keepData = false)
        {
            DateTime started = DateTime.Now;
            RuntimePushResponse? response = null;
            try
            {
                Guid? verifiedAssetId = await this.VerifyAssetIdExistsAsync(assetIdOrName);
                if (!verifiedAssetId.HasValue)
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Asset '{assetIdOrName}' does not exist or could not determine if it is an audio asset. PushAsync() aborted.");
                    }
                    return null;
                }

                if (serverSided)
                {
                    response = await this.internalClient.PushAssetAsync(verifiedAssetId.Value.ToString(), format, chunkSize, overlap, keepData);
                }

                else
                {
                    ISimdPayload? payload = null;
                    bool? isAudioAsset = await this.IsAssetAudioAsync(verifiedAssetId);

                    if (isAudioAsset == false)
                    {
                        var imageData = await this.internalClient.MediaDataAsync(assetIdOrName, format, chunkSize, overlap, keepData);
                        payload = await DataSerializer.SerializeAsync(imageData?.Base64Data ?? "", true);
                    }
                    else
                    {
                        var audioData = await this.internalClient.MediaDataAsync(assetIdOrName, format, chunkSize, overlap, keepData) as AudioData;
                        if (audioData?.AudioDataFloats?.LongLength > 0)
                        {
                            payload = await DataSerializer.SerializeAsync(audioData.AudioDataFloats, true);
                        }
                        else if (audioData?.AudioDataFloatChunks?.LongLength > 0)
                        {
                            payload = await DataSerializer.SerializeAsync(audioData.AudioDataFloatChunks, true);
                        }
                    }

                    if (payload == null)
                    {
                        await this.logger.LogAsync($"Failed to serialize data for asset '{assetIdOrName}'.");
                        return null;
                    }

                    var request = new RuntimePushRequest()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : PushAsync() (elapsed={DateTime.Now - started}), {(response != null ? "returned DTO" : "returned NULL")}");
                }
            }

            return response;
        }

        public async Task<RuntimePullResponse?> PullAsync(string indexPointerOrId, bool serverSided = true, bool freeBuffer = true)
        {
            DateTime started = DateTime.Now;
            bool hadValue = false;
            try
            {
                if (serverSided)
                {
                    // Nur wenn der String KEINE reines Pointer-Adresse (Zahl) ist, als Asset-ID prüfen
                    bool isNumericPointer = long.TryParse(indexPointerOrId, out _);
                    Guid? assetId = !isNumericPointer ? await this.VerifyAssetIdExistsAsync(indexPointerOrId) : null;

                    if (assetId.HasValue)
                    {
                        hadValue = true;
                        return await this.internalClient.PullAssetAsync(assetId.Value.ToString(), !freeBuffer);
                    }
                    else
                    {
                        // Speicher-Pointer immer strikt über den POST Memory-Pull Endpunkt auflösen
                        var lazyRequest = new RuntimePullRequest()
                        {
                            IndexPointerOrId = indexPointerOrId,
                            AsyncCall = true,
                            FreeAfterPull = freeBuffer,
                            EnsureReferencedAssetsUpdatedOrCreated = true
                        };
                        hadValue = true;
                        return await this.internalClient.PullAsync(lazyRequest);
                    }
                }
                else
                {
                    var request = new RuntimePullRequest()
                    {
                        IndexPointerOrId = indexPointerOrId,
                        AsyncCall = true,
                        FreeAfterPull = freeBuffer
                    };
                    hadValue = true;
                    return await this.internalClient.PullAsync(request);
                }
            }
            catch (ApiException apiEx)
            {
                hadValue = false;
                await this.logger.LogAsync($"[ApiClient] PullAsync Deserialization Error (Status {apiEx.StatusCode}): {apiEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                hadValue = false;
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : PullAsync(indexPointerOrId='{indexPointerOrId}', serverSided={serverSided}, freeBuffer={freeBuffer}) (elapsed={DateTime.Now - started}), {(hadValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // BackendFourierController
        public async Task<RuntimeFourierResponse?> PerformFourierTransformAsync(string indexPointerOrId, bool? inverse = null, bool keepBuffer = false)
        {
            var memInfo = await this.GetMemoryInfoAsync(indexPointerOrId);
            if (memInfo == null)
            {
                await this.logger.LogAsync($"Memory info not found for index pointer or ID: {indexPointerOrId}");
                return null;
            }

            var request = new RuntimeFourierRequest()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : PerformFourierTransformAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<RuntimeFourierResponse?> PerformFourierOnAudioAsync(string audioNameOrId, int chunkSize = 8192, float overlap = 0.5f, bool autoPull = false, bool keepDataOrBuffer = false)
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : PerformFourierOnAudioAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }


        // BackendKernelController
        public async Task<RuntimeKernelInfo[]> GetKernelsAsync(bool filterCompiled = true)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var kernels = await this.internalClient.KernelsAsync(filterCompiled);
                count = kernels.Count;
                return kernels.ToArray();
            }
            catch (ApiException apiEx)
            {
                return apiEx.StatusCode == 404 || apiEx.StatusCode == 204 ?  [] :  [];
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetKernelsAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<RuntimeCompileResponse?> CompileKernelAsync(string kernelCode, bool silent = false)
        {
            string? kernelName = DataParser.ExtractKernelName(kernelCode);
            if (string.IsNullOrEmpty(kernelName))
            {
                await this.logger.LogAsync("Failed to extract kernel name from the provided kernel code.");
                return null;
            }

            var request = new RuntimeCompileRequest()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : CompileKernelAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<RuntimeExecuteResponse?> ExecuteGenericKernelAsync(string kernelName, string[]? args = null, bool unloadAfterExecute = false, bool createResultPointerAssetReference = false)
        {
            RuntimeKernelInfo? kernelInfo = (await this.internalClient.KernelsAsync(true)).FirstOrDefault(k => k.FunctionName.Equals(kernelName, StringComparison.OrdinalIgnoreCase));
            if (kernelInfo == null)
            {
                await this.logger.LogAsync($"Kernel '{kernelName}' not found or not compiled.");
                return null;
            }

            var request = new RuntimeExecuteRequest()
            {
                KernelInfo = kernelInfo,
                ArgumentValues = args ?? [],
                UnloadAfterExecution = unloadAfterExecute,
                CreateResultPointerAssetReference = createResultPointerAssetReference,
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : ExecuteGenericKernelAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<RuntimeExecuteResponse?> ExecuteLinearKernelAsync(string kernelName, string[]? args = null, bool unloadAfterExecute = false)
        {
            RuntimeKernelInfo? kernelInfo = (await this.internalClient.KernelsAsync(true)).FirstOrDefault(k => k.FunctionName.Equals(kernelName, StringComparison.OrdinalIgnoreCase));
            if (kernelInfo == null)
            {
                await this.logger.LogAsync($"Kernel '{kernelName}' not found or not compiled.");
                return null;
            }

            var request = new RuntimeExecuteRequest()
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : ExecuteLinearKernelAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : UploadMediaAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : DownloadMediaAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<ImageInfo[]> GetImageInfosAsync(string? nameSearch = null)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var images = (await this.internalClient.MediaInfosAsync()).OfType<ImageInfo>().ToArray();
                if (!string.IsNullOrEmpty(nameSearch))
                {
                    images = images.Where(i => i.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                count = images.Count();
                return images.ToArray();
            }
            catch (ApiException apiEx)
            {
                return apiEx.StatusCode == 404 || apiEx.StatusCode == 204 ?  [] :  [];
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetImageInfosAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<AudioInfo[]> GetAudioInfosAsync(string? nameSearch = null)
        {
            DateTime started = DateTime.Now;
            int count = 0;
            try
            {
                var audios = (await this.internalClient.MediaInfosAsync()).OfType<AudioInfo>().ToArray();
                if (!string.IsNullOrEmpty(nameSearch))
                {
                    audios = audios.Where(a => a.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                count = audios.Count();
                return audios.ToArray();
            }
            catch (ApiException apiEx)
            {
                return apiEx.StatusCode == 404 || apiEx.StatusCode == 204 ?  [] :  [];
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAudioInfosAsync() (elapsed={DateTime.Now - started}, count={count})");
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAllAssetIdsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<ImageData?> GetImageDataAsync(string idOrName, string format = "png", bool keepData = true)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var imageData = await this.internalClient.MediaDataAsync(idOrName, format, 0, 0, keepData) as ImageData;
                hasValue = imageData != null;
                return imageData;
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetImageDataAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<AudioData?> GetAudioDataAsync(string idOrName, int chunkSize = 0, float overlap = 0.5f, bool keepData = true)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var audioData = await this.internalClient.MediaDataAsync(idOrName, "wav", chunkSize, overlap, keepData) as AudioData;
                hasValue = audioData != null;
                return audioData;
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAudioDataAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
                }
            }
        }

        public async Task<ImageData?> GetImagePreviewAsync(string idOrName, int maxDimensions = 256)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var result = await this.internalClient.MediaPreviewAsync(idOrName, maxDimensions, maxDimensions, maxDimensions) as ImageData;
                hasValue = result != null;
                return result;
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetImagePreviewAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
                        await this.logger.LogAsync("No image IDs or names provided for preview retrieval.");
                    }
                    return [];
                }

                ConcurrentDictionary<DateTime, ImageData> result = [];

                var tasks = idsOrNames.Select(async idOrName =>
                {
                    try
                    {
                        var imageData = await this.internalClient.MediaPreviewAsync(idOrName, maxDimensions, maxDimensions, maxDimensions) as ImageData;
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
                        await this.logger.LogAsync(ex);
                    }
                });

                await Task.WhenAll(tasks);

                return result.OrderBy(e => e.Key).Select(e => e.Value).ToArray();
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetImagePreviewsAsync() (elapsed={DateTime.Now - started}, count={count})");
                }
            }
        }

        public async Task<ImageData?> GetAudioWaveformAsync(string idOrName, int width = 512, int height = 128)
        {
            DateTime started = DateTime.Now;
            bool hasValue = false;
            try
            {
                var result = await this.internalClient.MediaPreviewAsync(idOrName, width, height, width) as ImageData;
                hasValue = result != null;
                return result;
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAudioWaveformAsync() (elapsed={DateTime.Now - started}), {(hasValue ? "returned DTO" : "returned NULL")}");
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
                        await this.logger.LogAsync("No audio IDs or names provided for waveform retrieval.");
                    }
                    return [];
                }

                ConcurrentDictionary<DateTime, ImageData> result = [];
                var tasks = idsOrNames.Select(async idOrName =>
                {
                    try
                    {
                        var imageData = await this.internalClient.MediaPreviewAsync(idOrName, width, height, width) as ImageData;
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
                        await this.logger.LogAsync(ex);
                    }
                });

                await Task.WhenAll(tasks);
                return result.OrderBy(e => e.Key).Select(e => e.Value).ToArray();
            }
            catch (Exception ex)
            {
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAudioWaveformsAsync() (elapsed={DateTime.Now - started}, count={count})");
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
                await this.logger.LogAsync(ex);
                return false;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : DeleteMediaAsync() (elapsed={DateTime.Now - started}), {(value ? "returned TRUE" : "returned FALSE")}");
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
                await this.logger.LogAsync(ex);
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : ClearAllMediaAsync() (elapsed={DateTime.Now - started})");
                }
            }

        }




        // Accessibility helpers


        public async Task<Guid?> GetAssetIdForIndexPointerAsync(string indexPointer)
        {
            DateTime started = DateTime.Now;
            Guid? assetId = null;

            try
            {
                RuntimeMemInfo? mem = await this.GetMemoryInfoAsync(indexPointer);
                if (mem == null || string.IsNullOrEmpty(mem.Id))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find a CudaMem-obj allocated with IndexPointer={indexPointer}");
                    }
                    return null;
                }

                Guid? audioId = (await this.GetAudioInfosAsync()).FirstOrDefault(a => !string.IsNullOrEmpty(a.Pointer) && mem.Pointers.Contains(a.Pointer, StringComparer.OrdinalIgnoreCase))?.Id;
                Guid? imageId = (await this.GetImageInfosAsync()).FirstOrDefault(i => !string.IsNullOrEmpty(i.Pointer) && mem.Pointers.Contains(i.Pointer, StringComparer.OrdinalIgnoreCase))?.Id;

                assetId = audioId ?? imageId;
                return assetId;
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAssetIdForIndexPointer(indexPointer='{indexPointer}') (elapsed={DateTime.Now - started}), {(assetId.HasValue ? $"returned assetId={assetId}" : "returned NULL")}");
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetAssetIdsForIndexPointers() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<Guid?> VerifyAssetIdExistsAsync(Guid id)
        {
            DateTime started = DateTime.Now;
            Guid? guid = null;
            try
            {
                Guid? audioId = (await this.GetAudioInfosAsync()).FirstOrDefault(a => a.Id.Equals(id))?.Id;
                Guid? imageId = (await this.GetImageInfosAsync()).FirstOrDefault(i => i.Id.Equals(id))?.Id;
                guid = audioId ?? imageId;

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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : VerifyAssetIdExistsAsync(id={id}) (elapsed={DateTime.Now - started}), {(guid.HasValue ? $"returned assetId={guid}" : "returned NULL")}");
                }
            }
        }

        public async Task<Guid?> VerifyAssetIdExistsAsync(string idOrName)
        {
            DateTime started = DateTime.Now;
            Guid? guid = null;
            if (string.IsNullOrWhiteSpace(idOrName))
            {
                if ((int) this.LogLevel >= 4)
                {
                    await this.logger.LogAsync($"VerifyAssetIdExistsAsync() called with null or empty idOrName.");
                }
                return null;
            }
            try
            {
                if (Guid.TryParse(idOrName, out var parsedGuid))
                {
                    guid = await this.VerifyAssetIdExistsAsync(parsedGuid);
                }
                else
                {
                    guid = (await this.GetAudioInfosAsync()).FirstOrDefault(a => a.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || a.Id.ToString().Equals(idOrName, StringComparison.OrdinalIgnoreCase))?.Id ?? (await this.GetImageInfosAsync()).FirstOrDefault(i => i.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || i.Id.ToString().Equals(idOrName, StringComparison.OrdinalIgnoreCase))?.Id;
                    if ((int) this.LogLevel >= 5)
                    {
                        await this.logger.LogAsync($"[ApiClient] : VerifyAssetIdExistsAsync() (elapsed={DateTime.Now - started}, assetId={guid})");
                    }
                }
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
                await this.logger.LogAsync(ex);
                return null;
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : VerifyAssetIdsExistsAsync() (elapsed={DateTime.Now - started})");
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : VerifyAssetIdsExistsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string?> GetIndexPointerForAssetIdAsync(Guid assetId)
        {
            DateTime started = DateTime.Now;
            string? verifiedPtr = null;
            try
            {
                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(assetId);
                if (!verifiedId.HasValue || verifiedId.Value == Guid.Empty)
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId}");
                    }
                    return null;
                }

                string? assetPtr = (await this.GetAudioInfosAsync()).FirstOrDefault(a => a.Id.Equals(verifiedId))?.Pointer ?? (await this.GetImageInfosAsync()).FirstOrDefault(a => a.Id.Equals(verifiedId))?.Pointer;
                if (string.IsNullOrEmpty(assetPtr))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId} that has a Pointer");
                    }
                    return null;
                }

                verifiedPtr = (await this.GetMemoryInfoAsync(assetPtr))?.IndexPointer;
                if (string.IsNullOrEmpty(verifiedPtr))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId} that has a Pointer which can be found in any CudaMemInfo-obj allocated");
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetIndexPointerForAssetIdAsync(assetId={assetId}) (elapsed={DateTime.Now - started}), {(string.IsNullOrEmpty(verifiedPtr) ? "returned NULL" : $"returned indexPointer='{verifiedPtr}'")}");
                }
            }
        }

        public async Task<string?> GetIndexPointerForAssetIdAsync(string idOrName)
        {
            DateTime started = DateTime.Now;
            string? indexPointer = null;
            try
            {
                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(idOrName);
                if (verifiedId == null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id or Name={idOrName}");
                    }
                    return null;
                }
                indexPointer = await this.GetIndexPointerForAssetIdAsync(verifiedId.Value);
                return indexPointer;
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetIndexPointerForAssetIdAsync(idOrName='{idOrName}') (elapsed={DateTime.Now - started}), {(string.IsNullOrEmpty(indexPointer) ? "returned NULL" : $"returned indexPointer='{indexPointer}'")}");
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
                        await this.logger.LogAsync($"One or more provided assetIds do not exist as an Asset (ImageObj/AudioObj) or equal Guid.Empty, which is invalid: Ids=[{string.Join(" ,", assetIds.Where(i => !verifiedIds.Contains(i)))}]");
                    }
                    return [];
                }

                var assetPtrs = (await this.GetAudioInfosAsync()).Where(a => verifiedIds.Contains(a.Id)).Select(i => i.Pointer ?? "0").Concat((await this.GetImageInfosAsync()).Where(a => verifiedIds.Contains(a.Id)).Select(i => i.Pointer ?? "0"));
                if (assetPtrs.Count() != verifiedIds.Count())
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Lengths mismatching for verifiedIds and assetPtrs ({verifiedIds.Length} != {assetPtrs.Count()})");
                        return [];
                    }
                    return [];
                }

                var verifiedPtrs = (await this.GetMemoryListAsync()).Where(m => assetPtrs.Contains(m.IndexPointer)).Select(i => i.IndexPointer);
                if (verifiedPtrs.Count() != assetPtrs.Count())
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Lengths mismatching for verifiedPtrs and assetPtrs ({verifiedPtrs.Count()} != {assetPtrs.Count()})");
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetIndexPointesrForAssetIdsAsync() (elapsed={DateTime.Now - started})");
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
                        await this.logger.LogAsync($"One or more provided idsOrNames do not exist as an Asset (ImageObj/AudioObj) or equal Guid.Empty, which is invalid: Ids=[{string.Join(" ,", idsOrNames.Select(i => Guid.TryParse(i, out var g) ? g : (Guid?) null)?.Where(v => v.HasValue && !verifiedIds.Contains(v.Value)) ?? [])}]");
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
                await this.logger.LogAsync(ex);
                return [];
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : GetIndexPointersForAssetIdsAsync() (elapsed={DateTime.Now - started})");
                }
            }
        }

        public async Task<string[]> GetIndexPointersWithAssetRefIdAsync(string assedId)
        {
            if (string.IsNullOrEmpty(assedId))
            {
                return [];
            }

            return Guid.TryParse(assedId, out var guid) && !guid.Equals(Guid.Empty)
                ? (await this.GetMemoryListAsync())
                    .Where(m => (m.AssetReferenceIds ?? []).Contains(guid) || m.AssetReferenceId == guid)
                    .Select(rm => rm.IndexPointer)
                    .ToArray()
                :  [];
        }

        /// <summary>
        /// Gets the <see cref="Shared.Interfaces.IRuntimeMem.IndexPointer"/> from every <see cref="Shared.Interfaces.IRuntimeMem"/> <br/>
        /// matching their <see cref="Shared.Interfaces.IRuntimeMem.AssetReferenceIds"/> or <see cref="Shared.Interfaces.IRuntimeMem.AssetReferenceId"/> <br/>
        /// over <paramref name="requireAll"/> with the set of <paramref name="assetIds"/>.<br/><br/>
        /// <b>Parameters:</b><br/>
        /// • <b>assetIds:</b> Requires not even 'some' <see cref="System.Collections.Generic.IEnumerable{T}"/> ids from <see cref="System.Guid.ToString()"/> each, you can leave it as-is and get an empty <see cref="System.Array"/> back.<br/>
        /// • <b>requireAll:</b> Ternary operator:<br/>
        ///   - true = EVERY <paramref name="assetIds"/> has to match EVERY <see cref="Shared.Interfaces.IRuntimeMem.AssetReferenceIds"/><br/>
        ///   - null = INDEX <see cref="Shared.Interfaces.IRuntimeMem.AssetReferenceId"/> has to match ANY <paramref name="assetIds"/><br/>
        ///   - false = ANY <see cref="Shared.Interfaces.IRuntimeMem.AssetReferenceIds"/> shall match ANY <paramref name="assetIds"/>, at least one.<br/>
        /// • <b>distinct:</b> Toggles filtering the results using <see cref="System.Linq.Enumerable.Distinct{TSource}(System.Collections.Generic.IEnumerable{TSource})"/>.
        /// </summary>
        /// <param name="assetIds">Requires not even 'some' <see cref="System.Collections.Generic.IEnumerable{T}"/> ids from <see cref="System.Guid.ToString()"/> each, you can leave it as-is and get an empty <see cref="System.Array"/> back.</param>
        /// <param name="requireAll">Ternary operator: true=EVERY assetIds matches EVERY AssetReferenceIds, null=INDEX AssetReferenceId matches ANY assetIds, false=ANY AssetReferenceIds matches ANY assetIds.</param>
        /// <param name="distinct">Toggles filtering the results using <see cref="System.Linq.Enumerable.Distinct{TSource}(System.Collections.Generic.IEnumerable{TSource})"/>.</param>
        /// <returns>A string[] which is non-nullable and contains each matching <see cref="Shared.Interfaces.IRuntimeMem.IndexPointer"/>.</returns>
        public async Task<string[]> GetIndexPointersForRefIdsAsync(IEnumerable<string>? assetIds = null, bool? requireAll = null, bool distinct = true)
        {
            assetIds = assetIds?.Where(a => !string.IsNullOrEmpty(a)).Cast<string>().ToArray();
            if (assetIds == null || !(assetIds.Any()))
            {
                return [];
            }

            var memInfos = await this.GetMemoryListAsync();
            if (memInfos == null || !(memInfos.Length > 0))
            {
                return [];
            }

            var assetIdSet = assetIds.ToHashSet();
            var query = memInfos.AsParallel().Where(mem =>
            {
                var memRefs = (mem.AssetReferenceIds ?? []).Select(r => r.ToString()).ToHashSet();
                if (mem.AssetReferenceId.HasValue)
                {
                    memRefs.Add(mem.AssetReferenceId.Value.ToString());
                }

                return requireAll.Equals(true) ? assetIdSet.All(id => memRefs.Contains(id)) :
                       requireAll.Equals(null) ? !string.IsNullOrEmpty(mem.AssetReferenceId.ToString()) && assetIds.Contains(mem.AssetReferenceId.ToString()) :
                       requireAll.Equals(false) && memRefs.Any(id => assetIds.Contains(id));
            }).Select(m => m.IndexPointer);

            return distinct ? query.Distinct().ToArray() : query.ToArray();
        }

        public async Task<bool?> IsAssetAudioAsync(Guid? assetId)
        {
            DateTime started = DateTime.Now;
            bool? isAudio = null;
            try
            {
                if (assetId == null || assetId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Null or empty assetId provided: {assetId}");
                    }
                    return null;
                }

                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(assetId.Value);
                if (verifiedId == null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id={assetId}");
                    }
                    return null;
                }
                isAudio = (await this.GetAudioInfosAsync()).Any(a => a.IdMatch(verifiedId.Value));
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : IsAssetAudioAsync(assetId={assetId}) (elapsed={DateTime.Now - started}), {(isAudio.HasValue ? $"returned {isAudio}" : "returned NULL")}");
                }
            }
        }

        public async Task<bool?> IsAssetAudioAsync(string? assetIdOrName)
        {
            DateTime started = DateTime.Now;
            bool? isAudio = null;
            try
            {
                if (string.IsNullOrEmpty(assetIdOrName))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Null or empty assetIdOrName provided: {assetIdOrName}");
                    }
                    return null;
                }

                Guid? verifiedId = await this.VerifyAssetIdExistsAsync(assetIdOrName);
                if (verifiedId == null || verifiedId.Equals(Guid.Empty))
                {
                    if ((int) this.LogLevel >= 4)
                    {
                        await this.logger.LogAsync($"Could not find an Asset-obj in Audios nor Images with Id or Name={assetIdOrName}");
                    }
                    return null;
                }

                isAudio = (await this.GetAudioInfosAsync()).Any(a => a.IdMatch(verifiedId.Value));
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
                await this.logger.LogAsync(ex);
                return null;
            }
            finally
            {
                if ((int) this.LogLevel >= 5)
                {
                    await this.logger.LogAsync($"[ApiClient] : IsAssetAudioAsync(assetIdOrName='{assetIdOrName}') (elapsed={DateTime.Now - started}), {(isAudio.HasValue ? $"returned {isAudio}" : "returned NULL")}");
                }
            }
        }

        public async Task<RuntimeExecuteResponse?> ExecuteGenericKernelAsync(String functionName, String[] argumentValues, Object unloadAfterExecute, Boolean createResultPointerReferenceAssets) => throw new NotImplementedException();
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
