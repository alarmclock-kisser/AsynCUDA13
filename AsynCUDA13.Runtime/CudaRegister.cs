using ManagedCuda;
using ManagedCuda.BasicTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsynCUDA13.Shared;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Central registry that owns and tracks all CUDA device memory allocations and CUDA streams for a context.
    /// It allocates, transfers (push/pull) and frees memory, hands out and recycles streams for asynchronous
    /// operations, and exposes bindable lists and counters for monitoring. Instances are internal to the runtime
    /// and are created and owned by <see cref="CudaService"/>.
    /// </summary>
    internal class CudaRegister : IDisposable
    {
        // Fields
        /// <summary>All registered memory objects keyed by their unique id.</summary>
        private readonly ConcurrentDictionary<Guid, CudaMem> Memory = [];

        /// <summary>Bindable view of the registered memory objects for UI consumers.</summary>
        public readonly BindingList<CudaMem> MemoryList = [];

        /// <summary>Bindable list mirroring the byte size of each tracked allocation.</summary>
        private readonly BindingList<long> _memorySizes = [];

        /// <summary>All active CUDA streams mapped to their current outstanding-operation count.</summary>
        internal readonly ConcurrentDictionary<CudaStream, int> Streams = [];

        /// <summary>Bindable list mirroring the per-stream outstanding-operation counts.</summary>
        private readonly BindingList<int> _streamThreads = [];

        /// <summary>The CUDA primary context this registry operates on.</summary>
        private readonly PrimaryContext Context;


        // Properties
        /// <summary>Gets the total number of bytes currently allocated across all tracked memory objects.</summary>
        public long TotalAllocated => this.Memory.Values.Sum(m => m.TotalSize);

        /// <summary>Gets the number of registered memory objects.</summary>
        public int RegisteredMemoryObjects => this.Memory.Count;

        /// <summary>Gets the number of streams that currently have at least one outstanding operation.</summary>
        public int ThreadsActive => this.Streams.Count(s => s.Value > 0);

        /// <summary>Gets the number of streams that are currently idle.</summary>
        public int ThreadsIdle => this.Streams.Count(s => s.Value <= 0);

        /// <summary>Gets the maximum number of threads per multiprocessor reported by the active device.</summary>
        public int MaxThreads => this.Context.GetDeviceInfo().MaxThreadsPerMultiProcessor;

        /// <summary>Gets the bindable list of allocation sizes in bytes.</summary>
        public BindingList<long> MemorySizesList => this._memorySizes;

        /// <summary>Gets the bindable list of per-stream outstanding-operation counts.</summary>
        public BindingList<int> StreamThreadsList => this._streamThreads;




        /// <summary>
        /// Decrements the outstanding-operation count for the given stream and disposes/removes it once it becomes idle.
        /// Also cleans up any other idle streams and refreshes the bindable stream-thread list.
        /// </summary>
        /// <param name="stream">The stream to release.</param>
        /// <param name="removeWhenIdle">If <c>true</c>, the stream is disposed and removed once its count reaches zero.</param>
        private void ReleaseStream(CudaStream stream, bool removeWhenIdle = true)
        {
            if (this.Streams.TryGetValue(stream, out int value))
            {
                if (value > 0)
                {
                    this.Streams[stream] = value - 1;
                }

                if (removeWhenIdle && this.Streams.TryGetValue(stream, out int updated) && updated <= 0)
                {
                    this.Streams.TryRemove(stream, out _);
                    stream.Dispose();
                }
            }

            // Clean up any idle streams that remain at <= 0
            foreach (var idle in this.Streams.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList())
            {
                if (this.Streams.TryRemove(idle, out _))
                {
                    idle.Dispose();
                }
            }

            this.RefreshStreamThreads();
        }

        /// <summary>
        /// Adds an allocation size to the bindable <see cref="MemorySizesList"/> in a thread-safe manner.
        /// </summary>
        /// <param name="size">The allocation size in bytes.</param>
        private void AddMemorySize(long size)
        {
            lock (this._memorySizes)
            {
                this._memorySizes.Add(size);
            }
        }

        /// <summary>
        /// Rebuilds the bindable <see cref="StreamThreadsList"/> from the current per-stream counts,
        /// suppressing intermediate change notifications for efficiency.
        /// </summary>
        private void RefreshStreamThreads()
        {
            lock (this._streamThreads)
            {
                this._streamThreads.RaiseListChangedEvents = false;
                this._streamThreads.Clear();
                foreach (var v in this.Streams.Values)
                {
                    this._streamThreads.Add(v);
                }
                this._streamThreads.RaiseListChangedEvents = true;
                this._streamThreads.ResetBindings();
            }
        }


        // Enumerables
        /// <summary>Gets the memory object that contains the given native handle, or <c>null</c> if none matches.</summary>
        /// <param name="indexPointer">The native handle to look up.</param>
        public CudaMem? this[nint indexPointer] => this.Memory.Values.FirstOrDefault(m => m.Pointers.Contains(indexPointer));

        /// <summary>Gets the memory object with the given id, or <c>null</c> if it is not registered.</summary>
        /// <param name="id">The unique id of the memory object.</param>
        public CudaMem? this[Guid id] => this.Memory.ContainsKey(id) ? this.Memory[id] : null;

        /// <summary>Gets the stream with the given CUDA stream id, or <c>null</c> if it does not exist.</summary>
        /// <param name="id">The CUDA stream id.</param>
        internal CudaStream? this[ulong id] => this.GetStream(id);


        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="CudaRegister"/> class and makes the supplied context current.
        /// </summary>
        /// <param name="ctx">The CUDA primary context this registry manages memory and streams for.</param>
        public CudaRegister(PrimaryContext ctx)
        {
            this.Context = ctx;

            this.Context.SetCurrent();
        }

        /// <summary>
        /// Binds the CUDA primary context to the calling thread. A <see cref="PrimaryContext"/> is
        /// thread-affine, and this registry is used from an ASP.NET singleton where each request may run on a
        /// different thread-pool thread. Without re-binding the context, driver calls such as
        /// <c>Synchronize()</c>/<c>CopyToHost()</c> fail with <c>ErrorInvalidContext</c> ("no context bound to
        /// the current thread"). Every public method that issues driver calls must call this first.
        /// </summary>
        private void EnsureContext()
        {
            try
            {
                this.Context?.SetCurrent();
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error binding CUDA context to current thread");
            }
        }



        // Methods (Free)
        /// <summary>
        /// Frees all device buffers described by the given memory object and removes it from the registry.
        /// </summary>
        /// <param name="mem">The memory object to free.</param>
        /// <returns>The number of bytes freed; a negative value indicates the device memory was released but the object was not found in the registry.</returns>
        public long FreeMemory(CudaMem mem)
        {
            long freed = mem.TotalSize;

            this.EnsureContext();
            CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
            foreach (var devicePointer in devicePointers)
            {
                try
                {
                    this.Context.FreeMemory(devicePointer);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log("Error freeing memory", ex);
                }
            }

            if (this.Memory.TryRemove(mem.Id, out _))
            {
                lock (this._memorySizes)
                {
                    this._memorySizes.Remove(mem.TotalSize);
                }
                mem.Dispose();
            }
            else
            {
                freed *= -1;
            }

            return freed;
        }

        /// <summary>
        /// Looks up the memory object that owns the given native handle and frees all of its device buffers.
        /// </summary>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if no matching allocation was found.</returns>
        public long FreeMemory(IntPtr indexPointer)
        {
            CudaMem? mem = this[indexPointer];
            if (mem == null)
            {
                return 0;
            }

            long freed = mem.TotalSize;

            this.EnsureContext();
            CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
            foreach (var devicePointer in devicePointers)
            {
                try
                {
                    this.Context.FreeMemory(devicePointer);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log(ex, "Error freeing memory");
                }
            }

            if (this.Memory.TryRemove(mem.Id, out _))
            {
                lock (this._memorySizes)
                {
                    this._memorySizes.Remove(mem.TotalSize);
                }
                mem.Dispose();
            }
            else
            {
                freed *= -1;
            }

            return freed;
        }

        /// <summary>
        /// Looks up the memory object with the given id and frees all of its device buffers.
        /// </summary>
        /// <param name="id">The unique id of the allocation to free.</param>
        /// <returns>The number of bytes freed, or 0 if no matching allocation was found.</returns>
        public long FreeMemory(Guid id)
        {
            CudaMem? mem = this[id];
            if (mem == null)
            {
                return 0;
            }

            long freed = mem.TotalSize;

            this.EnsureContext();
            CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
            foreach (var devicePointer in devicePointers)
            {
                try
                {
                    this.Context.FreeMemory(devicePointer);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log(ex, "Error freeing memory");
                }
            }

            if (this.Memory.TryRemove(mem.Id, out _))
            {
                lock (this._memorySizes)
                {
                    this._memorySizes.Remove(mem.TotalSize);
                }
                mem.Dispose();
            }
            else
            {
                freed *= -1;
            }

            return freed;
        }


        // Methods (Streams)
        /// <summary>
        /// Creates, synchronizes and registers a new CUDA stream.
        /// </summary>
        /// <returns>The id of the newly created stream, or <c>null</c> if creation failed.</returns>
        internal ulong? CreateStream()
        {
            Guid id = Guid.Empty;

            try
            {
                this.Context.SetCurrent();
                CudaStream stream = new();
                stream.Synchronize();

                id = Guid.NewGuid();
                if (this.Streams.TryAdd(stream, 0))
                {
                    this.RefreshStreamThreads();
                    return stream.ID;
                }
                else
                {
                    stream.Dispose();
                    return null;
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error creating stream");
                return null;
            }
        }

        /// <summary>
        /// Returns a stream to use for an operation. If an id is supplied, the matching stream is returned;
        /// otherwise a new stream is created while the count is below the device's async-engine count, or the
        /// least-busy existing stream is reused.
        /// </summary>
        /// <param name="id">The optional id of a specific stream to retrieve.</param>
        /// <returns>An available <see cref="CudaStream"/>, or <c>null</c> if none could be found or created.</returns>
        public CudaStream? GetStream(ulong? id = null)
        {
            this.EnsureContext();
            int engines = this.Context.GetDeviceInfo().AsyncEngineCount;
            int streams = this.Streams.Count;

            ulong? streamId = null;
            CudaStream? stream = null;

            // Finds the stream by ID if provided, or returns stream with lowest value, or creates a new stream if no ID is provided
            if (id.HasValue)
            {
                stream = this.Streams.Keys.FirstOrDefault(s => s.ID == id.Value);
            }
            else
            {
                if (streams < engines)
                {
                    streamId = this.CreateStream();
                    if (streamId.HasValue)
                    {
                        stream = this.Streams.Keys.FirstOrDefault(s => s.ID == streamId.Value);
                    }
                }
                else
                {
                    // Select stream with the lowest value
                    stream = this.Streams.Keys.OrderBy(s => this.Streams[s]).FirstOrDefault();
                }
            }

            if (stream == null)
            {
                StaticLogger.Log($"No available stream found or created.{(id.HasValue ? $" ID was {id}" : string.Empty)}");
            }

            return stream;
        }

        /// <summary>
        /// Acquires multiple streams at once, creating and registering them as needed.
        /// </summary>
        /// <param name="maxCount">The number of streams to acquire; when 0 or less, fills up to the remaining thread capacity.</param>
        /// <param name="ids">Optional specific stream ids to retrieve instead of creating new streams.</param>
        /// <returns>The acquired streams, or <c>null</c> if any stream could not be created or retrieved.</returns>
        public IEnumerable<CudaStream>? GetManyStreams(int maxCount = 0, IEnumerable<ulong>? ids = null)
        {
            this.EnsureContext();
            if (maxCount <= 0)
            {
                maxCount = this.MaxThreads - this.Streams.Count();
            }
            if (maxCount <= 0 || this.Context == null)
            {
                return null;
            }

            CudaStream[] created = new CudaStream[maxCount];
            var results = created.Select((s, i) =>
            {
                CudaStream? stream = ids != null && ids.Any() ? this.GetStream(ids.ElementAt(i)) : this.GetStream();
                if (stream == null)
                {
                    return null;
                }
                created[i] = stream;
                return stream;
            });

            foreach (var s in results)
            {
                if (s != null && !this.Streams.ContainsKey(s))
                {
                    if (this.Streams.TryAdd(s, 0))
                    {
                        s.Synchronize();
                    }
                    else
                    {
                        s.Dispose();
                    }
                }
            }

            if (created.Any(s => s == null))
            {
                StaticLogger.Log("Some streams could not be created or retrieved.");
                return null;
            }

            this.RefreshStreamThreads();

            return created;
        }

        /// <summary>
        /// Asynchronously acquires multiple streams at once, creating, synchronizing and registering them as needed.
        /// </summary>
        /// <param name="maxCount">The number of streams to acquire; when 0 or less, fills up to the remaining thread capacity.</param>
        /// <param name="ids">Optional specific stream ids to retrieve instead of creating new streams.</param>
        /// <returns>A task producing the acquired streams, or <c>null</c> if none could be created or retrieved.</returns>
        public async Task<IEnumerable<CudaStream>?> GetManyStreamsAsync(int maxCount = 0, IEnumerable<ulong>? ids = null)
        {
            this.EnsureContext();
            if (maxCount <= 0)
            {
                maxCount = this.MaxThreads - this.Streams.Count();
            }
            if (maxCount <= 0 || this.Context == null)
            {
                return null;
            }

            CudaStream[] created = new CudaStream[maxCount];
            var results = created.Select((s, i) =>
            {
                CudaStream? stream = ids != null && ids.Any() ? this.GetStream(ids.ElementAt(i)) : this.GetStream();
                if (stream == null)
                {
                    return null;
                }

                created[i] = stream;
                return stream;
            });

            int added = 0;
            foreach (var s in results)
            {
                if (s != null && !this.Streams.ContainsKey(s))
                {
                    if (this.Streams.TryAdd(s, 0))
                    {
                        await Task.Run(s.Synchronize);
                        added++;
                    }
                    else
                    {
                        s.Dispose();
                    }
                }
            }

            if (added == 0)
            {
                StaticLogger.Log("No streams could be created or retrieved.");
                return null;
            }

            this.RefreshStreamThreads();

            return created;
        }


        // Methods (Allocate)
        /// <summary>
        /// Allocates a single uninitialized device buffer and registers it.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="length">The number of elements to allocate.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> on failure or non-positive length.</returns>
        public CudaMem? AllocateSingle<T>(IntPtr length) where T : unmanaged
        {
            if (length <= 0)
            {
                return null;
            }

            try
            {
                this.EnsureContext();
                var stream = this.GetStream();
                if (stream != null)
                {
                    this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
                    this.RefreshStreamThreads();
                }

                CudaDeviceVariable<T> devVariable = new((long) length);
                var pointer = devVariable.DevicePointer;
                CudaMem mem = new(pointer, length, typeof(T));
                if (this.Memory.TryAdd(mem.Id, mem))
                {
                    // The registry now owns this device buffer and frees it explicitly via FreeMemory.
                    // Suppress the CudaDeviceVariable finalizer so the GC cannot free the buffer behind
                    // the registry's back once this local wrapper goes out of scope.
                    GC.SuppressFinalize(devVariable);
                    this.AddMemorySize(mem.TotalSize);
                    return mem;
                }
                else
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    StaticLogger.Log($"Failed to allocate memory for {typeof(T).Name} of length {length}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, $"Error allocating single memory");
                return null;
            }
        }

        /// <summary>
        /// Asynchronously allocates a single uninitialized device buffer and registers it.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="length">The number of elements to allocate.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> on failure.</returns>
        public async Task<CudaMem?> AllocateSingleAsync<T>(IntPtr length) where T : unmanaged
        {
            if (length <= 0)
            {
                return null;
            }

            if (this.Context == null)
            {
                StaticLogger.Log("Error allocating single memory (async): context is null");
                return null;
            }

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        this.Context.SetCurrent();
                        CudaDeviceVariable<T> devVariable = new((long) length);
                        var pointer = devVariable.DevicePointer;
                        CudaMem mem = new(pointer, length, typeof(T));
                        if (this.Memory.TryAdd(mem.Id, mem))
                        {
                            GC.SuppressFinalize(devVariable);
                            this.AddMemorySize(mem.TotalSize);
                            return mem;
                        }
                        else
                        {
                            devVariable.Dispose();
                            mem.Dispose();
                            StaticLogger.Log($"Failed to allocate memory for {typeof(T).Name} of length {length}.");
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        StaticLogger.Log(ex, $"Error allocating single memory (async)");
                        return null;
                    }
                });
            }
            catch
            {
                return null;
            }
            finally
            {
                // Ensure no lingering stream entries affect ThreadsActive in async path
                if (!this.Streams.IsEmpty)
                {
                    foreach (var key in this.Streams.Keys.ToList())
                    {
                        this.ReleaseStream(key);
                    }
                }
            }

        }

        /// <summary>
        /// Allocates a group of uninitialized device buffers (one per supplied length) and registers them as one object.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> on failure or invalid lengths.</returns>
        public CudaMem? AllocateGroup<T>(IntPtr[] lengths) where T : unmanaged
        {
            if (lengths.LongLength <= 0 || lengths.Any(l => l <= 0))
            {
                return null;
            }

            CudaStream? stream = null;
            try
            {
                this.EnsureContext();
                stream = this.GetStream();
                if (stream != null)
                {
                    this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
                    this.RefreshStreamThreads();
                }

                CudaDeviceVariable<T>[] devVariables = lengths.Select(l => new CudaDeviceVariable<T>((long) l)).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();
                CudaMem mem = new(pointers, lengths, typeof(T));
                if (this.Memory.TryAdd(mem.Id, mem))
                {
                    foreach (var devVariable in devVariables)
                    {
                        GC.SuppressFinalize(devVariable);
                    }
                    this.AddMemorySize(mem.TotalSize);
                    return mem;
                }
                else
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }
                    mem.Dispose();
                    StaticLogger.Log($"Failed to allocate grouped memory for {typeof(T).Name} with lengths {(lengths.LongLength + "x " + lengths.FirstOrDefault())}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error allocating grouped memory");
                return null;
            }
            finally
            {
                if (stream != null)
                {
                    this.ReleaseStream(stream);
                }
            }
        }

        /// <summary>
        /// Asynchronously allocates a group of uninitialized device buffers and registers them as one object.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type to allocate.</typeparam>
        /// <param name="lengths">The element count for each buffer to allocate.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> on failure.</returns>
        public async Task<CudaMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged
        {
            if (lengths.LongLength <= 0 || lengths.Any(l => l <= 0))
            {
                return null;
            }
            if (this.Context == null)
            {
                StaticLogger.Log("Error allocating grouped memory (async): context is null");
                return null;
            }

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        this.Context.SetCurrent();
                        CudaDeviceVariable<T>[] devVariables = lengths.Select(l => new CudaDeviceVariable<T>((long) l)).ToArray();
                        var pointers = devVariables.Select(v => v.DevicePointer).ToArray();

                        CudaMem mem = new(pointers, lengths, typeof(T));

                        if (this.Memory.TryAdd(mem.Id, mem))
                        {
                            foreach (var devVariable in devVariables)
                            {
                                GC.SuppressFinalize(devVariable);
                            }
                            this.AddMemorySize(mem.TotalSize);
                            return mem;
                        }
                        else
                        {
                            foreach (var devVariable in devVariables)
                            {
                                devVariable.Dispose();
                            }
                            mem.Dispose();
                            StaticLogger.Log($"Failed to allocate grouped memory for {typeof(T).Name} with lengths {(lengths.LongLength + "x " + lengths.FirstOrDefault())}");
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        StaticLogger.Log(ex, "Error allocating grouped memory (async)");
                        return null;
                    }
                });
            }
            catch
            {
                return null;
            }
            finally
            {
                if (!this.Streams.IsEmpty)
                {
                    foreach (var key in this.Streams.Keys.ToList())
                    {
                        this.ReleaseStream(key);
                    }
                }
            }
        }


        // Methods (Push)
        /// <summary>
        /// Copies a sequence of host data to a newly allocated device buffer and registers it.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The host data to upload.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> if the data is empty or the upload fails.</returns>
        public CudaMem? PushData<T>(IEnumerable<T> data) where T : unmanaged
        {
            if (data == null || !data.Any())
            {
                return null;
            }

            CudaStream? stream = null;
            try
            {
                this.EnsureContext();
                stream = this.GetStream();
                if (stream != null)
                {
                    this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
                    this.RefreshStreamThreads();
                }

                IntPtr length = (nint) data.LongCount();
                CudaDeviceVariable<T> devVariable = new((long) length);
                var pointer = devVariable.DevicePointer;

                this.Context.CopyToDevice(devVariable.DevicePointer, data.ToArray());

                CudaMem mem = new(pointer, length, typeof(T));

                if (this.Memory.TryAdd(mem.Id, mem))
                {
                    GC.SuppressFinalize(devVariable);
                    this.AddMemorySize(mem.TotalSize);
                    StaticLogger.Log($"[DIAG] PushData<{typeof(T).Name}> ptr=0x{mem.IndexPointer:X} len={mem.IndexLength} bytes={mem.TotalSize} registered={this.Memory.Count}.");
                    return mem;
                }
                else
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    StaticLogger.Log($"Failed to push data for {typeof(T).Name} of length {length}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error pushing data");
                return null;
            }
            finally
            {
                if (stream != null)
                {
                    this.ReleaseStream(stream);
                }
            }
        }

        /// <summary>
        /// Copies several host data chunks to a group of newly allocated device buffers and registers them as one object.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="chunks">The collection of host data chunks to upload, one buffer per chunk.</param>
        /// <returns>The registered <see cref="CudaMem"/>, or <c>null</c> if the input is empty or the upload fails.</returns>
        public CudaMem? PushChunks<T>(IEnumerable<IEnumerable<T>> chunks) where T : unmanaged
        {
            if (chunks == null || !chunks.Any())
            {
                return null;
            }

            CudaStream? stream = null;
            try
            {
                this.EnsureContext();
                stream = this.GetStream();
                if (stream != null)
                {
                    this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
                    this.RefreshStreamThreads();
                }

                IntPtr[] lengths = chunks.Select(chunk => (nint) chunk.LongCount()).ToArray();
                CudaDeviceVariable<T>[] devVariables = chunks.Select(chunk => new CudaDeviceVariable<T>((long) chunk.LongCount())).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();

                for (int i = 0; i < chunks.Count(); i++)
                {
                    this.Context.CopyToDevice(devVariables[i].DevicePointer, chunks.ElementAt(i).ToArray());
                }

                CudaMem mem = new(pointers, lengths, typeof(T));

                if (this.Memory.TryAdd(mem.Id, mem))
                {
                    foreach (var devVariable in devVariables)
                    {
                        GC.SuppressFinalize(devVariable);
                    }
                    this.AddMemorySize(mem.TotalSize);
                    return mem;
                }
                else
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }
                    mem.Dispose();
                    StaticLogger.Log($"Failed to push chunks for {typeof(T).Name} with lengths {(lengths.LongLength + "x " + lengths.FirstOrDefault())}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error pushing chunks");
                return null;
            }
            finally
            {
                if (stream != null)
                {
                    this.ReleaseStream(stream);
                }
            }
        }

        /// <summary>
        /// Asynchronously copies a sequence of host data to a newly allocated device buffer using a CUDA stream, and registers it.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="data">The host data to upload.</param>
        /// <param name="id">The optional id of a specific stream to use for the transfer.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> on failure.</returns>
        public async Task<CudaMem?> PushDataAsync<T>(IEnumerable<T> data, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = null;

            this.EnsureContext();
            var stream = this.GetStream(id);
            if (stream == null)
            {
                return null;
            }

            this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
            this.RefreshStreamThreads();

            try
            {
                IntPtr length = (nint) data.LongCount();
                CudaDeviceVariable<T> devVariable = new((long) length, stream);
                var pointer = devVariable.DevicePointer;

                devVariable.AsyncCopyToDevice(data.ToArray(), stream);
                await Task.Run(stream.Synchronize);

                mem = new(pointer, length, typeof(T));
                if (!this.Memory.TryAdd(mem.Id, mem))
                {
                    devVariable.Dispose();
                    mem.Dispose();
                    StaticLogger.Log($"Failed to push data for {typeof(T).Name} of length {length}.");
                    mem = null;
                }
                else
                {
                    GC.SuppressFinalize(devVariable);
                    this.AddMemorySize(mem.TotalSize);
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, $"Error pushing data (async)");
            }
            finally
            {
                this.ReleaseStream(stream);
            }

            return mem;
        }

        /// <summary>
        /// Asynchronously copies several host data chunks to a group of newly allocated device buffers using a CUDA stream, and registers them.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="chunks">The collection of host data chunks to upload, one buffer per chunk.</param>
        /// <param name="id">The optional id of a specific stream to use for the transfer.</param>
        /// <returns>A task producing the registered <see cref="CudaMem"/>, or <c>null</c> on failure.</returns>
        public async Task<CudaMem?> PushChunksAsync<T>(IEnumerable<IEnumerable<T>> chunks, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = null;
            this.EnsureContext();
            var stream = this.GetStream(id);
            if (stream == null)
            {
                return null;
            }
            this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
            this.RefreshStreamThreads();

            try
            {
                IntPtr[] lengths = chunks.Select(chunk => (nint) chunk.LongCount()).ToArray();
                CudaDeviceVariable<T>[] devVariables = chunks.Select(chunk => new CudaDeviceVariable<T>((long) chunk.LongCount(), stream)).ToArray();
                var pointers = devVariables.Select(v => v.DevicePointer).ToArray();

                for (int i = 0; i < chunks.Count(); i++)
                {
                    devVariables[i].AsyncCopyToDevice(chunks.ElementAt(i).ToArray(), stream);
                }

                await Task.Run(stream.Synchronize);

                mem = new(pointers, lengths, typeof(T));
                if (!this.Memory.TryAdd(mem.Id, mem))
                {
                    foreach (var devVariable in devVariables)
                    {
                        devVariable.Dispose();
                    }

                    mem.Dispose();
                    StaticLogger.Log($"Failed to push chunks for {typeof(T).Name} with lengths {(lengths.LongLength + "x " + lengths.FirstOrDefault())}.");
                    mem = null;
                }
                else
                {
                    foreach (var devVariable in devVariables)
                    {
                        GC.SuppressFinalize(devVariable);
                    }
                    this.AddMemorySize(mem.TotalSize);
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, $"Error pushing chunks (async)");
            }
            finally
            {
                this.ReleaseStream(stream);
            }

            return mem;
        }


        // Methods (Pull)
        /// <summary>
        /// Copies the data of a single registered device buffer back to the host.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to read.</param>
        /// <param name="keep">If <c>false</c>, the device memory is freed after the copy completes.</param>
        /// <returns>The downloaded host array, or an empty array if the allocation was not found or the copy failed.</returns>
        public T[] PullData<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];

            if (mem == null || mem.Pointers.Length == 0 || mem.Lengths.Length == 0)
            {
                StaticLogger.Log($"[DIAG] PullData<{typeof(T).Name}> lookup MISS for ptr=0x{indexPointer:X} (registered={this.Memory.Count}). Returning empty.");
                return [];
            }

            try
            {
                CUdeviceptr devicePointer = new(mem.IndexPointer);
                // CudaDeviceVariable<T>(CUdeviceptr, SizeT) interprets the second argument as the size in
                // bytes, not the element count. Passing the element count made non-multiples of the type
                // size (e.g. len=1 for float) throw "Variable size is not a multiple of its type size."
                // and returned an empty array, which is why aggregate/kernel pulls came back empty.
                SizeT sizeInBytes = (long) mem.IndexLength * mem.ElementSize;
                CudaDeviceVariable<T> devVariable = new(devicePointer, sizeInBytes);
                T[] data = new T[mem.IndexLength];

                this.EnsureContext();
                this.Context.Synchronize();
                this.Context.CopyToHost(data, devVariable.DevicePointer);

                StaticLogger.Log($"[DIAG] PullData<{typeof(T).Name}> ptr=0x{mem.IndexPointer:X} len={mem.IndexLength} first=[{string.Join(", ", data.Take(4))}] keep={keep}.");

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return data;
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error pulling data");
                return [];
            }
        }

        /// <summary>
        /// Copies all device buffers of a grouped allocation back to the host as separate arrays.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to read.</param>
        /// <param name="keep">If <c>false</c>, the device memory is freed after the copy completes.</param>
        /// <returns>A list with one host array per buffer, or an empty list if the allocation was not found or the copy failed.</returns>
        public List<T[]> PullChunks<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];

            if (mem == null || mem.Pointers.Length == 0 || mem.Lengths.Length == 0)
            {
                return [];
            }

            try
            {
                List<T[]> chunks = [];
                CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
                // See PullData: the (CUdeviceptr, SizeT) constructor expects a byte size, so scale each
                // buffer's element count by the element size before wrapping the existing device pointer.
                CudaDeviceVariable<T>[] devVariables = devicePointers.Select((ptr, i) => new CudaDeviceVariable<T>(ptr, (SizeT) ((long) mem.Lengths[i] * mem.ElementSize))).ToArray();

                this.EnsureContext();
                for (int i = 0; i < devVariables.Length; i++)
                {
                    T[] chunkData = new T[mem.Lengths[i]];
                    this.Context.CopyToHost(chunkData, devVariables[i].DevicePointer);
                    chunks.Add(chunkData);
                }

                this.Context.Synchronize();

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return chunks;
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error pulling chunks");
                return [];
            }
        }

        /// <summary>
        /// Asynchronously copies the data of a single registered device buffer back to the host using a native async memcpy on a CUDA stream.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to read.</param>
        /// <param name="keep">If <c>false</c>, the device memory is freed after the copy completes.</param>
        /// <param name="id">The optional id of a specific stream to use for the transfer.</param>
        /// <returns>A task producing the downloaded host array, or an empty array on failure.</returns>
        public async Task<T[]> PullDataAsync<T>(IntPtr indexPointer, bool keep = false, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];
            if (mem == null || mem.Count <= 0)
            {
                return [];
            }

            try
            {
                int length = (int) mem.IndexLength;
                T[] data = new T[length];
                this.EnsureContext();
                var stream = this.GetStream(id);
                if (stream == null)
                {
                    return data;
                }
                this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
                this.RefreshStreamThreads();

                // this.Context.SetCurrent();
                this.Context.SetCurrent();

                CUdeviceptr devicePtr = new(mem.IndexPointer);

                // Native asynchroner Transfer: Device ? Host
                int byteSize = data.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
                unsafe
                {
                    fixed (T* pData = data)
                    {
                        var res = ManagedCuda.DriverAPINativeMethods.AsynchronousMemcpy_v2.cuMemcpyAsync(
                            new CUdeviceptr((IntPtr) pData),
                            devicePtr,
                            (SizeT) byteSize,
                            stream.Stream
                        );
                        if (res != CUResult.Success)
                        {
                            throw new CudaException(res);
                        }
                    }
                }

                await Task.Run(stream.Synchronize);

                this.ReleaseStream(stream);

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return data;
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error pulling data (async)");
                return [];
            }
            finally
            {
                GC.Collect();
            }
        }

        /// <summary>
        /// Asynchronously copies all device buffers of a grouped allocation back to the host using native async memcpy on a CUDA stream.
        /// Falls back to a synchronous copy when no stream is available.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the data.</typeparam>
        /// <param name="indexPointer">The native handle of a buffer belonging to the allocation to read.</param>
        /// <param name="keep">If <c>false</c>, the device memory is freed after the copy completes.</param>
        /// <param name="id">The optional id of a specific stream to use for the transfer.</param>
        /// <returns>A task producing a list with one host array per buffer, or an empty list on failure.</returns>
        public async Task<List<T[]>> PullChunksAsync<T>(IntPtr indexPointer, bool keep = false, ulong? id = null) where T : unmanaged
        {
            CudaMem? mem = this[indexPointer];
            if (mem == null || mem.Count <= 0)
            {
                return [];
            }

            try
            {
                List<T[]> chunks = [];
                this.EnsureContext();
                var stream = this.GetStream(id);
                if (stream == null)
                {
                    StaticLogger.Log("No stream available for async pull, falling back to synchronous copy.");
                    return this.PullChunks<T>(indexPointer, keep);
                }
                this.Streams[stream] = this.Streams.GetValueOrDefault(stream, 0) + 1;
                this.RefreshStreamThreads();

                // this.Context.SetCurrent();

                CUdeviceptr[] devicePointers = mem.Pointers.Select(p => new CUdeviceptr(p)).ToArray();
                for (int i = 0; i < devicePointers.Length; i++)
                {
                    T[] data = new T[mem.Lengths[i]];
                    CUdeviceptr devicePtr = devicePointers[i];

                    // Native asynchroner Transfer: Device ? Host
                    int byteSize = data.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
                    unsafe
                    {
                        fixed (T* pData = data)
                        {
                            var res = ManagedCuda.DriverAPINativeMethods.AsynchronousMemcpy_v2.cuMemcpyAsync(
                                new CUdeviceptr((IntPtr) pData),
                                devicePtr,
                                (SizeT) byteSize,
                                stream.Stream
                            );
                            if (res != CUResult.Success)
                            {
                                throw new CudaException(res);
                            }
                        }
                    }

                    chunks.Add(data);
                }

                await Task.Run(stream.Synchronize);

                this.ReleaseStream(stream);

                if (!keep)
                {
                    this.FreeMemory(mem);
                }

                return chunks;
            }
            catch (Exception ex)
            {
                StaticLogger.Log(ex, "Error pulling chunks (async)");
                return [];
            }
            finally
            {
                GC.Collect();
            }
        }


        // Methods (Statistics)







        // Dispose
        /// <summary>
        /// Frees all registered device memory allocations, disposes all registered streams,
        /// clears the internal tracking lists, and suppresses finalization.
        /// </summary>
        public void Dispose()
        {
            foreach (var mem in this.Memory.Values.ToList())
            {
                try
                {
                    this.FreeMemory(mem);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log("Error freeing memory during dispose", ex);
                }
            }
            this.Memory.Clear();

            foreach (var stream in this.Streams.Keys)
            {
                try
                {
                    stream.Dispose();
                }
                catch
                {
                }
            }
            this.Streams.Clear();
            lock (this._streamThreads)
            {
                this._streamThreads.Clear();
            }
            lock (this._memorySizes)
            {
                this._memorySizes.Clear();
            }

            GC.SuppressFinalize(this);
        }
    }
}
