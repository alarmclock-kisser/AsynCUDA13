using System;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Common contract shared by every pluggable compute backend (CUDA, OpenCL, ...).
    /// Kept intentionally small and backend-agnostic so that no runtime backend can introduce a
    /// cross/circular project reference back to <see cref="Shared"/>. Both
    /// <see cref="AsynCUDA13.Runtime.ICudaService"/> and
    /// <see cref="AsynCUDA13.OpenClBackend.IOpenClService"/> implement this interface, which lets the
    /// API layer treat any backend as an interchangeable <see cref="IRuntimeBackend"/>.
    /// </summary>
    public interface IRuntimeBackend
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
        /// Gets the total number of bytes currently allocated on the device.
        /// </summary>
        long TotalAllocatedBytes { get; }



        IRuntimeRegister Register { get; }

        IRuntimeFourier Fourier { get; }

        IRuntimeCompiler Compiler { get; }

        IRuntimeLauncher Launcher { get; }



    }
}
