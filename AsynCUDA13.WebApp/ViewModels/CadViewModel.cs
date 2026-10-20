using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AsynCUDA13.WebApp.ViewModels
{
    public sealed class CadViewModel : ViewModelBase<RuntimeExecuteRequest, RuntimeExecuteResponse>, IViewModelLifecycle
    {
        private readonly LanguageService _language;
        private bool _editorInitialized;
        private bool _disposed;
        private bool _backendReady;
        private bool _kernelReady;
        private bool _renderWorkerRunning;
        private long _latestRenderRevision;
        private double? _lastRenderDurationMilliseconds;
        private CadRenderRequest? _pendingRender;
        private DotNetObjectReference<CadViewModel>? _editorReference;
        private readonly object _renderQueueLock = new();

        public CadViewModel(ApiClient apiClient, IJSRuntime js, LanguageService language)
            : base(apiClient, js)
        {
            this._language = language;
        }

        public string SelectedShape { get; private set; } = "cube";
        public string BackendLabel => string.IsNullOrWhiteSpace(this.Api.BackendType) ? this._language["CadBackendNotConnected"] : this.Api.BackendType;
        public string AddButtonText => this.SelectedShape switch
        {
            "sphere" => this._language["CadAddSphere"],
            "pyramid" => this._language["CadAddPyramid"],
            "cylinder" => this._language["CadAddCylinder"],
            _ => this._language["CadAddCube"]
        };
        public string? BackendMessage { get; private set; }
        public bool BackendError { get; private set; }
        public string LastRenderDurationText => this._lastRenderDurationMilliseconds is double milliseconds
            ? string.Format(this._language.GetCurrentCulture(), this._language["CadRenderDurationMilliseconds"], milliseconds)
            : this._language["CadRenderNotAvailable"];

        public async Task LoadAsync()
        {
            try
            {
                await this.Api.InitializeAsync();
            }
            catch (Exception ex)
            {
                this.BackendError = true;
                this.BackendMessage = string.Format(this._language["CadBackendConnectionUnavailable"], ex.Message);
            }
        }

        public async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || this._editorInitialized)
            {
                return;
            }

            this._editorInitialized = true;
            this._editorReference = DotNetObjectReference.Create(this);
            await this.Js.InvokeVoidAsync("cadEditor.initialize", "cad-canvas", "cad-object-list", "cad-import-file", this.GetEditorTexts(), this._editorReference);
        }

        public async Task OnShapeChanged(ChangeEventArgs args)
        {
            this.SelectedShape = args.Value?.ToString() ?? "cube";
            await this.Js.InvokeVoidAsync("cadEditor.setTool", this.SelectedShape);
        }

        public async Task AddShapeAsync() => await this.Js.InvokeVoidAsync("cadEditor.addPrimitive", this.SelectedShape);
        public async Task CompletePrismSketchAsync() => await this.Js.InvokeVoidAsync("cadEditor.completePrismSketch");

        public async Task FinishPrismAsync()
        {
            this.SelectedShape = "cube";
            await this.Js.InvokeVoidAsync("cadEditor.finishPrism");
        }

        public async Task ToggleMoveAsync() => await this.Js.InvokeVoidAsync("cadEditor.toggleMoveMode");
        public async Task DeleteSelectedAsync() => await this.Js.InvokeVoidAsync("cadEditor.deleteSelected");
        public async Task UndoAsync() => await this.Js.InvokeVoidAsync("cadEditor.undo");
        public async Task RedoAsync() => await this.Js.InvokeVoidAsync("cadEditor.redo");
        public async Task ClearSceneAsync() => await this.Js.InvokeVoidAsync("cadEditor.clearScene");
        public async Task ExportSceneAsync() => await this.Js.InvokeVoidAsync("cadEditor.exportScene");
        public async Task ImportSceneAsync() => await this.Js.InvokeVoidAsync("cadEditor.openImport");
        public async Task ResetViewAsync() => await this.Js.InvokeVoidAsync("cadEditor.resetView");
        public async Task RenderNowAsync() => await this.Js.InvokeVoidAsync("cadEditor.renderNow");

        [JSInvokable]
        public async Task QueueBackendRenderAsync(IJSStreamReference sceneDataStream, int primitiveCount, float[] camera, int width, int height, long revision)
        {
            bool isStale;
            lock (this._renderQueueLock)
            {
                isStale = this._disposed || revision <= this._latestRenderRevision;
                if (!isStale)
                {
                    this._latestRenderRevision = revision;
                }
            }
            if (isStale)
            {
                await sceneDataStream.DisposeAsync();
                return;
            }

            float[]? sceneData;
            try
            {
                await using Stream stream = await sceneDataStream.OpenReadStreamAsync(8 * 1024 * 1024);
                sceneData = await JsonSerializer.DeserializeAsync<float[]>(stream);
            }
            finally
            {
                await sceneDataStream.DisposeAsync();
            }

            sceneData ??= [];
            bool startWorker = false;
            lock (this._renderQueueLock)
            {
                if (this._disposed || revision != this._latestRenderRevision)
                {
                    return;
                }
                this._pendingRender = new CadRenderRequest(sceneData, primitiveCount, camera, width, height, revision);
                if (!this._renderWorkerRunning)
                {
                    this._renderWorkerRunning = true;
                    startWorker = true;
                }
            }

            if (startWorker)
            {
                _ = this.ProcessRenderQueueAsync();
            }
        }

        private Dictionary<string, string> GetEditorTexts() => new()
        {
            ["shapeCube"] = this._language["CadShapeCube"],
            ["shapeSphere"] = this._language["CadShapeSphere"],
            ["shapePyramid"] = this._language["CadShapePyramid"],
            ["shapePrism"] = this._language["CadShapePrismName"],
            ["shapeCylinder"] = this._language["CadShapeCylinder"],
            ["statusNavigation"] = this._language["CadStatusNavigation"],
            ["saved"] = this._language["CadLocalSaved"],
            ["saveFailed"] = this._language["CadSaveFailed"],
            ["saveError"] = this._language["CadSaveError"],
            ["savedLoadError"] = this._language["CadSavedLoadError"],
            ["objectOne"] = this._language["CadObjectCountOne"],
            ["objectMany"] = this._language["CadObjectCountMany"],
            ["noSelection"] = this._language["CadNoSelection"],
            ["statusPrismTool"] = this._language["CadStatusPrismTool"],
            ["statusSelectedTool"] = this._language["CadStatusSelectedTool"],
            ["statusCreated"] = this._language["CadStatusCreated"],
            ["statusSelected"] = this._language["CadStatusSelected"],
            ["prismMinimum"] = this._language["CadPrismMinimum"],
            ["prismPointSet"] = this._language["CadPrismPointSet"],
            ["prismHeight"] = this._language["CadPrismHeight"],
            ["prismCancelled"] = this._language["CadPrismCancelled"],
            ["prismPointLimit"] = this._language["CadPrismPointLimit"],
            ["prismCreated"] = this._language["CadPrismCreated"],
            ["selectedDeleted"] = this._language["CadSelectedDeleted"],
            ["confirmClear"] = this._language["CadConfirmClear"],
            ["sceneCleared"] = this._language["CadSceneCleared"],
            ["undo"] = this._language["CadUndoStatus"],
            ["redo"] = this._language["CadRedoStatus"],
            ["moveSelect"] = this._language["CadMoveSelect"],
            ["moveActive"] = this._language["CadMoveActive"],
            ["moveFinished"] = this._language["CadMoveFinished"],
            ["backendRendering"] = this._language["CadBackendRendering"],
            ["sceneExported"] = this._language["CadSceneExported"],
            ["importTooLarge"] = this._language["CadImportTooLarge"],
            ["importInvalid"] = this._language["CadImportInvalid"],
            ["imported"] = this._language["CadImported"],
            ["importFailed"] = this._language["CadImportFailed"],
            ["canvasInitFailed"] = this._language["CadCanvasInitFailed"]
        };

        private async Task ProcessRenderQueueAsync()
        {
            while (!this._disposed)
            {
                CadRenderRequest? request;
                lock (this._renderQueueLock)
                {
                    request = this._pendingRender;
                    this._pendingRender = null;
                    if (request == null)
                    {
                        this._renderWorkerRunning = false;
                        return;
                    }
                }

                try
                {
                    CadRenderedFrame frame = await this.RenderFrameAsync(request);
                    if (!this.IsLatestRender(request.Revision))
                    {
                        continue;
                    }

                    this._lastRenderDurationMilliseconds = frame.Duration.TotalMilliseconds;
                    this.NotifyStateChanged(false);
                    using var stream = new MemoryStream(frame.Pixels, writable: false);
                    using var streamReference = new DotNetStreamReference(stream);
                    string status = string.Format(this._language.GetCurrentCulture(), this._language["CadBackendRendered"], this.Api.BackendType, request.Width, request.Height);
                    await this.Js.InvokeVoidAsync("cadEditor.displayBackendFrame", streamReference, request.Width, request.Height, request.Revision, status);
                }
                catch (JSDisconnectedException)
                {
                    lock (this._renderQueueLock)
                    {
                        this._renderWorkerRunning = false;
                        this._pendingRender = null;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    if (!this.IsLatestRender(request.Revision))
                    {
                        continue;
                    }

                    try
                    {
                        string message = string.Format(this._language["CadOperationFailed"], ex.Message);
                        await this.Js.InvokeVoidAsync("cadEditor.backendRenderFailed", request.Revision, message);
                    }
                    catch (JSDisconnectedException)
                    {
                        lock (this._renderQueueLock)
                        {
                            this._renderWorkerRunning = false;
                            this._pendingRender = null;
                        }
                        return;
                    }
                }
            }

            lock (this._renderQueueLock)
            {
                this._renderWorkerRunning = false;
                this._pendingRender = null;
            }
        }

        private bool IsLatestRender(long revision)
        {
            lock (this._renderQueueLock)
            {
                return !this._disposed && this._pendingRender == null && this._latestRenderRevision == revision;
            }
        }

        private async Task<CadRenderedFrame> RenderFrameAsync(CadRenderRequest request)
        {
            long startedAt = Stopwatch.GetTimestamp();
            if (request.SceneData is null || request.Camera is null || request.SceneData.Length == 0 ||
                request.PrimitiveCount < 0 || request.PrimitiveCount > request.SceneData.Length / 10 || request.Camera.Length != 13)
            {
                throw new InvalidOperationException(this._language["CadRenderInvalidScene"]);
            }
            if (request.Width < 1 || request.Height < 1 || request.Width > 1024 || request.Height > 768 || (long) request.Width * request.Height > 1024 * 768)
            {
                throw new InvalidOperationException(this._language["CadRenderInvalidSize"]);
            }
            this.ValidateRenderData(request);
            if (!this.Api.Initialized)
            {
                await this.Api.InitializeAsync();
            }
            if (!this.Api.BackendType.Equals("CUDA", StringComparison.OrdinalIgnoreCase) &&
                !this.Api.BackendType.Equals("OpenCL", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(this._language["CadBackendUnsupported"], this.Api.BackendType));
            }

            await this.EnsureBackendReadyAsync();

            const string kernelName = "cad_render_scene";
            var scenePush = await this.Api.PushBufferAsync(request.SceneData);
            string? scenePointer = scenePush?.MemoryInfo?.IndexPointer;
            if (string.IsNullOrWhiteSpace(scenePointer))
            {
                throw new InvalidOperationException(this._language["CadPushFailed"]);
            }

            string? imagePointer = null;
            bool imagePulled = false;
            try
            {
                if (scenePush?.Success != true)
                {
                    throw new InvalidOperationException(this._language["CadPushFailed"]);
                }

                int pixelCount = checked(request.Width * request.Height);
                var imagePush = await this.Api.PushBufferAsync(new float[pixelCount]);
                imagePointer = imagePush?.MemoryInfo?.IndexPointer;
                if (imagePush?.Success != true || string.IsNullOrWhiteSpace(imagePointer))
                {
                    throw new InvalidOperationException(this._language["CadPushFailed"]);
                }

                var arguments = new List<string>(18)
                {
                    scenePointer,
                    request.PrimitiveCount.ToString(CultureInfo.InvariantCulture),
                    imagePointer,
                    request.Width.ToString(CultureInfo.InvariantCulture),
                    request.Height.ToString(CultureInfo.InvariantCulture)
                };
                arguments.AddRange(request.Camera.Select(value => value.ToString(CultureInfo.InvariantCulture)));

                var execution = await this.Api.ExecuteGenericKernelAsync(kernelName, [.. arguments]);
                if (execution?.KernelInfo == null ||
                    (this.Api.BackendType.Equals("OpenCL", StringComparison.OrdinalIgnoreCase) && !execution.Success))
                {
                    this._kernelReady = false;
                    throw new InvalidOperationException(string.Format(this._language["CadExecuteFailed"], this.Api.BackendType));
                }

                var pull = await this.Api.PullAsync(imagePointer, serverSided: false);
                if (pull?.Success != true)
                {
                    throw new InvalidOperationException(this._language["CadPullFailed"]);
                }
                imagePulled = true;
                if (pull.Payload is not AsynCUDA13.Shared.Api.Payloads.SimdPayload1D payload)
                {
                    throw new InvalidOperationException(this._language["CadPullInvalidFormat"]);
                }

                float[] packedPixels = await AsynCUDA13.Shared.Serialization.DataParser.ParseAsync<float>(payload) ?? [];
                if (packedPixels.Length != pixelCount)
                {
                    throw new InvalidOperationException(this._language["CadUnexpectedMeshSize"]);
                }

                byte[] pixels = MemoryMarshal.AsBytes(packedPixels.AsSpan()).ToArray();
                return new CadRenderedFrame(pixels, Stopwatch.GetElapsedTime(startedAt));
            }
            finally
            {
                if (!imagePulled && !string.IsNullOrWhiteSpace(imagePointer))
                {
                    await this.Api.FreeMemoryAsync(imagePointer);
                }
                await this.Api.FreeMemoryAsync(scenePointer);
            }
        }

        private void ValidateRenderData(CadRenderRequest request)
        {
            if (request.Camera.Any(value => !float.IsFinite(value)) || request.Camera[12] <= 0)
            {
                throw new InvalidOperationException(this._language["CadRenderInvalidScene"]);
            }

            int descriptorLength = checked(request.PrimitiveCount * 10);
            for (int index = 0; index < request.PrimitiveCount; index++)
            {
                int offset = index * 10;
                ReadOnlySpan<float> primitive = request.SceneData.AsSpan(offset, 10);
                if (ContainsNonFinite(primitive) || primitive[0] != MathF.Truncate(primitive[0]) || primitive[0] < 0 || primitive[0] > 6 ||
                    primitive[4] == 0 || primitive[5] == 0 || primitive[6] == 0 ||
                    primitive[7] != MathF.Truncate(primitive[7]) || primitive[8] != MathF.Truncate(primitive[8]) ||
                    primitive[7] < 0 || primitive[8] < 0 || (primitive[9] != 0 && primitive[9] != 1))
                {
                    throw new InvalidOperationException(this._language["CadRenderInvalidScene"]);
                }

                int kind = (int) primitive[0];
                int pointOffset = (int) primitive[7];
                int pointCount = (int) primitive[8];
                int pointStride = kind switch
                {
                    3 => pointCount is >= 3 and <= 128 ? 2 : 0,
                    6 => pointCount == 2 ? 3 : 0,
                    _ => pointCount == 0 ? 0 : -1
                };
                if (pointStride < 0 || ((kind == 3 || kind == 6) && pointStride == 0) ||
                    (pointStride == 0 && pointOffset != 0) ||
                    (pointStride > 0 && (pointOffset < descriptorLength || (long) pointOffset + (long) pointCount * pointStride > request.SceneData.Length)))
                {
                    throw new InvalidOperationException(this._language["CadRenderInvalidScene"]);
                }

                if (pointStride > 0 && ContainsNonFinite(request.SceneData.AsSpan(pointOffset, pointCount * pointStride)))
                {
                    throw new InvalidOperationException(this._language["CadRenderInvalidScene"]);
                }
            }
        }

        private static bool ContainsNonFinite(ReadOnlySpan<float> values)
        {
            foreach (float value in values)
            {
                if (!float.IsFinite(value)) return true;
            }
            return false;
        }

        private async Task EnsureBackendReadyAsync()
        {
            if (!this._backendReady)
            {
                var context = await this.Api.GetRuntimeContextInfoAsync();
                if (context?.Online != true)
                {
                    throw new InvalidOperationException(string.Format(this._language["CadBackendOffline"], this.Api.BackendType));
                }
                this._backendReady = true;
            }

            if (this._kernelReady)
            {
                return;
            }

            const string kernelName = "cad_render_scene";
            if (!(await this.Api.GetKernelsAsync()).Any(kernel => kernel.FunctionName.Equals(kernelName, StringComparison.OrdinalIgnoreCase)))
            {
                string extension = this.Api.BackendType.Equals("CUDA", StringComparison.OrdinalIgnoreCase) ? ".cu" : ".cl";
                string sourcePath = Path.Combine(AppContext.BaseDirectory, "Kernels", "CAD", kernelName + extension);
                string source = await File.ReadAllTextAsync(sourcePath);
                var compileResult = await this.Api.CompileKernelAsync(source);
                if (compileResult?.KernelInfo == null)
                {
                    throw new InvalidOperationException(string.Format(this._language["CadCompileFailed"], this.Api.BackendType));
                }
            }
            this._kernelReady = true;
        }

        public async ValueTask DisposeAsync()
        {
            lock (this._renderQueueLock)
            {
                if (this._disposed)
                {
                    return;
                }
                this._disposed = true;
                this._pendingRender = null;
            }

            try
            {
                if (this._editorInitialized)
                {
                    await this.Js.InvokeVoidAsync("cadEditor.dispose");
                }
            }
            catch (JSDisconnectedException)
            {
            }
            finally
            {
                this._editorReference?.Dispose();
                this._editorReference = null;
            }
        }

        private sealed record CadRenderRequest(float[] SceneData, int PrimitiveCount, float[] Camera, int Width, int Height, long Revision);
        private sealed record CadRenderedFrame(byte[] Pixels, TimeSpan Duration);
    }
}