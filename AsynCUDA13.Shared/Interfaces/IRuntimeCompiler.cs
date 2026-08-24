using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Discovers, compiles, loads and inspects kernels for the runtime.
    /// Provides a unified interface for both CUDA and OpenCL kernel compilation.
    /// </summary>
    public interface IRuntimeCompiler : IDisposable
    {
        /// <summary>
        /// Gets the name of the currently loaded kernel, or <c>null</c> if none is loaded.
        /// </summary>
        string? KernelName { get; }

        /// <summary>
        /// Unloads the currently loaded kernel.
        /// </summary>
        void UnloadKernel(string? name);

        /// <summary>
        /// Gets the list of available kernel source files.
        /// </summary>
        /// <returns>An array of file paths to kernel source files.</returns>
        string[] GetSourceFiles();

        /// <summary>
        /// Gets the kernel with the specified name.
        /// </summary>
        /// <param name="name">The name of the kernel to retrieve.</param>
        /// <returns>The kernel, or <c>null</c> if not found.</returns>
        object? GetKernel(string name);

        /// <summary>
        /// Checks if a kernel with the specified name exists.
        /// </summary>
        /// <param name="name">The name of the kernel to check.</param>
        /// <returns><c>true</c> if the kernel exists; otherwise <c>false</c>.</returns>
        bool HasKernel(string name);
    }
}
