using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using ManagedCuda;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// High-level public facade for the AsynCUDA13 runtime. A <see cref="CudaService"/> owns the CUDA primary
    /// context for a selected device and coordinates the underlying components (<see cref="CudaRegister"/>,
    /// <see cref="CudaFourier"/>, <see cref="CudaCompiler"/> and <see cref="CudaLauncher"/>). It exposes device
    /// discovery, initialization, memory transfer (push/pull), allocation and free operations in both synchronous
    /// and asynchronous forms, guarding every call with an online/availability check.
    /// </summary>
    public class CudaService : ICudaService, IDisposable
    {
        // Static CUDA properties
        /// <summary>Gets the number of CUDA-capable devices available on the system.</summary>
        public static int DeviceCount => CudaContext.GetDeviceCount();

        /// <summary>Gets the properties of every available device keyed by device id.</summary>
        internal static Dictionary<int, CudaDeviceProperties> AvailableDevicesProps => GetAvailableDevicesProperties();

        /// <summary>Gets the installed CUDA driver version.</summary>
        public static Version CudaDriverVersion => CudaContext.GetDriverVersion();

        /// <summary>Gets or sets a value indicating whether log messages are suppressed from the in-memory UI list.</summary>
        public static bool SilenceLogging { get; set; } = false;

        // Instance Properties
        /// <summary>Gets the id of the currently selected device, or -1 if the service is offline.</summary>
        public int SelectedDeviceId { get; private set; } = -1;

        /// <summary>
        /// Gets the name of the currently selected device, or <c>null</c> if none is selected.
        /// </summary>
        public string? SelectedDeviceName => this.SelectedCudaDeviceProperties?.DeviceName;

        /// <summary>Gets the properties of the currently selected device, or <c>null</c> if none is selected.</summary>
        internal CudaDeviceProperties? SelectedCudaDeviceProperties => this[this.SelectedDeviceId];

        public Dictionary<string, string> SelectedDeviceProperties => this.SelectedCudaDeviceProperties?.GetType()
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .ToDictionary(
                prop => prop.Name,
                prop => prop.GetValue(this.SelectedCudaDeviceProperties)?.ToString() ?? string.Empty) ?? [];

        public Dictionary<int, Dictionary<string, string>> TotalAvailableDeviceProperties => AvailableDevicesProps
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value.GetType()
                    .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    .ToDictionary(
                        prop => prop.Name,
                        prop => prop.GetValue(kv.Value)?.ToString() ?? string.Empty)
            );

        /// <summary>Gets the CUDA primary context backing this service, or <c>null</c> when offline.</summary>
        private PrimaryContext? _context { get; set; } = null;

        /// <summary>Gets a value indicating whether the service has an initialized context and a valid selected device.</summary>
        public bool Online => this._context != null && this.SelectedDeviceId >= 0;

        /// <summary>Gets a bindable list of human-readable descriptions of the available devices.</summary>
        public BindingList<string> DeviceEntries { get; private set; } = new BindingList<string>(
            GetAvailableDevicesProperties()
            .Select(kv => $"[{kv.Key}] {kv.Value.DeviceName} - {kv.Value.TotalGlobalMemory / (1024 * 1024)} MB")
            .ToList()
        );



        // Accessors
        /// <summary>Gets the registered memory object that owns the given native handle, or <c>null</c>.</summary>
        /// <param name="indexPointer">The native handle to look up.</param>
        public IRuntimeMem? this[IntPtr indexPointer] => this._register?[indexPointer];

        /// <summary>Gets the registered memory object with the given id, or <c>null</c>.</summary>
        /// <param name="id">The unique id of the memory object.</param>
        public IRuntimeMem? this[Guid id] => this._register?[id];

        /// <summary>Gets the registered memory object that matches the given string, which may be either a native handle or a unique id, or <c>null</c>.</summary>
        /// <param name="indexPointerOrId">The string representation of either a native handle or a unique id.</param>
        public IRuntimeMem? this[string indexPointerOrId] => this._register?[indexPointerOrId];

        /// <summary>Gets the total number of bytes currently allocated by the registry.</summary>
        public long TotalAllocatedBytes => this._register?.TotalAllocatedBytes ?? 0;

        /// <summary>Gets the number of registered memory objects.</summary>
        public int TotalAllocations => this._register?.AllocationCount ?? 0;

        /// <summary>Gets the number of streams with at least one outstanding operation.</summary>
        public int ThreadsActive => this._register?.ThreadsActive ?? 0;

        /// <summary>Gets the number of idle streams.</summary>
        public int ThreadsIdle => this._register?.ThreadsIdle ?? 0;

        /// <summary>Gets the maximum number of threads per multiprocessor reported by the active device.</summary>
        public int MaxThreads => this._register?.MaxThreads ?? 0;

        private static readonly BindingList<long> EmptyMemorySizes = [];
        private static readonly BindingList<int> EmptyStreamThreads = [];

        /// <summary>Gets the bindable list of allocation sizes (bytes), or an empty list when offline.</summary>
        internal BindingList<long> MemorySizesList => this._register?.MemorySizesList ?? EmptyMemorySizes;

        /// <summary>Gets the bindable list of per-stream outstanding-operation counts, or an empty list when offline.</summary>
        public BindingList<int> StreamThreadsList => this._register?.StreamThreadsList ?? EmptyStreamThreads;

        /// <summary>Gets a snapshot of the currently registered device memory allocations, or an empty list when offline.</summary>
        public IReadOnlyCollection<IRuntimeMem> RegisteredMemory => this._register?.AllocationsBindingList.ToArray() ?? [];


        // Fields
        /// <summary>The memory and stream registry; created during initialization.</summary>
        internal CudaRegister? _register { get; private set; } = null;
        public IRuntimeRegister Register => this._register ?? throw new InvalidOperationException("CudaService is offline; Register is unavailable.");

        /// <summary>The Fourier transform helper; created during initialization.</summary>
        internal CudaFourier? _fourier { get; private set; } = null;
        public IRuntimeFourier Fourier => this._fourier ?? throw new InvalidOperationException("CudaService is offline; Fourier is unavailable.");

        /// <summary>The kernel compiler/loader; created during initialization.</summary>
        internal CudaCompiler? _compiler { get; private set; } = null;
        public IRuntimeCompiler Compiler => this._compiler ?? throw new InvalidOperationException("CudaService is offline; Compiler is unavailable.");

        /// <summary>The kernel launcher; created during initialization.</summary>
        internal CudaLauncher? _launcher { get; private set; } = null;
        public IRuntimeLauncher Launcher => this._launcher ?? throw new InvalidOperationException("CudaService is offline; Launcher is unavailable.");



        // Enumerables
        /// <summary>Gets the properties of the device with the given id, or <c>null</c> if it does not exist.</summary>
        /// <param name="deviceId">The device id to look up.</param>
        public CudaDeviceProperties? this[int deviceId] => GetAvailableDevicesProperties().GetValueOrDefault(deviceId);

        /// <summary>Gets the properties of the device with the given name (case-insensitive), or <c>null</c> if not found.</summary>
        /// <param name="deviceName">The device name to look up.</param>
        public CudaDeviceProperties? this[uint deviceId] => GetAvailableDevicesProperties().GetValueOrDefault((int) deviceId);

        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="CudaService"/> class in an offline state.
        /// </summary>
        public CudaService()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaService"/> class, optionally initializing a device immediately.
        /// </summary>
        /// <param name="preferredDeviceIndex">The device id to initialize; when negative, the service starts offline.</param>
        public CudaService(int preferredDeviceIndex)
        {
            if (preferredDeviceIndex >= 0)
            {
                this.Initialize(preferredDeviceIndex);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaService"/> class and initializes the device matching the given name.
        /// </summary>
        /// <param name="preferredDeviceName">The name (or id string) of the device to initialize.</param>
        public CudaService(string preferredDeviceName)
        {
            if (!string.IsNullOrWhiteSpace(preferredDeviceName))
            {
                this.Initialize(preferredDeviceName);
            }
        }



        // Methods (static)
        /// <summary>
        /// Enumerates all CUDA-capable devices and returns their properties keyed by device id.
        /// </summary>
        /// <returns>A dictionary mapping each device id to its <see cref="CudaDeviceProperties"/>.</returns>
        public static Dictionary<int, CudaDeviceProperties> GetAvailableDevicesProperties()
        {
            var deviceProps = new Dictionary<int, CudaDeviceProperties>();
            int deviceCount = DeviceCount;
            for (int i = 0; i < deviceCount; i++)
            {
                using var context = new CudaContext(i);
                var props = context.GetDeviceInfo();
                deviceProps[i] = props;
            }
            return deviceProps;
        }


        // Methods (instance)
        /// <summary>
        /// Disposes the CUDA context and all owned components (launcher, compiler, Fourier helper and registry),
        /// resets the selected device and suppresses finalization.
        /// </summary>
        public void Dispose()
        {
            // Dispose the context-dependent components BEFORE the primary context: the registry frees its
            // device buffers through this.Context.FreeMemory(...), so tearing the context down first would
            // make every free throw 'Cannot access a disposed object: ManagedCuda.PrimaryContext' and leak VRAM.
            this._launcher?.Dispose();
            this._launcher = null;
            StaticLogger.Log("CudaService: Disposed Launcher");
            this._compiler?.Dispose();
            this._compiler = null;
            StaticLogger.Log("CudaService: Disposed Compiler");
            this._fourier?.Dispose();
            this._fourier = null;
            StaticLogger.Log("CudaService: Disposed Fourier");
            this._register?.Dispose();
            this._register = null;
            StaticLogger.Log("CudaService: Disposed Register");

            // Now that all owned components have released their device resources, dispose the context last.
            if (this._context != null)
            {
                this._context.Dispose();
                this._context = null;

                StaticLogger.Log("CudaService: Disposed CUDA context");
            }

            this.SelectedDeviceId = -1;

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Initializes the service on the specified device id, creating the primary context and all dependent components.
        /// Any previously initialized context is disposed first.
        /// </summary>
        /// <param name="deviceId">The id of the device to initialize.</param>
        /// <returns><c>true</c> if initialization succeeded; otherwise <c>false</c>.</returns>
        public bool Initialize(int deviceId = -1)
        {
            if (deviceId < 0 || deviceId >= DeviceCount || DeviceCount <= 0)
            {
                if (deviceId >= DeviceCount)
                {
                    StaticLogger.Log($"CudaService: Invalid device ID {deviceId} for initialization");
                    return false;
                }

                if (DeviceCount <= 0)
                {
                    StaticLogger.Log("CudaService: No CUDA devices available for initialization");
                    return false;
                }

                this.Dispose();
                this.SelectedDeviceId = -1;
                StaticLogger.Log("CudaService: Disposed <offline>");
                return false;
            }

            try
            {
                if (this._context != null)
                {
                    StaticLogger.Log($"CudaService: Re-initializing from device ID {this.SelectedDeviceId} to device ID {deviceId}");
                    this.Dispose();
                }

                this._context = new PrimaryContext(deviceId);
                this.SelectedDeviceId = deviceId;
                // Initialize other objects
                this._register = new CudaRegister(this._context);
                this._fourier = new CudaFourier(this._context, this._register);
                this._compiler = new CudaCompiler(this._context);
                this._launcher = new CudaLauncher(this._context, this._register, this._fourier, this._compiler);
                StaticLogger.Log($"CudaService: Initialized on device ID {deviceId} ({this.SelectedCudaDeviceProperties?.DeviceName})");
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"CudaService: Failed to initialize on device ID {deviceId}", ex);
                this.Dispose();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Initializes the service on the device that matches the given name. The name may also be a numeric id string.
        /// </summary>
        /// <param name="name">The device name to match, or a numeric id as a string.</param>
        /// <param name="exactMatch">If <c>true</c>, requires an exact (case-insensitive) name match; otherwise matches by substring.</param>
        /// <returns><c>true</c> if a device was found and initialized; otherwise <c>false</c>.</returns>
        public bool Initialize(string name, bool exactMatch = false)
        {
            int index = -1;

            // Try parse name as float int id
            if (int.TryParse(name, out index))
            {
                StaticLogger.Log("Name string was id");
                return this.Initialize(index);
            }

            if (exactMatch)
            {
                var deviceEntry = AvailableDevicesProps.FirstOrDefault(
                    kv => kv.Value.DeviceName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (deviceEntry.Value != null)
                {
                    index = deviceEntry.Key;
                }
                else
                {
                    StaticLogger.Log($"CudaService: No device found with exact name '{name}'");
                    return false;
                }
            }
            else
            {
                // Match by contains & ignore case
                var deviceEntry = AvailableDevicesProps.FirstOrDefault(
                    kv => kv.Value.DeviceName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (deviceEntry.Value != null)
                {
                    index = deviceEntry.Key;
                }
                else
                {
                    StaticLogger.Log($"CudaService: No device found containing name '{name}'");
                    return false;
                }
            }

            return this.Initialize(index);
        }



        // Accessors (float)
        /// <summary>
        /// Uploads a sequence of host data to the device as a float buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The host data to upload.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline or the upload fails.</returns>
        public IRuntimeMem? PushData<T>(IEnumerable<T> data) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot push data - service is offline");
                return null;
            }

            return this._register.PushData(data);
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
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return this._register.PullData<T>(indexPointer, keepBuffer);
        }

        /// <summary>
        /// Downloads the buffer described by the given memory object back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object whose primary buffer should be read.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>The downloaded host array, or <c>null</c> if the service is offline.</returns>
        public T[]? PullData<T>(CudaMem mem, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }

            return this._register.PullData<T>(mem.IndexPointer, keepBuffer);
        }

        /// <summary>
        /// Allocates a float uninitialized device buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="elementCount">The number of elements to allocate.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public IRuntimeMem? AllocateSingle<T>(int elementCount) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot allocate memory - service is offline");
                return null;
            }
            return this._register.AllocateSingle<T>(elementCount);
        }

        /// <summary>
        /// Sets the CUDA primary context as the current context for the calling thread.
        /// This is required before any CUDA operations on the calling thread.
        /// </summary>
        /// <returns><c>true</c> if the context was set successfully; <c>false</c> if the service is offline.</returns>
        public void SetCurrent()
        {
            if (!this.Online || this._context == null)
            {
                StaticLogger.Log("CudaService: Cannot set current context - service is offline");
                return;
            }

            this._context.SetCurrent();
        }

        /// <summary>
        /// Blocks until all previously issued work on the primary context has completed.
        /// </summary>
        /// <remarks>
        /// Kernels launched via the default stream (for example by the GPU-database executor) run
        /// asynchronously, so a device synchronization is required before their results can be read
        /// back safely. Mirrors the synchronize step performed by <see cref="CudaLauncher"/>.
        /// </remarks>
        /// <returns><c>true</c> if the context was synchronized; <c>false</c> if the service is offline.</returns>
        public bool Synchronize()
        {
            if (!this.Online || this._context == null)
            {
                StaticLogger.Log("CudaService: Cannot synchronize - service is offline");
                return false;
            }

            this._context.Synchronize();
            return true;
        }


        // Accessors (group / chunks)
        /// <summary>
        /// Uploads several host data chunks to the device as a group of buffers.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The collection of host data chunks to upload.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public IRuntimeMem? PushChunks<T>(IEnumerable<T[]> data) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot push data - service is offline");
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
        public IEnumerable<T[]>? PullChunks<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return this._register.PullChunks<T>(indexPointer, keepBuffer);
        }

        /// <summary>
        /// Downloads the grouped allocation described by the given memory object back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object describing the grouped allocation.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>The downloaded chunks, or <c>null</c> if the service is offline.</returns>
        public IEnumerable<T[]>? PullChunks<T>(CudaMem mem, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return this._register.PullChunks<T>(mem.IndexPointer, keepBuffer);
        }

        /// <summary>
        /// Allocates a group of uninitialized device buffers (one per supplied length).
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public IRuntimeMem? AllocateGroup<T>(IntPtr[] lengths) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot allocate memory - service is offline");
                return null;
            }
            return this._register.AllocateGroup<T>(lengths);
        }


        // Accessors (float) ((async))
        /// <summary>
        /// Asynchronously uploads a sequence of host data to the device as a float buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The host data to upload.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public async Task<IRuntimeMem?> PushDataAsync<T>(IEnumerable<T> data) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot push data - service is offline");
                return null;
            }

            return await this._register.PushDataAsync(data);
        }

        /// <summary>
        /// Asynchronously downloads a float device buffer back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of the buffer to read.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>A task producing the downloaded host array, or <c>null</c> if the service is offline.</returns>
        public async Task<T[]?> PullDataAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return await this._register.PullDataAsync<T>(indexPointer, keepBuffer);
        }

        /// <summary>
        /// Asynchronously downloads the buffer described by the given memory object back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="cudaMem">The memory object whose primary buffer should be read.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>A task producing the downloaded host array, or <c>null</c> if the service is offline.</returns>
        public async Task<T[]?> PullDataAsync<T>(CudaMem cudaMem, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return await this._register.PullDataAsync<T>(cudaMem.IndexPointer, keepBuffer);
        }

        /// <summary>
        /// Asynchronously allocates a float uninitialized device buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="elementCount">The number of elements to allocate.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public async Task<IRuntimeMem?> AllocateSingleAsync<T>(IntPtr elementCount) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot allocate memory - service is offline");
                return null;
            }
            return await this._register.AllocateSingleAsync<T>(elementCount);
        }


        // Accessors (group / chunks) ((async))
        /// <summary>
        /// Asynchronously uploads several host data chunks to the device as a group of buffers.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The collection of host data chunks to upload.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public async Task<IRuntimeMem?> PushChunksAsync<T>(IEnumerable<T[]> data) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot push data - service is offline");
                return null;
            }
            return await this._register.PushChunksAsync(data);
        }

        /// <summary>
        /// Asynchronously downloads a grouped device allocation back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>A task producing the downloaded chunks, or <c>null</c> if the service is offline.</returns>
        public async Task<IEnumerable<T[]>?> PullChunksAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return await this._register.PullChunksAsync<T>(indexPointer, keepBuffer);
        }

        /// <summary>
        /// Asynchronously downloads the grouped allocation described by the given memory object back to the host as separate chunks.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="mem">The memory object describing the grouped allocation.</param>
        /// <param name="keepBuffer">If <c>true</c>, the device memory is retained after the copy.</param>
        /// <returns>A task producing the downloaded chunks, or <c>null</c> if the service is offline.</returns>
        public async Task<IEnumerable<T[]>?> PullChunksAsync<T>(CudaMem mem, bool keepBuffer = false) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot pull data - service is offline");
                return null;
            }
            return await this._register.PullChunksAsync<T>(mem.IndexPointer, keepBuffer);
        }

        /// <summary>
        /// Asynchronously allocates a group of uninitialized device buffers (one per supplied length).
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> if the service is offline.</returns>
        public async Task<IRuntimeMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot allocate memory - service is offline");
                return null;
            }
            return await this._register.AllocateGroupAsync<T>(lengths);
        }


        // Accessors (free)
        /// <summary>
        /// Frees the device memory described by the given memory object.
        /// </summary>
        /// <param name="mem">The memory object to free.</param>
        /// <returns>The number of bytes freed, or 0 if the service is offline.</returns>
        public long FreeMemory(CudaMem mem)
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return 0;
            }
            return this._register.FreeMemory(mem);
        }

        /// <summary>
        /// Frees the device memory that owns the given native handle.
        /// </summary>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if the service is offline.</returns>
        public long FreeMemory(IntPtr indexPointer)
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return 0;
            }
            return this._register.FreeMemory(indexPointer);
        }

        /// <summary>
        /// Frees the device memory of the allocation with the given id.
        /// </summary>
        /// <param name="id">The unique id of the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if the service is offline.</returns>
        public long FreeMemory(Guid id)
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return 0;
            }
            return this._register.FreeMemory(id);
        }

        /// <summary>
        /// Asynchronously frees the device memory of the allocation with the given id.
        /// </summary>
        /// <param name="id">The unique id of the allocation to free.</param>
        /// <returns>A task producing the number of bytes freed, or 0 if the service is offline.</returns>
        public async Task<long> FreeMemoryAsync(Guid id)
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return 0;
            }
            return await Task.Run(() => this._register.FreeMemory(id));
        }

        /// <summary>
        /// Asynchronously frees the device memory that owns the given native handle.
        /// </summary>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to free.</param>
        /// <returns>A task producing the number of bytes freed, or 0 if the service is offline.</returns>
        public async Task<long> FreeMemoryAsync(IntPtr indexPointer)
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return 0;
            }
            return await Task.Run(() => this._register.FreeMemory(indexPointer));
        }

        /// <summary>
        /// Frees all allocated device memory.
        /// </summary>
        public void FreeAllMemory()
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return;
            }

            foreach (var item in this._register.AllocationsBindingList)
            {
                try
                {
                    this._register.FreeMemory(item);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log($"CudaService: Exception while freeing memory for item {item.Id}", ex);
                }
            }
        }

        /// <summary>
        /// Asynchronously frees all allocated device memory.
        /// </summary>
        public async Task FreeAllMemoryAsync()
        {
            if (!this.Online || this._context == null || this._register == null)
            {
                StaticLogger.Log("CudaService: Cannot free memory - service is offline");
                return;
            }
            var tasks = this._register.AllocationsBindingList.Select(item => Task.Run(() =>
            {
                try
                {
                    this._register.FreeMemory(item);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log($"CudaService: Exception while freeing memory for item {item.Id}", ex);
                }
            })).ToArray();
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Checks if CUDA is available on the system.
        /// </summary>
        /// <returns>True if CUDA is available, false otherwise.</returns>
        public bool IsCudaAvailable()
        {
            return CudaAvailabilityTester.IsCudaAvailable();
        }

        /// <summary>
        /// Gets information about all available CUDA devices on the system.
        /// </summary>
        /// <returns>Array of device information, or empty array if CUDA is not available.</returns>
        public RuntimeDeviceInfo[] GetAllDeviceInfos()
        {
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                return [];
            }

            var infos = GetAvailableDevicesProperties().Select(props => new RuntimeDeviceInfo
            {
                DeviceId = props.Key,
                DeviceName = props.Value.DeviceName,
                Properties = props.Value.GetType()
                    .GetProperties()
                    .ToDictionary(
                        prop => prop.Name,
                        prop => prop.GetValue(props.Value)?.ToString() ?? string.Empty)
            }).ToArray();

            return infos;
        }

    }
}
