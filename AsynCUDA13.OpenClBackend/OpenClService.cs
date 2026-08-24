using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using OpenTK.Compute.OpenCL;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// High-level facade for the OpenCL runtime and the OpenCL counterpart to the CUDA runtime service.
    /// Enumerates every available OpenCL device (across all platforms, including CPU devices), creates a
    /// context and command queue for a selected device, compiles the kernels in memory and exposes simple
    /// FFT/IFFT entry points that operate directly on <see cref="float"/> and <see cref="Vector2"/> arrays.
    /// </summary>
    public sealed class OpenClService : IOpenClService, IDisposable
    {
        private OpenClRegister? _register;
        public IRuntimeRegister Register => this._register ?? throw new InvalidOperationException("OpenClService: service is offline. Call Initialize(...) first.");
        private OpenClCompiler? _compiler;
        public IRuntimeCompiler Compiler => this._compiler ?? throw new InvalidOperationException("OpenClService: service is offline. Call Initialize(...) first.");
        private OpenClLauncher? _launcher;
        public IRuntimeLauncher Launcher => this._launcher ?? throw new InvalidOperationException("OpenClService: service is offline. Call Initialize(...) first.");
        private OpenClFourier? _fourier;
        public IRuntimeFourier Fourier => this._fourier ?? throw new InvalidOperationException("OpenClService: service is offline. Call Initialize(...) first.");
        private bool _disposed;

        /// <summary>
        /// Gets all OpenCL devices available on the machine, each identified by a flat <see cref="OpenClDevice.Index"/>.
        /// </summary>
        public IReadOnlyList<OpenClDevice> AvailableDevices { get; }

        /// <summary>
        /// Gets the number of available OpenCL devices.
        /// </summary>
        public int DeviceCount => this.AvailableDevices.Count;

        /// <summary>
        /// Gets the flat index of the currently selected device, or <c>-1</c> when the service is offline.
        /// </summary>
        public int SelectedDeviceId { get; private set; } = -1;

        /// <summary>
        /// Gets the currently selected device info, or <c>null</c> when the service is offline.
        /// </summary>
        public OpenClDevice? SelectedDevice { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the service has an initialized device context.
        /// </summary>
        public bool Online => this._register != null && this.SelectedDeviceId >= 0;

        /// <summary>
        /// Gets the total number of bytes currently allocated on the device.
        /// </summary>
        public long TotalAllocatedBytes => this._register?.TotalAllocatedBytes ?? 0;




        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClService"/> class, discovering all devices.
        /// Optionally initializes a preferred device immediately.
        /// </summary>
        /// <param name="preferredDeviceIndex">
        /// The flat device index to initialize, or <c>-1</c> to leave the service offline until
        /// <see cref="Initialize(int)"/> is called.
        /// </param>
        public OpenClService(int preferredDeviceIndex = -1)
        {
            this.AvailableDevices = OpenClDevice.DiscoverAll();

            if (this.AvailableDevices.Count == 0)
            {
                StaticLogger.LogWarning("OpenClService: no OpenCL devices found on this machine.");
            }
            else
            {
                StaticLogger.LogSuccess($"OpenClService: discovered {this.AvailableDevices.Count} OpenCL device(s).");
            }

            if (preferredDeviceIndex >= 0)
            {
                this.Initialize(preferredDeviceIndex);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClService"/> class and initializes the first
        /// device whose name contains <paramref name="preferredDeviceName"/>.
        /// </summary>
        /// <param name="preferredDeviceName">A case-insensitive substring of the desired device name.</param>
        public OpenClService(string preferredDeviceName)
        {
            this.AvailableDevices = OpenClDevice.DiscoverAll();
            this.Initialize(preferredDeviceName);
        }



        // Initialization
        /// <summary>
        /// Initializes the context, command queue, compiler and launcher for the device at the given flat index.
        /// Any previously initialized device is disposed first.
        /// </summary>
        /// <param name="deviceIndex">The flat device index from <see cref="AvailableDevices"/>.</param>
        /// <returns><c>true</c> if the device was initialized successfully; otherwise <c>false</c>.</returns>
        public bool Initialize(int deviceIndex)
        {
            if (deviceIndex < 0 || deviceIndex >= this.AvailableDevices.Count)
            {
                StaticLogger.LogError($"Initialize: device index {deviceIndex} is out of range (0..{this.AvailableDevices.Count - 1}).");
                return false;
            }

            this.Shutdown();

            OpenClDevice info = this.AvailableDevices[deviceIndex];

            CLContext context = CL.CreateContext(IntPtr.Zero, [info.Device], IntPtr.Zero, IntPtr.Zero, out CLResultCode contextCode);
            if (contextCode != CLResultCode.Success)
            {
                StaticLogger.LogError($"Initialize: CreateContext failed for '{info.DeviceName}' ({contextCode}).");
                return false;
            }

            // CreateCommandQueue is deprecated since OpenCL 1.2, but it is the most broadly compatible
            // entry point: many CPU OpenCL runtimes only expose OpenCL 1.2, where CreateCommandQueue is the
            // canonical call. We keep it intentionally to maximize device coverage (including CPU-only systems).
#pragma warning disable CS0618
            CLCommandQueue queue = CL.CreateCommandQueue(context, info.Device, (CommandQueueProperty)0, out CLResultCode queueCode);
#pragma warning restore CS0618
            if (queueCode != CLResultCode.Success)
            {
                StaticLogger.LogError($"Initialize: CreateCommandQueue failed for '{info.DeviceName}' ({queueCode}).");
                CL.ReleaseContext(context);
                return false;
            }

            this._register = new OpenClRegister(context, queue, info.Device);
            this._compiler = new OpenClCompiler(context, info.Device);
            this._launcher = new OpenClLauncher(this._register, this._compiler, queue);
            this._fourier = new OpenClFourier(this._register, this._launcher);

            this.SelectedDeviceId = deviceIndex;
            this.SelectedDevice = info;

            StaticLogger.LogSuccess($"OpenClService: initialized device {info}.");
            return true;
        }

        /// <summary>
        /// Initializes the first device whose name contains <paramref name="deviceName"/> (case-insensitive).
        /// </summary>
        /// <param name="deviceName">A case-insensitive substring of the desired device name.</param>
        /// <returns><c>true</c> if a matching device was initialized; otherwise <c>false</c>.</returns>
        public bool Initialize(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                StaticLogger.LogError("Initialize: device name is null or empty.");
                return false;
            }

            for (int i = 0; i < this.AvailableDevices.Count; i++)
            {
                if (this.AvailableDevices[i].DeviceName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return this.Initialize(i);
                }
            }

            StaticLogger.LogError($"Initialize: no device matching '{deviceName}' found.");
            return false;
        }

        /// <summary>
        /// Releases the current device context, command queue, compiler and all allocations, leaving the
        /// service offline but reusable (devices can be re-enumerated via a new instance).
        /// </summary>
        public void Shutdown()
        {
            this._fourier = null;
            this._launcher = null;

            this._compiler?.Dispose();
            this._compiler = null;

            this._register?.Dispose();
            this._register = null;

            this.SelectedDeviceId = -1;
            this.SelectedDevice = null;
        }



        // Fourier convenience (delegates to OpenClFourier)
        /// <summary>
        /// Runs a full forward FFT over an arbitrary-length array. See <see cref="OpenClFourier.Fft(float[])"/>.
        /// </summary>
        public Vector2[]? Fft(float[] data)
        {
            return this.EnsureFourier()?.Fft(data);
        }

        /// <summary>
        /// Runs a full inverse FFT over an arbitrary-length array. See <see cref="OpenClFourier.Ifft(Vector2[], int)"/>.
        /// </summary>
        public float[]? Ifft(Vector2[] data, int originalLength = -1)
        {
            return this.EnsureFourier()?.Ifft(data, originalLength);
        }

        /// <summary>
        /// Runs a chunked forward FFT. See <see cref="OpenClFourier.FftChunked(float[], int, int)"/>.
        /// </summary>
        public Vector2[]? FftChunked(float[] data, int chunkSize = OpenClFourier.DefaultChunkSize, int overlap = 0)
        {
            return this.EnsureFourier()?.FftChunked(data, chunkSize, overlap);
        }

        /// <summary>
        /// Runs a chunked inverse FFT. See <see cref="OpenClFourier.IfftChunked(Vector2[], int, int)"/>.
        /// </summary>
        public float[]? IfftChunked(Vector2[] data, int chunkSize = OpenClFourier.DefaultChunkSize, int overlap = 0)
        {
            return this.EnsureFourier()?.IfftChunked(data, chunkSize, overlap);
        }

        /// <summary>
        /// Asynchronously runs a full forward FFT. See <see cref="OpenClFourier.FftAsync(float[])"/>.
        /// </summary>
        public Task<Vector2[]?> FftAsync(float[] data)
        {
            OpenClFourier? fourier = this.EnsureFourier();
            return fourier != null ? fourier.FftAsync(data) : Task.FromResult<Vector2[]?>(null);
        }

        /// <summary>
        /// Asynchronously runs a full inverse FFT. See <see cref="OpenClFourier.IfftAsync(Vector2[], int)"/>.
        /// </summary>
        public Task<float[]?> IfftAsync(Vector2[] data, int originalLength = -1)
        {
            OpenClFourier? fourier = this.EnsureFourier();
            return fourier != null ? fourier.IfftAsync(data, originalLength) : Task.FromResult<float[]?>(null);
        }



        // Image kernels
        /// <summary>
        /// Executes an image kernel over an uploaded byte image buffer using linear 1D dispatch
        /// (global size = width*height, one work-item per pixel).
        /// For float-buffer (in-place) kernels the argument order is
        /// <c>[image, width, height, channels, ...extraArgs]</c>. For dual-buffer (in/out) kernels an
        /// output buffer is allocated, the argument order is <c>[input, output, width, height, channels, ...extraArgs]</c>,
        /// and the result is copied back into the input buffer so the original pointer stays valid.
        /// </summary>
        /// <param name="kernelName">The image kernel to run.</param>
        /// <param name="indexPointer">The native handle of the image device buffer (input, and output when float-buffer).</param>
        /// <param name="arguments">The additional scalar arguments in kernel order (after channels).</param>
        /// <param name="width">The image width in pixels.</param>
        /// <param name="height">The image height in pixels.</param>
        /// <param name="channels">The number of channels per pixel.</param>
        /// <param name="dualBuffer">Whether the kernel expects a separate input and output buffer.</param>
        /// <returns>The input <paramref name="indexPointer"/> on success; otherwise <c>null</c>.</returns>
        public IntPtr? ExecuteImageKernel(string kernelName, IntPtr indexPointer, object[] arguments, int width, int height, int channels, bool dualBuffer)
        {
            if (!this.Online || this._register == null || this._launcher == null)
            {
                StaticLogger.LogError("OpenClService: cannot execute image kernel - service is offline.");
                return null;
            }

            IRuntimeMem? input = this._register[indexPointer] as IRuntimeMem;
            if (input == null)
            {
                StaticLogger.LogError($"ExecuteImageKernel: no allocation found for handle {indexPointer}.");
                return null;
            }

            long globalWorkSize = (long) width * height;
            if (globalWorkSize <= 0)
            {
                StaticLogger.LogError($"ExecuteImageKernel '{kernelName}': invalid dimensions {width}x{height}.");
                return null;
            }

            IRuntimeMem? output = null;
            try
            {
                var args = new List<object> { input };

                if (dualBuffer)
                {
                    output = this._register.AllocateSingle<Byte>(input.IndexLength) as IRuntimeMem;
                    if (output == null)
                    {
                        StaticLogger.LogError($"ExecuteImageKernel '{kernelName}': failed to allocate output buffer.");
                        return null;
                    }

                    args.Add(output);
                }

                args.Add(width);
                args.Add(height);
                args.Add(channels);
                if (arguments != null)
                {
                    args.AddRange(arguments);
                }

                bool ok = this._launcher.Execute(kernelName, globalWorkSize, args.ToArray());
                if (!ok)
                {
                    return null;
                }

                if (dualBuffer && output != null)
                {
                    // Copy the out-of-place result back into the input buffer so the caller's pointer stays valid.
                    Byte[]? result = this._register.PullData<Byte>(output.IndexPointer, keepBuffer: false);
                    if (result == null)
                    {
                        StaticLogger.LogError($"ExecuteImageKernel '{kernelName}': failed to read output buffer.");
                        return null;
                    }

                    output = null; // freed by PullData

                    if (!this._register.WriteData(indexPointer, result))
                    {
                        StaticLogger.LogError($"ExecuteImageKernel '{kernelName}': failed to write result back to input buffer.");
                        return null;
                    }
                }

                return indexPointer;
            }
            catch (Exception ex)
            {
                StaticLogger.LogError($"ExecuteImageKernel '{kernelName}' failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (output != null)
                {
                    this._register.FreeMemory(output.IndexPointer);
                }
            }
        }



        // Memory transfer (float)
        /// <summary>
        /// Uploads a host array to the device as a float buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The host data to upload.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline or the upload fails.</returns>
        public IRuntimeMem? PushData<T>(T[] data) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot push data - service is offline.");
                return null;
            }

            return this._register.PushData(data) as IRuntimeMem;
        }

        /// <summary>
        /// Downloads a float device buffer back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of the buffer to read.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>The downloaded host array, or <c>null</c> if the service is offline.</returns>
        public T[]? PullData<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot pull data - service is offline.");
                return null;
            }

            return this._register.PullData<T>(indexPointer, keepBuffer);
        }

        /// <summary>
        /// Downloads the buffer described by the given memory object back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object whose primary buffer should be read.</param>
        /// <returns>The downloaded host array, or <c>null</c> if the service is offline.</returns>
        public T[]? PullData<T>(IRuntimeMem mem) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot pull data - service is offline.");
                return null;
            }

            return this._register.PullData<T>(mem);
        }

        /// <summary>
        /// Allocates a float uninitialized device buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="elementCount">The number of elements to allocate.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline.</returns>
        public IRuntimeMem? AllocateSingle<T>(int elementCount) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot allocate memory - service is offline.");
                return null;
            }

            return this._register.AllocateSingle<T>(elementCount);
        }


        // Memory transfer (group / chunks)
        /// <summary>
        /// Uploads several host data chunks to the device as a group of buffers.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The collection of host data chunks to upload.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline.</returns>
        public IRuntimeMem? PushChunks<T>(IEnumerable<T[]> data) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot push data - service is offline.");
                return null;
            }

            return this._register.PushChunks(data);
        }

        /// <summary>
        /// Downloads a grouped device allocation back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>The downloaded chunks, or <c>null</c> if the service is offline.</returns>
        public T[][] PullChunks<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot pull data - service is offline.");
                return [];
            }

            return this._register.PullChunks<T>(indexPointer, keepBuffer).ToArray();
        }

        /// <summary>
        /// Downloads the grouped allocation described by the given memory object back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object describing the grouped allocation.</param>
        /// <returns>The downloaded chunks, or <c>null</c> if the service is offline.</returns>
        public T[][] PullChunks<T>(IRuntimeMem mem) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot pull data - service is offline.");
                return [];
            }

            return this._register.PullChunks<T>(mem)?.ToArray() ?? [];
        }

        /// <summary>
        /// Allocates a group of uninitialized device buffers (one per supplied length).
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <returns>The registered <see cref="IRuntimeMem"/>, or <c>null</c> if the service is offline.</returns>
        public IRuntimeMem? AllocateGroup<T>(long[] lengths) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot allocate memory - service is offline.");
                return null;
            }

            return this._register.AllocateGroup<T>(lengths);
        }


        // Memory transfer (float) ((async))
        /// <summary>
        /// Asynchronously uploads a host array to the device as a float buffer.
        /// </summary>
        public async Task<IRuntimeMem?> PushDataAsync<T>(T[] data) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot push data - service is offline.");
                return null;
            }

            return await this._register.PushDataAsync(data);
        }

        /// <summary>
        /// Asynchronously downloads a float device buffer back to the host.
        /// </summary>
        public async Task<T[]?> PullDataAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot pull data - service is offline.");
                return null;
            }

            return await this._register.PullDataAsync<T>(indexPointer, keepBuffer);
        }

        /// <summary>
        /// Asynchronously allocates a float uninitialized device buffer.
        /// </summary>
        public Task<IRuntimeMem?> AllocateSingleAsync<T>(int elementCount) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot allocate memory - service is offline.");
                return Task.FromResult<IRuntimeMem?>(null);
            }

            return this._register.AllocateSingleAsync<T>(elementCount);
        }


        // Memory transfer (group / chunks) ((async))
        /// <summary>
        /// Asynchronously uploads several host data chunks to the device as a group of buffers.
        /// </summary>
        public async Task<IRuntimeMem?> PushChunksAsync<T>(IEnumerable<T[]> data) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot push data - service is offline.");
                return null;
            }

            return await this._register.PushChunksAsync(data);
        }

        /// <summary>
        /// Asynchronously downloads a grouped device allocation back to the host as separate chunks.
        /// </summary>
        public async Task<T[][]> PullChunksAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot pull data - service is offline.");
                return [];
            }

            return (await this._register.PullChunksAsync<T>(indexPointer, keepBuffer)).ToArray();
        }

        /// <summary>
        /// Asynchronously allocates a group of uninitialized device buffers (one per supplied length).
        /// </summary>
        public async Task<IRuntimeMem?> AllocateGroupAsync<T>(long[] lengths) where T : unmanaged
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot allocate memory - service is offline.");
                return null;
            }

            return await this._register.AllocateGroupAsync<T>(lengths);
        }


        // Free
        /// <summary>
        /// Frees the device memory that owns the given native handle.
        /// </summary>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if the service is offline.</returns>
        public long FreeMemory(IntPtr indexPointer)
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot free memory - service is offline.");
                return 0;
            }

            return this._register.FreeMemory(indexPointer);
        }

        /// <summary>
        /// Frees the device memory with the given id.
        /// </summary>
        /// <param name="id">The unique id of the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if the service is offline.</returns>
        public long FreeMemory(Guid id)
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot free memory - service is offline.");
                return 0;
            }

            return this._register.FreeMemory(id);
        }

        /// <summary>
        /// Frees the device memory described by the given memory object.
        /// </summary>
        /// <param name="mem">The memory object to free.</param>
        /// <returns>The number of bytes freed, or 0 if the service is offline.</returns>
        public long FreeMemory(IRuntimeMem mem)
        {
            if (!this.Online || this._register == null)
            {
                StaticLogger.LogError("OpenClService: cannot free memory - service is offline.");
                return 0;
            }

            return this._register.FreeMemory(mem);
        }



        // Helpers
        /// <summary>
        /// Returns the Fourier helper, logging an error when the service is offline.
        /// </summary>
        private OpenClFourier? EnsureFourier()
        {
            if (this._fourier == null)
            {
                StaticLogger.LogError("OpenClService: service is offline. Call Initialize(...) first.");
            }

            return this._fourier;
        }



        // Disposal
        /// <summary>
        /// Disposes the service, releasing all OpenCL resources.
        /// </summary>
        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this.Shutdown();
            this._disposed = true;
        }
    }
}
