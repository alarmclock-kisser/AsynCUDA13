using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using ManagedCuda;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Interface for the CUDA service — used by controllers and tests for mocking.
    /// </summary>
    public interface ICudaService : IRuntimeService
    {
        int ThreadsActive { get; }
        int ThreadsIdle { get; }

        bool Initialize(string name, bool exactMatch = false);



        bool Synchronize();



        Task<long> FreeMemoryAsync(IntPtr indexPointer);


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
        RuntimeDeviceInfo[] GetAllDeviceInfos();
    }
}
