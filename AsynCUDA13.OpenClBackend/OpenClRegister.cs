using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenTK.Compute.OpenCL;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Central registry that owns the OpenCL context and command queue for a float selected device and
    /// tracks every <see cref="IRuntimeMem"/> allocation. Provides synchronous and asynchronous helpers for
    /// allocating, pushing, pulling and freeing device memory.
    /// </summary>
    public sealed class OpenClRegister : IRuntimeRegister, IDisposable
    {
        private readonly IRollingFileMemoryLogger _logger;
        private readonly ConcurrentDictionary<Guid, IRuntimeMem> _allocations = new();
        private bool _disposed;

        /// <summary>
        /// Gets the OpenCL context used for all allocations.
        /// </summary>
        internal CLContext Context { get; }

        /// <summary>
        /// Gets the command queue used for all transfers and kernel launches.
        /// </summary>
        internal CLCommandQueue Queue { get; }

        /// <summary>
        /// Gets the device this registry operates on.
        /// </summary>
        internal CLDevice Device { get; }

        /// <summary>
        /// Gets the number of currently tracked allocations.
        /// </summary>
        public int AllocationCount => this._allocations.Count;

        /// <summary>
        /// Gets a snapshot of all currently tracked allocations.
        /// </summary>
        public IReadOnlyCollection<Shared.Interfaces.IRuntimeMem> Allocations => this._allocations.Values.ToArray();

        /// <summary>
        /// Gets the total number of bytes currently allocated across all tracked buffers.
        /// </summary>
        public long TotalAllocatedBytes
        {
            get
            {
                long total = 0;
                foreach (var mem in this._allocations.Values)
                {
                    total += mem.TotalSize;
                }

                return total;
            }
        }



        // Accessors
        /// <summary>
        /// Gets the tracked allocation whose id matches <paramref name="id"/>, or <c>null</c> if none is found.
        /// </summary>
        /// <param name="id">The unique id of the allocation.</param>
        public Shared.Interfaces.IRuntimeMem? this[Guid id] => this._allocations.TryGetValue(id, out var mem) ? mem : this._allocations.Values.FirstOrDefault(m => m.AssetReferenceIds.Equals(id));

        /// <summary>
        /// Gets the tracked allocation that owns the given native buffer handle, or <c>null</c> if none is found.
        /// </summary>
        /// <param name="indexPointer">The native handle of any buffer belonging to the allocation.</param>
        public Shared.Interfaces.IRuntimeMem? this[IntPtr indexPointer]
        {
            get
            {
                if (indexPointer == 0)
                {
                    return null;
                }

                foreach (var mem in this._allocations.Values)
                {
                    foreach (var pointer in mem.PointerIds)
                    {
                        if (pointer == indexPointer)
                        {
                            return mem;
                        }
                    }
                }

                return null;
            }
        }



        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClRegister"/> class for the given context, queue and device.
        /// </summary>
        internal OpenClRegister(CLContext context, CLCommandQueue queue, CLDevice device, IRollingFileMemoryLogger logger)
        {
            this.Context = context;
            this.Queue = queue;
            this.Device = device;
            this._logger = logger;
        }



        // Allocation
        /// <summary>
        /// Allocates a float uninitialized device buffer holding <paramref name="length"/> elements of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="length">The number of elements to allocate.</param>
        /// <param name="flags">The OpenCL memory flags (defaults to read/write).</param>
        /// <returns>The created <see cref="IRuntimeMem"/>, or <c>null</c> if allocation failed.</returns>
        internal Shared.Interfaces.IRuntimeMem? AllocateSingle<T>(long length, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            if (length <= 0)
            {
                this._logger.LogError($"AllocateSingle: invalid length {length}.");
                return null;
            }

            int elementSize = Marshal.SizeOf<T>();
            IntPtr size = new((int) (length * elementSize));

            CLBuffer buffer = CL.CreateBuffer(this.Context, flags, (nuint) size, IntPtr.Zero, out CLResultCode result);
            if (result != CLResultCode.Success)
            {
                this._logger.LogError($"AllocateSingle: CreateBuffer failed ({result}).");
                return null;
            }

            IRuntimeMem mem = new OpenClMem(buffer, length, typeof(T));
            this._allocations[mem.Id] = mem;
            return mem;
        }

        public Shared.Interfaces.IRuntimeMem? AllocateSingle<T>(IntPtr length) where T : unmanaged
        {
            return this.AllocateSingle<T>((long) length);
        }

        /// <summary>
        /// Allocates a float device buffer and copies <paramref name="data"/> into it.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="data">The host data to upload.</param>
        /// <param name="flags">The OpenCL memory flags (defaults to read/write).</param>
        /// <returns>The created <see cref="IRuntimeMem"/>, or <c>null</c> if allocation or upload failed.</returns>
        internal Shared.Interfaces.IRuntimeMem? PushData<T>(T[] data, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            if (data == null || data.Length == 0)
            {
                this._logger.LogError("PushData: data is null or empty.");
                return null;
            }

            if (this.AllocateSingle<T>(data.Length, flags) is not IRuntimeMem mem)
            {
                return null;
            }

            CLResultCode result = CL.EnqueueWriteBuffer(this.Queue, ((OpenClMem) mem).IndexBuffer, true, 0, data, null, out _);
            if (result != CLResultCode.Success)
            {
                this._logger.LogError($"PushData: EnqueueWriteBuffer failed ({result}).");
                this.Free(mem);
                return null;
            }

            return mem;
        }

        public Shared.Interfaces.IRuntimeMem? PushData<T>(IEnumerable<T> data) where T : unmanaged
        {
            return this.PushData(data.ToArray());
        }

        /// <summary>
        /// Reads the contents of a float-buffer allocation back into a host array.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="mem">The allocation to read from.</param>
        /// <returns>The host data, or <c>null</c> if the read failed.</returns>
        public T[]? PullData<T>(IRuntimeMem mem) where T : unmanaged
        {
            if (mem == null || mem.Count == 0)
            {
                this._logger.LogError("PullData: allocation is null or empty.");
                return null;
            }

            T[] result = new T[mem.IndexLength];
            CLResultCode code = CL.EnqueueReadBuffer(this.Queue, ((OpenClMem) mem).IndexBuffer, true, 0, result, null, out _);
            if (code != CLResultCode.Success)
            {
                this._logger.LogError($"PullData: EnqueueReadBuffer failed ({code}).");
                return null;
            }

            return result;
        }

        /// <summary>
        /// Asynchronously allocates a buffer and uploads <paramref name="data"/> into it.
        /// </summary>
        internal Task<Shared.Interfaces.IRuntimeMem?> PushDataAsync<T>(T[] data, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            return Task.Run(() => this.PushData(data, flags));
        }

        public Task<Shared.Interfaces.IRuntimeMem?> PushDataAsync<T>(IEnumerable<T> data) where T : unmanaged
        {
            return Task.Run(() => this.PushData(data));
        }

        /// <summary>
        /// Asynchronously reads the contents of a float-buffer allocation back into a host array.
        /// </summary>
        public Task<T[]?> PullDataAsync<T>(IRuntimeMem mem) where T : unmanaged
        {
            return Task.Run(() => this.PullData<T>(mem));
        }

        /// <summary>
        /// Reads a float-buffer allocation identified by its native handle back into a host array.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="indexPointer">The native handle of the buffer to read.</param>
        /// <param name="keepBuffer">If <c>false</c>, the allocation is freed after the copy.</param>
        /// <returns>The host data, or <c>null</c> if the handle is unknown or the read failed.</returns>
        public T[] PullData<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (this[indexPointer] is not IRuntimeMem mem)
            {
                this._logger.LogError($"PullData: no allocation found for handle {indexPointer}.");
                return [];
            }

            T[]? result = this.PullData<T>(mem);
            if (!keepBuffer)
            {
                this.Free(mem);
            }

            return result ?? [];
        }

        /// <summary>
        /// Asynchronously reads a float-buffer allocation identified by its native handle.
        /// </summary>
        public Task<T[]> PullDataAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            return Task.Run(() => this.PullData<T>(indexPointer, keepBuffer));
        }

        /// <summary>
        /// Writes a host array into an existing float-buffer allocation identified by its native handle,
        /// without reallocating. The data length must not exceed the buffer capacity.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="indexPointer">The native handle of the target buffer.</param>
        /// <param name="data">The host data to write.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public bool WriteData<T>(IntPtr indexPointer, T[] data) where T : unmanaged
        {
            if (this[indexPointer] is not IRuntimeMem mem)
            {
                this._logger.LogError($"WriteData: no allocation found for handle {indexPointer}.");
                return false;
            }

            CLResultCode result = CL.EnqueueWriteBuffer(this.Queue, ((OpenClMem) mem).IndexBuffer, true, 0, data, null, out _);
            if (result != CLResultCode.Success)
            {
                this._logger.LogError($"WriteData: EnqueueWriteBuffer failed ({result}).");
                return false;
            }

            return true;
        }



        // Group / chunk allocation
        /// <summary>
        /// Allocates a group of uninitialized device buffers (one per supplied length) tracked as a float unit.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <param name="flags">The OpenCL memory flags (defaults to read/write).</param>
        /// <returns>The created <see cref="IRuntimeMem"/>, or <c>null</c> if allocation failed.</returns>
        internal Shared.Interfaces.IRuntimeMem? AllocateGroup<T>(long[] lengths, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            if (lengths == null || lengths.Length == 0)
            {
                this._logger.LogError("AllocateGroup: lengths is null or empty.");
                return null;
            }

            int elementSize = Marshal.SizeOf<T>();
            var buffers = new CLBuffer[lengths.Length];

            for (int i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] <= 0)
                {
                    this._logger.LogError($"AllocateGroup: invalid length {lengths[i]} at index {i}.");
                    ReleaseBuffers(buffers, i);
                    return null;
                }

                IntPtr size = new((int) (lengths[i] * elementSize));
                buffers[i] = CL.CreateBuffer(this.Context, flags, (nuint) size, IntPtr.Zero, out CLResultCode result);
                if (result != CLResultCode.Success)
                {
                    this._logger.LogError($"AllocateGroup: CreateBuffer failed at index {i} ({result}).");
                    ReleaseBuffers(buffers, i);
                    return null;
                }
            }

            IRuntimeMem mem = new OpenClMem(buffers, lengths, typeof(T));
            this._allocations[mem.Id] = mem;
            return mem;
        }

        public Shared.Interfaces.IRuntimeMem? AllocateGroup<T>(IntPtr[] lengths) where T : unmanaged
        {
            return this.AllocateGroup<T>(Array.ConvertAll(lengths, l => (long) l));
        }

        /// <summary>
        /// Allocates a group of device buffers and copies each chunk in <paramref name="data"/> into its own buffer.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="data">The collection of host data chunks to upload.</param>
        /// <param name="flags">The OpenCL memory flags (defaults to read/write).</param>
        /// <returns>The created <see cref="IRuntimeMem"/>, or <c>null</c> if allocation or upload failed.</returns>
        internal Shared.Interfaces.IRuntimeMem? PushChunks<T>(IEnumerable<T[]> data, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            if (data == null)
            {
                this._logger.LogError("PushChunks: data is null.");
                return null;
            }

            T[][] chunks = data as T[][] ?? data.ToArray();
            if (chunks.Length == 0)
            {
                this._logger.LogError("PushChunks: data is empty.");
                return null;
            }

            long[] lengths = new long[chunks.Length];
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] == null || chunks[i].Length == 0)
                {
                    this._logger.LogError($"PushChunks: chunk {i} is null or empty.");
                    return null;
                }

                lengths[i] = chunks[i].Length;
            }

            if (this.AllocateGroup<T>(lengths, flags) is not IRuntimeMem mem)
            {
                return null;
            }

            for (int i = 0; i < chunks.Length; i++)
            {
                CLResultCode result = CL.EnqueueWriteBuffer(this.Queue, ((OpenClMem) mem).Buffers[i], true, 0, chunks[i], null, out _);
                if (result != CLResultCode.Success)
                {
                    this._logger.LogError($"PushChunks: EnqueueWriteBuffer failed at index {i} ({result}).");
                    this.Free(mem);
                    return null;
                }
            }

            return mem;
        }

        public Shared.Interfaces.IRuntimeMem? PushChunks<T>(IEnumerable<IEnumerable<T>> data) where T : unmanaged
        {
            return this.PushChunks(data.Select(chunk => chunk.ToArray()).ToArray());
        }

        /// <summary>
        /// Reads a grouped allocation back into separate host arrays (one per buffer).
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="mem">The grouped allocation to read from.</param>
        /// <returns>The host chunks, or <c>null</c> if the read failed.</returns>
        internal T[][]? PullChunks<T>(IRuntimeMem mem) where T : unmanaged
        {
            if (mem == null || mem.Count == 0)
            {
                this._logger.LogError("PullChunks: allocation is null or empty.");
                return null;
            }

            var chunks = new T[mem.Count][];
            for (int i = 0; i < mem.Count; i++)
            {
                T[] result = new T[mem.PointerLengths[i]];
                CLResultCode code = CL.EnqueueReadBuffer(this.Queue, ((OpenClMem) mem).Buffers[i], true, 0, result, null, out _);
                if (code != CLResultCode.Success)
                {
                    this._logger.LogError($"PullChunks: EnqueueReadBuffer failed at index {i} ({code}).");
                    return null;
                }

                chunks[i] = result;
            }

            return chunks;
        }

        /// <summary>
        /// Reads a grouped allocation identified by its native handle back into separate host arrays.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="indexPointer">The native handle of any buffer belonging to the allocation.</param>
        /// <param name="keepBuffer">If <c>false</c>, the allocation is freed after the copy.</param>
        /// <returns>The host chunks, or <c>null</c> if the handle is unknown or the read failed.</returns>
        public List<T[]> PullChunks<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            if (this[indexPointer] is not IRuntimeMem mem)
            {
                this._logger.LogError($"PullChunks: no allocation found for handle {indexPointer}.");
                return [];
            }

            T[][]? result = this.PullChunks<T>(mem);
            if (!keepBuffer)
            {
                this.Free(mem);
            }

            return result?.ToList() ?? [];
        }

        /// <summary>
        /// Asynchronously allocates a float uninitialized device buffer.
        /// </summary>
        internal Task<Shared.Interfaces.IRuntimeMem?> AllocateSingleAsync<T>(long length, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            return Task.Run(() => this.AllocateSingle<T>(length, flags));
        }

        public Task<Shared.Interfaces.IRuntimeMem?> AllocateSingleAsync<T>(IntPtr length) where T : unmanaged
        {
            return Task.Run(() => this.AllocateSingle<T>((long) length));
        }

        /// <summary>
        /// Asynchronously allocates a group of uninitialized device buffers.
        /// </summary>
        internal Task<IRuntimeMem?> AllocateGroupAsync<T>(long[] lengths, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            return Task.Run(() => this.AllocateGroup<T>(lengths, flags) as IRuntimeMem);
        }

        public Task<Shared.Interfaces.IRuntimeMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged
        {
            return Task.Run(() => this.AllocateGroup<T>(lengths));
        }

        /// <summary>
        /// Asynchronously allocates a group of buffers and uploads each chunk in <paramref name="data"/>.
        /// </summary>
        internal Task<Shared.Interfaces.IRuntimeMem?> PushChunksAsync<T>(IEnumerable<T[]> data, MemoryFlags flags = MemoryFlags.ReadWrite) where T : unmanaged
        {
            return Task.Run(() => this.PushChunks(data, flags));
        }

        public Task<Shared.Interfaces.IRuntimeMem?> PushChunksAsync<T>(IEnumerable<IEnumerable<T>> data) where T : unmanaged
        {
            return Task.Run(() => this.PushChunks(data));
        }

        /// <summary>
        /// Asynchronously reads a grouped allocation identified by its native handle.
        /// </summary>
        public Task<List<T[]>> PullChunksAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged
        {
            return Task.Run(() => this.PullChunks<T>(indexPointer, keepBuffer));
        }

        /// <summary>
        /// Asynchronously reads a grouped allocation back into separate host arrays.
        /// </summary>
        public Task<T[][]?> PullChunksAsync<T>(Shared.Interfaces.IRuntimeMem mem) where T : unmanaged
        {
            return Task.Run(() => this.PullChunks<T>((IRuntimeMem) mem));
        }



        // Bookkeeping
        /// <summary>
        /// Registers an externally created allocation so the registry tracks and disposes it.
        /// </summary>
        internal void Track(IRuntimeMem mem)
        {
            if (mem != null)
            {
                this._allocations[mem.Id] = mem;
            }
        }

        /// <summary>
        /// Determines whether the given allocation is tracked by this registry.
        /// </summary>
        public bool Contains(IRuntimeMem mem)
        {
            return mem != null && this._allocations.ContainsKey(mem.Id);
        }

        /// <summary>
        /// Frees a float tracked allocation and releases its device memory.
        /// </summary>
        /// <param name="mem">The allocation to free.</param>
        /// <returns><c>true</c> if the allocation was tracked and freed; otherwise <c>false</c>.</returns>
        public bool Free(IRuntimeMem mem)
        {
            if (mem == null)
            {
                return false;
            }

            if (this._allocations.TryRemove(mem.Id, out var tracked))
            {
                tracked.Dispose();
                return true;
            }

            // Not tracked, but still release to avoid leaks.
            mem.Dispose();
            return false;
        }

        /// <summary>
        /// Frees the tracked allocation that owns the given native handle.
        /// </summary>
        /// <param name="indexPointer">The native handle of any buffer belonging to the allocation.</param>
        /// <returns>The number of bytes freed, or 0 if no matching allocation was found.</returns>
        public long FreeMemory(IntPtr indexPointer)
        {
            if (this[indexPointer] is not OpenClMem mem)
            {
                return 0;
            }

            long size = mem.TotalSize;
            return this.Free(mem) ? size : 0;
        }

        /// <summary>
        /// Frees the tracked allocation with the given id.
        /// </summary>
        /// <param name="id">The unique id of the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if no matching allocation was found.</returns>
        public long FreeMemory(Guid id)
        {
            if (this[id] is not OpenClMem mem)
            {
                return 0;
            }

            long size = mem.TotalSize;
            return this.Free(mem) ? size : 0;
        }

        /// <summary>
        /// Frees the given allocation and returns the number of bytes released.
        /// </summary>
        /// <param name="mem">The allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if the allocation was not tracked.</returns>
        public long FreeMemory(IRuntimeMem mem)
        {
            if (mem as OpenClMem is not OpenClMem openClMem)
            {
                return 0;
            }

            long size = openClMem.TotalSize;
            return this.Free(openClMem) ? size : 0;
        }

        /// <summary>
        /// Frees every tracked allocation.
        /// </summary>
        public void FreeAll()
        {
            foreach (var key in this._allocations.Keys)
            {
                if (this._allocations.TryRemove(key, out var mem))
                {
                    mem.Dispose();
                }
            }
        }



        // Helpers
        /// <summary>
        /// Releases the first <paramref name="count"/> buffers, used to roll back a partially completed group allocation.
        /// </summary>
        private static void ReleaseBuffers(CLBuffer[] buffers, int count)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    CL.ReleaseMemoryObject(buffers[i]);
                }
                catch
                {
                }
            }
        }



        // Disposal
        /// <summary>
        /// Frees all allocations and releases the command queue and context.
        /// </summary>
        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this.FreeAll();

            try
            {
                CL.ReleaseCommandQueue(this.Queue);
            }
            catch
            {
            }

            try
            {
                CL.ReleaseContext(this.Context);
            }
            catch
            {
            }

            this._disposed = true;
        }
    }
}
