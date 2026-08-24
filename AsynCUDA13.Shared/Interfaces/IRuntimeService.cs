using System;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Common contract shared by every pluggable compute backend (CUDA, OpenCL, ...).
    /// Kept intentionally small and backend-agnostic so that no runtime backend can introduce a
    /// cross/circular project reference back to <see cref="Shared"/>. Both
    /// <see cref="AsynCUDA13.Runtime.ICudaService"/> and
    /// <see cref="AsynCUDA13.OpenClBackend.IOpenClService"/> implement this interface, which lets the
    /// API layer treat any backend as an interchangeable <see cref="IRuntimeService"/>.
    /// </summary>
    public interface IRuntimeService : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the backend has an initialized device context.
        /// </summary>
        bool Online { get; }

        /// <summary>
        /// Gets the flat index of the currently selected device, or <c>-1</c> when the backend is offline.
        /// </summary>
        int SelectedDeviceId { get; }

        /// <summary>
        /// Gets the name of the currently selected device, or <c>null</c> when the backend is offline.
        /// </summary>
        string? SelectedDeviceName { get; }

        /// <summary>
        /// Gets the total number of memory-object allocations made on the device since initialization.
        /// </summary>
        int TotalAllocations { get; }

        /// <summary>
        /// Gets the total number of bytes currently allocated on the device.
        /// </summary>
        long TotalAllocatedBytes { get; }

        /// <summary>
        /// Gets the total number of bytes currently available on the device.
        /// </summary>
        IReadOnlyCollection<IRuntimeMem> RegisteredMemory { get; }

        /// <summary>
        /// Gets a dictionary of the currently selected device's properties, or an empty dictionary when the backend is offline.
        /// </summary>
        Dictionary<string, string> SelectedDeviceProperties { get; }

        /// <summary>
        /// Gets a dictionary of all available devices and their properties, or an empty dictionary when the backend is offline.
        /// </summary>
        Dictionary<int, Dictionary<string, string>> TotalAvailableDeviceProperties { get; }

        /// <summary>
        /// Gets the runtime register interface for managing memory allocations and deallocations on the device.
        /// </summary>
        IRuntimeRegister Register { get; }

        /// <summary>
        /// Gets the runtime Fourier transform interface for performing FFT operations on the device.
        /// </summary>
        IRuntimeFourier Fourier { get; }

        /// <summary>
        /// Gets the runtime compiler interface for compiling and executing code on the device.
        /// </summary>
        IRuntimeCompiler Compiler { get; }

        /// <summary>
        /// Gets the runtime launcher interface for launching kernels and managing execution on the device.
        /// </summary>
        IRuntimeLauncher Launcher { get; }

        /// <summary>
        /// Gets the memory object associated with the specified index pointer, or <c>null</c> if not found.
        /// </summary>
        /// <param name="indexPointer">The index pointer of the memory object.</param>
        /// <returns>The memory object associated with the specified index pointer, or <c>null</c> if not found.</returns>
        IRuntimeMem? this[IntPtr indexPointer] { get; }

        /// <summary>
        /// Gets the memory object associated with the specified unique identifier, or <c>null</c> if not found.
        /// </summary>
        /// <param name="id">The unique identifier of the memory object.</param>
        /// <returns>The memory object associated with the specified unique identifier, or <c>null</c> if not found.</returns>
        IRuntimeMem? this[Guid id] { get; }


        /// <summary>
        /// Gets the memory object associated with the specified index pointer or unique identifier, or <c>null</c> if not found.
        /// </summary>
        /// <param name="indexPointerOrId">The index pointer or unique identifier of the memory object.</param>
        /// <returns>The memory object associated with the specified index pointer or unique identifier, or <c>null</c> if not found.</returns>
        IRuntimeMem? this[string indexPointerOrId] { get; }



        /// <summary>
        /// Initializes the runtime service with the specified device ID. If no device ID is provided, the default device (ID 0) will be used.
        /// </summary>
        /// <param name="deviceId">The ID of the device to initialize.</param>
        /// <returns><c>true</c> if the initialization was successful; otherwise, <c>false</c>.</returns>
        bool Initialize(int deviceId = 0);

        /// <summary>
        /// Sets SynchronizationContext to the current device context, ensuring that subsequent operations are executed on the correct device.
        /// </summary>
        void SetCurrent();




        /// <summary>
        /// Frees the device memory associated with the specified index pointer and returns the number of bytes freed.
        /// </summary>
        /// <param name="indexPointer">The index pointer of the memory object to free.</param>
        /// <returns>The number of bytes freed.</returns>
        long FreeMemory(nint indexPointer);

        /// <summary>
        /// Frees the device memory associated with the specified unique identifier and returns the number of bytes freed.
        /// </summary>
        /// <param name="id">The unique identifier of the memory object to free.</param>
        /// <returns>The number of bytes freed.</returns>
        long FreeMemory(Guid id);

        /// <summary>
        /// Frees all device memory allocations, leaving the service online but with no registered buffers.
        /// </summary>
        void FreeAllMemory();


    }
}
