using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.Shared.Interfaces;
using ManagedCuda;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Interface for the CUDA service — used by controllers and tests for mocking.
    /// </summary>
    public interface ICudaService : IRuntimeBackend
    {
        CudaDeviceProperties? SelectedDeviceProperties { get; }
        IReadOnlyList<IRuntimeMem> RegisteredMemory { get; }
        IRuntimeMem? this[IntPtr indexPointer] { get; }
        IRuntimeMem? this[Guid id] { get; }
        IRuntimeMem? this[string indexPointerOrId] { get; }
        int RegisteredMemoryobjects { get; }
        int ThreadsActive { get; }
        int ThreadsIdle { get; }

        bool Initialize(int deviceId = -1);
        bool Initialize(string name, bool exactMatch = false);

        /// <summary>
        /// Sets the CUDA primary context as the current context for the calling thread.
        /// This is required before any CUDA operations on the calling thread.
        /// </summary>
        /// <returns>True if the context was set successfully; false if the service is offline.</returns>
        bool SetCurrent();

        bool Synchronize();

        void Dispose();

        long FreeMemory(IntPtr indexPointer);
        Task<long> FreeMemoryAsync(IntPtr indexPointer);

        void FreeAllMemory();
        Task FreeAllMemoryAsync();

        Task<IRuntimeMem?> AllocateSingleAsync<T>(IntPtr elementCount) where T : unmanaged;
        Task<IRuntimeMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged;
        Task<IRuntimeMem?> PushDataAsync<T>(IEnumerable<T> data) where T : unmanaged;
        Task<IRuntimeMem?> PushChunksAsync<T>(IEnumerable<T[]> data) where T : unmanaged;
        Task<T[]?> PullDataAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged;
        Task<IEnumerable<T[]>?> PullChunksAsync<T>(IntPtr indexPointer, bool keepBuffer = false) where T : unmanaged;

        /// <summary>
        /// Checks if CUDA is available on the system.
        /// </summary>
        /// <returns>True if CUDA is available, false otherwise.</returns>
        bool IsCudaAvailable();

        /// <summary>
        /// Gets information about all available CUDA devices on the system.
        /// </summary>
        /// <returns>Array of device information, or empty array if CUDA is not available.</returns>
        CudaDeviceInfo[] GetAllDeviceInfos();
    }
}
