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
        /// Gets the directory where kernel source files are located, or <c>null</c> if not set.
        /// </summary>
        string KernelDirectory { get; }

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
        /// Gets the list of compiled kernel files.
        /// </summary>
        /// <returns>An array of file paths to compiled kernel files.</returns>
        string[] GetCompiledFiles();

        /// <summary>
        /// Gets the kernel with the specified name.
        /// </summary>
        /// <param name="name">The name of the kernel to retrieve.</param>
        /// <returns>The kernel, or <c>null</c> if not found.</returns>
        object? GetKernel(string name);


        /// <summary>
        /// Gets the source file of the kernel with the specified name.
        /// </summary>
        /// <param name="name">The name of the kernel.</param>
        /// <returns>The file path of the kernel source file, or <c>null</c> if not found.</returns>
        string? GetKernelSourceFile(string name);


        /// <summary>
        /// Gets the source code of the kernel with the specified name.
        /// </summary>
        /// <param name="kernelName">The name of the kernel.</param>
        /// <returns>The source code of the kernel, or <c>null</c> if not found.</returns>
        string? GetKernelCode(string? kernelName);

        /// <summary>
        /// Gets the function name of the kernel with the specified name or file path or source code.
        /// </summary>
        /// <param name="kernel">The name, file path, or source code of the kernel.</param>
        /// <returns>The function name of the kernel, or <c>null</c> if not found.</returns>
        string? GetFunctionName(string? kernel);

        /// <summary>
        /// Gets the arguments of the kernel with the specified name.
        /// </summary>
        /// <param name="kernel">The name, file path, or source code of the kernel.</param>
        /// <returns>A dictionary mapping argument names to their types.</returns>
        Dictionary<string, Type> GetArguments(string? kernel);

        /// <summary>
        /// Checks if a kernel with the specified name exists.
        /// </summary>
        /// <param name="name">The name of the kernel to check.</param>
        /// <returns><c>true</c> if the kernel exists; otherwise <c>false</c>.</returns>
        bool HasKernel(string name);


        /// <summary>
        /// Loads the kernel with the specified name into the runtime.
        /// </summary>
        /// <param name="name">The name of the kernel to load.</param>
        /// <returns><c>true</c> if the kernel was successfully loaded; otherwise <c>false</c>.</returns>
        bool LoadKernel(string name);

        /// <summary>
        /// Compiles the provided kernel code and returns the compiled kernel object.
        /// </summary>
        /// <param name="kernelCode">The source code of the kernel to compile.</param>
        /// <returns>The compiled kernel object.</returns>
        string CompileKernel(string kernelCode);

        /// <summary>
        /// Precompiles the provided kernel code and returns whether the precompilation was successful.
        /// </summary>
        /// <param name="code">The source code of the kernel to precompile.</param>
        /// <returns>The name of the kernel if the precompilation was successful; otherwise <c>null</c>.</returns>
        string? PrecompileKernel(string code);

        /// <summary>
        /// Merges the provided arguments with the input and output pointers for an image processing kernel.
        /// </summary>
        /// <param name="inputPointer">The input data pointer.</param>
        /// <param name="outputPointer">The output data pointer.</param>
        /// <param name="width">The width of the image.</param>
        /// <param name="height">The height of the image.</param>
        /// <param name="channels">The number of image channels.</param>
        /// <param name="bitdepth">The bit depth of the image.</param>
        /// <param name="arguments">An array of additional arguments.</param>
        /// <param name="silent">If set to <c>true</c>, suppresses logging.</param>
        /// <returns>An array of objects ordered for kernel execution.</returns>
        object[] MergeArgumentsImage(IntPtr? inputPointer, IntPtr? outputPointer, int width, int height, int channels = 4, int bitdepth = 32, object[]? arguments = null, bool silent = false);



        /// <summary>
        /// Merges the provided arguments with the input and output pointers for an audio processing kernel.
        /// </summary>
        /// <param name="inputPointer">The input data pointer.</param>
        /// <param name="outputPointer">The output data pointer.</param>
        /// <param name="sampleRate">The audio sample rate.</param>
        /// <param name="channels">The number of audio channels.</param>
        /// <param name="bitdepth">The audio bit depth.</param>
        /// <param name="namedArguments">Optional dictionary of additional named arguments.</param>
        /// <returns>An array of objects ordered for kernel execution.</returns>
        object[] MergeArgumentsAudio(IntPtr inputPointer, IntPtr outputPointer, int sampleRate = 44100, int channels = 2, int bitdepth = 32, Dictionary<string, object>? namedArguments = null);
    }
}
