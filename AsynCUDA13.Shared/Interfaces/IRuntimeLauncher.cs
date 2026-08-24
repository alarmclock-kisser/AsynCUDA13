using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// Executes compiled kernels against device memory.
    /// Provides a unified interface for both CUDA and OpenCL kernel execution.
    /// </summary>
    public interface IRuntimeLauncher
    {
        /// <summary>
        /// Gets the name of the currently loaded kernel, or <c>null</c> if none is loaded.
        /// </summary>
        string? KernelName { get; }

        /// <summary>
        /// Executes a kernel by name with the supplied arguments.
        /// </summary>
        /// <param name="kernelName">The kernel entry-point name.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns>The elapsed time in milliseconds if the kernel executed successfully; otherwise <c>null</c>.</returns>
        int? Execute(string kernelName, params object[] arguments);
    }
}
