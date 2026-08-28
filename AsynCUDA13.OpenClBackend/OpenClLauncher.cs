using System;
using System.Runtime.InteropServices;
using OpenTK.Compute.OpenCL;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using System.Runtime.CompilerServices;
using AsynCUDA13.Shared.Serialization;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Executes compiled OpenCL kernels. Provides a flexible argument-mapping launch that accepts a mix of
    /// <see cref="IRuntimeMem"/> buffers and unmanaged scalar values, configures the work size and synchronizes
    /// the command queue. Kept deliberately small; the Fourier helper builds higher-level pipelines on top of it.
    /// </summary>
    internal sealed class OpenClLauncher : IRuntimeLauncher
    {
        /// <summary>
        /// The OpenCL register that tracks memory buffers and their associated metadata.
        /// </summary>
        private readonly OpenClRegister _register;

        /// <summary>
        /// The OpenCL compiler that holds the compiled kernels to launch.
        /// </summary>
        private readonly OpenClCompiler _compiler;

        /// <summary>
        /// The OpenCL command queue used to enqueue kernel executions and manage synchronization.
        /// </summary>
        private readonly CLCommandQueue _queue;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClLauncher"/> class.
        /// </summary>
        /// <param name="compiler">The compiler that holds the kernels to launch.</param>
        /// <param name="queue">The command queue to enqueue work on.</param>
        internal OpenClLauncher(OpenClRegister register, OpenClCompiler compiler, CLCommandQueue queue)
        {
            this._register = register;
            this._compiler = compiler;
            this._queue = queue;
        }

        /// <summary>
        /// Gets the IRuntimeLauncher interface for this instance.
        /// </summary>
        public IRuntimeLauncher Launcher => this;

        /// <summary>
        /// Gets the name of the currently loaded kernel, or <c>null</c> if none is loaded.
        /// </summary>
        public string? KernelName => this._compiler.KernelName;


        /// <summary>
        /// Executes a one-dimensional kernel by name with the supplied arguments and measures the execution time in milliseconds.
        /// </summary>
        /// <param name="kernelName">The name of the kernel to execute.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns>The execution time in milliseconds, or <c>null</c> if the execution failed.</returns>
        public int? Execute(string kernelName, params object[] arguments)
        {
            DateTime started = DateTime.Now;
            bool ok = this.Execute(kernelName, 0, 0, arguments);
            if (!ok)
            {
                return null;
            }
            return (int) (DateTime.Now - started).TotalMilliseconds;
        }

        /// <summary>
        /// Executes a one-dimensional kernel by name with the supplied arguments.
        /// Arguments may be <see cref="IRuntimeMem"/> (bound as buffers), <see cref="CLBuffer"/>, or unmanaged
        /// scalars (<see cref="int"/>, <see cref="float"/>, ...). Blocks until the kernel finishes.
        /// </summary>
        /// <param name="kernelName">The kernel entry-point name.</param>
        /// <param name="globalWorkSize">The total number of work-items.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns><c>true</c> if the kernel executed successfully; otherwise <c>false</c>.</returns>
        public bool Execute(string kernelName, long globalWorkSize = 0, params object[] arguments)
        {
            return this.Execute(kernelName, globalWorkSize, 0, arguments);
        }

        /// <summary>
        /// Executes a one-dimensional kernel by name with an optional local work size.
        /// </summary>
        /// <param name="kernelName">The kernel entry-point name.</param>
        /// <param name="globalWorkSize">The total number of work-items.</param>
        /// <param name="localWorkSize">The work-group size, or 0 to let the runtime choose.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns><c>true</c> if the kernel executed successfully; otherwise <c>false</c>.</returns>
        public bool Execute(string kernelName, long globalWorkSize = 0, long localWorkSize = 0, params object[] arguments)
        {
            CLKernel? kernel = this._compiler.GetClKernel(kernelName);
            if (kernel == null)
            {
                StaticLogger.LogError($"Execute '{kernelName}': kernel not found.");
                return false;
            }

            arguments = DataParser.AreAllArgumentsString(arguments) ? DataParser.ParseArgumentValues(arguments.Cast<string>(), this._compiler.GetArguments(kernelName).Values) : arguments;

            if (globalWorkSize <= 0)
            {
                (globalWorkSize, localWorkSize) = this.GetWorkSizes(arguments);

                if (globalWorkSize <= 0)
                {
                    StaticLogger.LogError($"Execute '{kernelName}': invalid global work size {globalWorkSize} (localWorkSize={localWorkSize}).");
                    return false;
                }
            }

            if (!this.SetArguments(kernel.Value, kernelName, arguments))
            {
                return false;
            }

            nuint[] global = [(nuint) globalWorkSize];
            nuint[]? local = localWorkSize > 0 ? [(nuint) localWorkSize] : null;

            CLResultCode code = CL.EnqueueNDRangeKernel(this._queue, kernel.Value, 1, null, global, local, 0, null, out _);
            if (code != CLResultCode.Success)
            {
                StaticLogger.LogError($"Execute '{kernelName}': EnqueueNDRangeKernel failed ({code}).");
                return false;
            }

            CLResultCode finish = CL.Finish(this._queue);
            if (finish != CLResultCode.Success)
            {
                StaticLogger.LogError($"Execute '{kernelName}': Finish failed ({finish}).");
                return false;
            }

            StaticLogger.LogSuccess($"Execute '{kernelName}': executed successfully.");
            return true;
        }


        /// <summary>
        /// Executes a one-dimensional kernel by name with the supplied arguments asynchronously and measures the execution time in milliseconds.
        /// </summary>
        /// <param name="kernelName">The name of the kernel to execute.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns>The execution time in milliseconds, or <c>null</c> if the execution failed.</returns>
        public async Task<int?> ExecuteAsync(string kernelName, params object[] arguments)
        {
            DateTime started = DateTime.Now;
            bool ok = await Task.Run(() => this.Execute(kernelName, 0, 0, arguments));
            if (!ok)
            {
                await StaticLogger.LogAsync($"ExecuteAsync '{kernelName}': execution failed.");
                return null;
            }
            return (int) (DateTime.Now - started).TotalMilliseconds;
        }

        /// <summary>
        /// Executes a one-dimensional kernel by name with the supplied arguments asynchronously.
        /// </summary>
        /// <param name="kernelName">The name of the kernel to execute.</param>
        /// <param name="globalWorkSize">The global work size, or 0 to let the runtime choose.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns><c>true</c> if the kernel executed successfully; otherwise <c>false</c>.</returns>
        public async Task<bool> ExecuteAsync(string kernelName, long globalWorkSize = 0, params object[] arguments)
        {
            return await Task.Run(() => this.Execute(kernelName, globalWorkSize, 0, arguments));
        }

        /// <summary>
        /// Executes a one-dimensional kernel by name with the supplied arguments asynchronously, allowing specification of both global and local work sizes.
        /// </summary>
        /// <param name="kernelName">The name of the kernel to execute.</param>
        /// <param name="globalWorkSize">The global work size, or 0 to let the runtime choose.</param>
        /// <param name="localWorkSize">The local work size, or 0 to let the runtime choose.</param>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns><c>true</c> if the kernel executed successfully; otherwise <c>false</c>.</returns>
        public async Task<bool> ExecuteAsync(string kernelName, long globalWorkSize = 0, long localWorkSize = 0, params object[] arguments)
        {
            return await Task.Run(() => this.Execute(kernelName, globalWorkSize, localWorkSize, arguments));
        }


        // Arguments
        /// <summary>
        /// Binds each argument to its kernel index, handling buffers and unmanaged scalars.
        /// </summary>
        private bool SetArguments(CLKernel kernel, string kernelName, object[] arguments)
        {
            for (uint i = 0; i < arguments.Length; i++)
            {
                object arg = arguments[i];
                CLResultCode code;

                if (arg is IntPtr ptr)
                {
                    OpenClMem? mem = this._register[(nint) arg] as OpenClMem;
                    if (mem != null)
                    {
                        arg = mem;
                    }
                    else
                    {
                        arg = ptr;
                    }
                }

                switch (arg)
                {
                    case OpenClMem mem:
                        {
                            CLBuffer buffer = mem.IndexBuffer;
                            code = CL.SetKernelArg(kernel, i, in buffer);
                            break;
                        }

                    case CLBuffer buffer:
                        code = CL.SetKernelArg(kernel, i, in buffer);
                        break;

                    case int value:
                        code = CL.SetKernelArg(kernel, i, in value);
                        break;

                    case uint value:
                        code = CL.SetKernelArg(kernel, i, in value);
                        break;

                    case float value:
                        code = CL.SetKernelArg(kernel, i, in value);
                        break;

                    case long value:
                        code = CL.SetKernelArg(kernel, i, in value);
                        break;

                    case nint value:
                        code = CL.SetKernelArg(kernel, i, in value);
                        break;

                    default:
                        StaticLogger.LogError($"Execute '{kernelName}': unsupported argument type '{arg?.GetType().Name ?? "null"}' at index {i}.");
                        return false;
                }

                if (code != CLResultCode.Success)
                {
                    StaticLogger.LogError($"Execute '{kernelName}': SetKernelArg failed at index {i} ({code}).");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines the global and local work sizes based on the provided arguments. It inspects each argument to find the maximum total size for the global work size and the minimum count for the local work size.
        /// </summary>
        /// <param name="arguments">The ordered kernel arguments.</param>
        /// <returns>A tuple containing the global and local work sizes.</returns>
        public (long globalWorkSize, long localWorkSize) GetWorkSizes(params object[] arguments)
        {
            long globalWorkSize = 0;
            long localWorkSize = 0;

            try
            {
                foreach (var arg in arguments)
                {
                    IntPtr handle = IntPtr.Zero;
                    if (arg is IRuntimeMem mem)
                    {
                        handle = mem.IndexPointer;
                    }
                    else if (arg is OpenClMem clMem)
                    {
                        handle = clMem.IndexPointer;
                    }
                    else if (arg is CLBuffer buffer)
                    {
                        handle = buffer.Handle;
                    }
                    else if (arg is IntPtr ptr)
                    {
                        handle = ptr;
                    }
                    else if (arg is long longPtr)
                    {
                        handle = new IntPtr(longPtr);
                    }

                    if (handle != IntPtr.Zero)
                    {
                        var verifiedMem = this._register[handle];
                        if (verifiedMem != null)
                        {
                            long memSize = verifiedMem.TotalLength;
                            if (memSize > globalWorkSize)
                            {
                                globalWorkSize = memSize;
                            }

                            long memStride = verifiedMem.Count;
                            if (memStride > 0 && (localWorkSize == 0 || memStride < globalWorkSize))
                            {
                                localWorkSize = memStride;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StaticLogger.LogError($"GetWorkSizes: failed to determine work sizes ({ex.Message}).");
            }

            return (globalWorkSize, localWorkSize);
        }



        // IDisposable
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
