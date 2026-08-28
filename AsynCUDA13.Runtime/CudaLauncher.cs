using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Serialization;
using System.Security.AccessControl;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Executes compiled CUDA kernels against device memory. The launcher resolves kernel arguments,
    /// validates their count and types, configures the launch grid/block dimensions and runs the kernel,
    /// supporting both linear (1D) workloads and image (2D) workloads.
    /// </summary>
    internal class CudaLauncher : IRuntimeLauncher
    {
        // Fields
        /// <summary>The CUDA primary context the kernels are launched on.</summary>
        private readonly PrimaryContext Context;

        /// <summary>The registry used to resolve and manage device memory.</summary>
        private readonly CudaRegister Register;

        /// <summary>The Fourier helper available for transform-based workloads.</summary>
        private readonly CudaFourier Fourier;

        /// <summary>The compiler used to load kernels and inspect their argument signatures.</summary>
        private readonly CudaCompiler Compiler;

        /// <summary>Gets the currently loaded kernel from the associated <see cref="CudaCompiler"/>.</summary>
        private CudaKernel? Kernel => this.Compiler.Kernel;

        /// <summary>Gets the name of the currently loaded kernel, or <c>null</c> if none is loaded.</summary>
        public string? KernelName => this.Compiler.KernelName;

        /// <summary>
        /// Gets the IRuntimeLauncher interface for this instance.
        /// </summary>
        public IRuntimeLauncher Launcher => this;





        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="CudaLauncher"/> class.
        /// </summary>
        /// <param name="ctx">The CUDA primary context to launch kernels on.</param>
        /// <param name="register">The memory registry used to resolve device buffers.</param>
        /// <param name="fourier">The Fourier helper instance.</param>
        /// <param name="compiler">The compiler that loads kernels and exposes their argument definitions.</param>
        internal CudaLauncher(PrimaryContext ctx, CudaRegister register, CudaFourier fourier, CudaCompiler compiler)
        {
            this.Context = ctx;
            this.Register = register;
            this.Fourier = fourier;
            this.Compiler = compiler;
        }




        // Methods
        /// <summary>
        /// Executes a kernel over a one-dimensional (linear) data buffer.
        /// Loads the kernel if necessary, validates the user-supplied arguments against the kernel signature,
        /// computes a 1D grid using a block size of 256 threads and runs the kernel synchronously.
        /// </summary>
        /// <param name="kernelName">The kernel to load when none is currently loaded; ignored if a kernel is already loaded.</param>
        /// <param name="pointer">The native handle of the input/output device buffer.</param>
        /// <param name="arguments">The non-pointer scalar arguments expected by the kernel, in order.</param>
        /// <param name="length">The number of elements to process (used to size the launch grid).</param>
        /// <returns>The input <paramref name="pointer"/> on success; otherwise <c>null</c>.</returns>
        public IntPtr? ExecuteLinearKernel(string? kernelName, IntPtr pointer, object[] arguments, IntPtr length)
        {
            this.Context.SetCurrent();

            // (Re)load whenever no kernel is loaded OR a different kernel is requested, so switching kernels
            // validates the arguments against the correct (newly loaded) signature instead of the previous one.
            bool needsLoad = this.Kernel == null ||
                (!string.IsNullOrEmpty(kernelName) &&
                 !string.Equals(this.KernelName, kernelName, StringComparison.Ordinal));

            if (needsLoad)
            {
                if (string.IsNullOrEmpty(kernelName))
                {
                    StaticLogger.Log("Kernel name not provided for loading.");
                    return null;
                }

                this.Compiler.LoadKernel(kernelName);
            }

            if (this.Kernel == null)
            {
                StaticLogger.Log($"Kernel not loaded '{kernelName ?? "N/A"}'");
                return null;
            }

            if (pointer == IntPtr.Zero)
            {
                StaticLogger.Log("Invalid input pointer (null).");
                return null;
            }

            Dictionary<string, Type> args = this.Compiler.GetArguments(null);
            List<Type> expectedUserArgs = [];
            foreach (var arg in args)
            {
                if (arg.Value == typeof(IntPtr))
                {
                    continue;
                }
                expectedUserArgs.Add(arg.Value);
            }

            if (arguments.Length != expectedUserArgs.Count)
            {
                StaticLogger.Log($"Argument count mismatch. Expected {expectedUserArgs.Count}, got {arguments.Length}.");
                return null;
            }

            for (int i = 0; i < expectedUserArgs.Count; i++)
            {
                object? value = arguments[i];
                Type expected = expectedUserArgs[i];
                if (value == null || !expected.IsAssignableFrom(value.GetType()))
                {
                    StaticLogger.Log($"Argument type mismatch at index {i}. Expected {expected.Name}, got {value?.GetType().Name ?? "null"}.");
                    return null;
                }
            }

            try
            {
                CUdeviceptr devicePtr = new(pointer);

                object[] kernelArgs = new object[args.Count];
                int userArgIndex = 0;
                for (int i = 0; i < args.Count; i++)
                {
                    Type expected = args.ElementAt(i).Value;
                    if (expected == typeof(IntPtr))
                    {
                        kernelArgs[i] = devicePtr;
                    }
                    else
                    {
                        kernelArgs[i] = arguments[userArgIndex++];
                    }
                }

                int blockSize = 256;
                int gridSize = (int) ((length + blockSize - 1) / blockSize);
                this.Kernel.BlockDimensions = new dim3(blockSize, 1, 1);
                this.Kernel.GridDimensions = new dim3(gridSize, 1, 1);

                this.Kernel.Run(kernelArgs);
                StaticLogger.Log($"Kernel executed '{this.KernelName ?? "N/A"}'");
                this.Context.Synchronize();

                return pointer;
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to execute kernel '{this.KernelName ?? "N/A"}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Executes a kernel over a two-dimensional image buffer.
        /// Loads the kernel if necessary, validates the user-supplied arguments (excluding the two image pointers
        /// and the width/height/channels/bit-depth parameters that are injected automatically), configures an 8x8
        /// 2D block layout and runs the kernel in-place synchronously.
        /// </summary>
        /// <param name="kernelName">The kernel to load when none is currently loaded; ignored if a kernel is already loaded.</param>
        /// <param name="pointer">The native handle of the image device buffer (used as both input and output).</param>
        /// <param name="arguments">The additional scalar arguments expected by the kernel, in order.</param>
        /// <param name="width">The image width in pixels.</param>
        /// <param name="height">The image height in pixels.</param>
        /// <param name="channels">The number of channels per pixel (default 4).</param>
        /// <param name="bitdepth">The bit depth per channel (default 8).</param>
        /// <returns>The input <paramref name="pointer"/> on success; otherwise <c>null</c>.</returns>
        public IntPtr? ExecuteImageKernel(string? kernelName, IntPtr pointer, object[] arguments, int width, int height, int channels = 4, int bitdepth = 8)
        {
            this.Context.SetCurrent();

            // (Re)load whenever no kernel is loaded OR a different kernel is requested. Previously the launcher
            // only loaded when no kernel was present, so switching kernels (e.g. mandelbrot -> julia) kept the
            // old kernel loaded and validated the new call against the old signature, producing an argument
            // count mismatch ("Expected 7, got 9") and rendering the wrong/incompatible kernel.
            bool needsLoad = this.Kernel == null ||
                (!string.IsNullOrEmpty(kernelName) &&
                 !string.Equals(this.KernelName, kernelName, StringComparison.Ordinal));

            if (needsLoad)
            {
                if (string.IsNullOrEmpty(kernelName))
                {
                    StaticLogger.Log("Kernel name not provided for loading.");
                    return null;
                }

                this.Compiler.LoadKernel(kernelName);
            }

            if (this.Kernel == null)
            {
                StaticLogger.Log($"Kernel not loaded '{kernelName ?? "N/A"}'");
                return null;
            }

            if (pointer == IntPtr.Zero)
            {
                StaticLogger.Log("Invalid input pointer (null).");
                return null;
            }

            Dictionary<string, Type> args = this.Compiler.GetArguments(null);
            List<Type> expectedUserArgs = [];
            int pointerCount = 0;
            foreach (var arg in args)
            {
                string name = arg.Key;
                Type type = arg.Value;

                // Count every pointer as a buffer slot: a float-pointer kernel runs in-place (IP), a
                // two-pointer kernel writes to a separate output buffer (OOP). This must not be capped at a
                // fixed number, otherwise an extra pointer is mistaken for a user scalar and the argument
                // count no longer matches (the "Expected 7, got 9" mismatch when the image had 2 pointers).
                if (type == typeof(IntPtr))
                {
                    pointerCount++;
                    continue;
                }
                if (name.Contains("width", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("height", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("chan", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("bit", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                expectedUserArgs.Add(type);
            }

            if (arguments.Length != expectedUserArgs.Count)
            {
                StaticLogger.Log($"Argument count mismatch. Expected {expectedUserArgs.Count}, got {arguments.Length}.");
                return null;
            }

            for (int i = 0; i < expectedUserArgs.Count; i++)
            {
                object? value = arguments[i];
                Type expected = expectedUserArgs[i];
                if (value == null || !expected.IsAssignableFrom(value.GetType()))
                {
                    StaticLogger.Log($"Argument type mismatch at index {i}. Expected {expected.Name}, got {value?.GetType().Name ?? "null"}.");
                    return null;
                }
            }

            try
            {
                IntPtr devicePtr = pointer;

                // IP kernels (a float pointer) run in-place: the input buffer is also the output buffer.
                // OOP kernels (two pointers) need a distinct output buffer so a stale/reused input allocation
                // cannot corrupt the result when switching kernels (e.g. mandelbrot -> julia). A fresh output
                // buffer of the same size is allocated per launch and its pointer is returned to the caller.
                IntPtr outputPtr = devicePtr;
                IntPtr resultPointer = pointer;
                bool outOfPlace = pointerCount >= 2;
                if (outOfPlace)
                {
                    long byteLength = (long) width * height * channels * (bitdepth / 8);
                    if (byteLength <= 0)
                    {
                        StaticLogger.Log("Invalid image dimensions for out-of-place output buffer allocation.");
                        return null;
                    }

                    var output = this.Register.AllocateSingle<Byte>((IntPtr) byteLength);
                    if (output == null)
                    {
                        StaticLogger.Log("Failed to allocate the out-of-place output image buffer.");
                        return null;
                    }

                    outputPtr = output.IndexPointer;
                    resultPointer = output.IndexPointer;
                }

                object[] kernelArgs = this.Compiler.MergeArgumentsImage(devicePtr, outputPtr, width, height, channels, bitdepth, arguments);

                int totalThreadsX = width;
                int totalThreadsY = height;

                int blockSizeX = 8;
                int blockSizeY = 8;

                int gridSizeX = (totalThreadsX + blockSizeX - 1) / blockSizeX;
                int gridSizeY = (totalThreadsY + blockSizeY - 1) / blockSizeY;

                this.Kernel.BlockDimensions = new dim3(blockSizeX, blockSizeY, 1);  // 2D-Block
                this.Kernel.GridDimensions = new dim3(gridSizeX, gridSizeY, 1);     // 2D-Grid

                this.Kernel.Run(kernelArgs);

                StaticLogger.Log($"Kernel executed '{this.KernelName ?? "N/A"}'");

                this.Context.Synchronize();

                return resultPointer;
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to execute kernel '{this.KernelName ?? "N/A"}'", ex);
                return null;
            }
        }


        // IRuntimeLauncher API
        public RuntimeExecuteResponse? Execute(string kernelName, params object[] arguments)
        {
            return this.ExecuteGenericKernel(kernelName, arguments);
        }

        public async Task<RuntimeExecuteResponse?> ExecuteAsync(string kernelName, params object[] arguments)
        {
            return await this.ExecuteGenericKernelAsync(kernelName, arguments);
        }


        // Generic async EXEC
        public async Task<RuntimeExecuteResponse?> ExecuteGenericKernelAsync(string? kernelName, object[] arguments, bool unloadWhenExecuted = false)
        {
            var response = new RuntimeExecuteResponse();

            // Set context first for thread-affine CUDA operations
            this.Context.SetCurrent();

            // If no kernelName provided but Kernel loaded, use that
            if (string.IsNullOrEmpty(kernelName) && this.Kernel == null)
            {
                await StaticLogger.LogAsync("No kernel name provided and no kernel is currently loaded.");
                return null;
            }

            // If kernelName equals current KernelName, use that, otherwise unload current kernel and load new one
            if (!string.IsNullOrEmpty(kernelName) && !string.Equals(this.KernelName, kernelName, StringComparison.Ordinal))
            {
                this.Compiler.UnloadKernel(null);
                this.Compiler.LoadKernel(kernelName);
            }
            if (this.Kernel == null)
            {
                await StaticLogger.LogAsync($"Kernel not loaded '{kernelName ?? "N/A"}'");
                return null;
            }


            // Verify arguments match Kernel signature
            Type[] argTypesSignature = this.Compiler.GetArguments(null).Values.ToArray();
            arguments = DataParser.AreAllArgumentsString(arguments) ? DataParser.ParseArgumentValues(arguments.Cast<string>(), argTypesSignature) : arguments;
            Type[] argTypes = arguments.Select(a => a?.GetType() ?? typeof(object)).ToArray();

            if (argTypes.Length != argTypesSignature.Length)
            {
                await StaticLogger.LogAsync($"Kernel argument count does not match signature '{kernelName ?? "N/A"}': expected {argTypesSignature.Length}, got {argTypes.Length}.");
                return null;
            }

            if (!argTypes.SequenceEqual(argTypesSignature))
            {
                string[] details = argTypes.Select((t, i) => new { Type = t, Index = i }).Where(x => x.Type != argTypesSignature[x.Index]).Select(x => $"<{x.Index}> {x.Type.Name} != {argTypesSignature[x.Index].Name}").ToArray();
                await StaticLogger.LogAsync($"Kernel arguments do not match signature '{kernelName ?? "N/A"}': {string.Join(", ", details)}");
                return null;
            }

            DateTime startTime = DateTime.Now;
            try
            {
                int length = 1;
                for (int i = 0; i < arguments.Length; i++)
                {
                    object argument = arguments[i];
                    if (argTypesSignature[i] == typeof(IntPtr))
                    {
                        if (argument is not IntPtr pointer || pointer == IntPtr.Zero || this.Register[pointer] is not CudaMem memory)
                        {
                            await StaticLogger.LogAsync($"Kernel pointer argument at index {i} is not a registered device buffer.");
                            return null;
                        }

                        long pointerLength = memory.IndexLength;
                        if (pointerLength <= 0 || pointerLength > int.MaxValue)
                        {
                            await StaticLogger.LogAsync("A registered kernel buffer has an invalid element length.");
                            return null;
                        }

                        length = Math.Max(length, (int) pointerLength);
                    }
                }

                var argumentDefinitions = this.Compiler.GetArguments(null);
                int widthIndex = argumentDefinitions.Keys
                    .Select((name, index) => new { name, index })
                    .FirstOrDefault(x => x.name.Contains("width", StringComparison.OrdinalIgnoreCase))?.index ?? -1;
                int heightIndex = argumentDefinitions.Keys
                    .Select((name, index) => new { name, index })
                    .FirstOrDefault(x => x.name.Contains("height", StringComparison.OrdinalIgnoreCase))?.index ?? -1;

                if (widthIndex >= 0 && heightIndex >= 0 &&
                    arguments[widthIndex] is int width && arguments[heightIndex] is int height &&
                    width > 0 && height > 0)
                {
                    const int blockSizeX = 8;
                    const int blockSizeY = 8;
                    int gridSizeX = (width + blockSizeX - 1) / blockSizeX;
                    int gridSizeY = (height + blockSizeY - 1) / blockSizeY;
                    this.Kernel.BlockDimensions = new dim3(blockSizeX, blockSizeY, 1);
                    this.Kernel.GridDimensions = new dim3(gridSizeX, gridSizeY, 1);
                }
                else
                {
                    const int blockSize = 256;
                    int gridSize = (length + blockSize - 1) / blockSize;
                    this.Kernel.BlockDimensions = new dim3(blockSize, 1, 1);
                    this.Kernel.GridDimensions = new dim3(gridSize, 1, 1);
                }

                var nullPtrArgs = arguments.Select((a, i) => a is IntPtr && a.Equals(IntPtr.Zero));

                object[] kernelArguments = arguments.Select((argument, index) =>
                    argTypesSignature[index].IsPointer && argument is IntPtr pointer
                        ? pointer.Equals(IntPtr.Zero) ? this.Register : new CUdeviceptr(pointer)
                        : argument).ToArray();

                // EXEC. Use the synchronous launch here so the configured grid and block dimensions
                // are applied by the same path as the dedicated image launcher before the output is read.
                this.Kernel.Run(kernelArguments);
                this.Context.Synchronize();
            }
            catch (Exception ex)
            {
                await StaticLogger.LogAsync($"Failed to execute kernel '{this.KernelName ?? "N/A"}': {ex.Message}");
                return null;
            }
            finally
            {
                if (unloadWhenExecuted)
                {
                    this.Compiler.UnloadKernel(null);
                }
            }

            response.ElapsedMs = (int) (DateTime.Now - startTime).TotalMilliseconds;
            await StaticLogger.LogAsync($"Kernel executed '{this.KernelName ?? "N/A"}' in {response.ElapsedMs} ms");
            return response;
        }

        public RuntimeExecuteResponse? ExecuteGenericKernel(string? kernelName, object[] arguments, bool unloadWhenExecuted = false)
        {
            var response = new RuntimeExecuteResponse();

            // Set context first for thread-affine CUDA operations
            this.Context.SetCurrent();

            // If no kernelName provided but Kernel loaded, use that
            if (string.IsNullOrEmpty(kernelName) && this.Kernel == null)
            {
                StaticLogger.Log("No kernel name provided and no kernel is currently loaded.");
                return null;
            }

            // If kernelName equals current KernelName, use that, otherwise unload current kernel and load new one
            if (!string.IsNullOrEmpty(kernelName) && !string.Equals(this.KernelName, kernelName, StringComparison.Ordinal))
            {
                this.Compiler.UnloadKernel(null);
                this.Compiler.LoadKernel(kernelName);
            }
            if (this.Kernel == null)
            {
                StaticLogger.Log($"Kernel not loaded '{kernelName ?? "N/A"}'");
                return null;
            }

            // Verify arguments match Kernel signature
            Type[] argTypes = arguments.Select(a => a?.GetType() ?? typeof(object)).ToArray();
            Type[] argTypesSignature = this.Compiler.GetArguments(null).Values.ToArray();
            if (argTypes.Length != argTypesSignature.Length)
            {
                StaticLogger.Log($"Kernel argument count does not match signature '{kernelName ?? "N/A"}': expected {argTypesSignature.Length}, got {argTypes.Length}.");
                return null;
            }

            if (!argTypes.SequenceEqual(argTypesSignature))
            {
                string[] details = argTypes.Select((t, i) => new { Type = t, Index = i }).Where(x => x.Type != argTypesSignature[x.Index]).Select(x => $"<{x.Index}> {x.Type.Name} != {argTypesSignature[x.Index].Name}").ToArray();
                StaticLogger.Log($"Kernel arguments do not match signature '{kernelName ?? "N/A"}': {string.Join(", ", details)}");
                return null;
            }

            DateTime startTime = DateTime.Now;
            try
            {
                int length = 1;
                for (int i = 0; i < arguments.Length; i++)
                {
                    object argument = arguments[i];
                    if (argTypesSignature[i] == typeof(IntPtr))
                    {
                        if (argument is not IntPtr pointer || pointer == IntPtr.Zero || this.Register[pointer] is not CudaMem memory)
                        {
                            StaticLogger.Log($"Kernel pointer argument at index {i} is not a registered device buffer.");
                            return null;
                        }

                        long pointerLength = memory.IndexLength;
                        if (pointerLength <= 0 || pointerLength > int.MaxValue)
                        {
                            StaticLogger.Log("A registered kernel buffer has an invalid element length.");
                            return null;
                        }

                        length = Math.Max(length, (int) pointerLength);
                    }
                }

                var argumentDefinitions = this.Compiler.GetArguments(null);
                int widthIndex = argumentDefinitions.Keys
                    .Select((name, index) => new { name, index })
                    .FirstOrDefault(x => x.name.Contains("width", StringComparison.OrdinalIgnoreCase))?.index ?? -1;
                int heightIndex = argumentDefinitions.Keys
                    .Select((name, index) => new { name, index })
                    .FirstOrDefault(x => x.name.Contains("height", StringComparison.OrdinalIgnoreCase))?.index ?? -1;

                if (widthIndex >= 0 && heightIndex >= 0 &&
                    arguments[widthIndex] is int width && arguments[heightIndex] is int height &&
                    width > 0 && height > 0)
                {
                    const int blockSizeX = 8;
                    const int blockSizeY = 8;
                    int gridSizeX = (width + blockSizeX - 1) / blockSizeX;
                    int gridSizeY = (height + blockSizeY - 1) / blockSizeY;
                    this.Kernel.BlockDimensions = new dim3(blockSizeX, blockSizeY, 1);
                    this.Kernel.GridDimensions = new dim3(gridSizeX, gridSizeY, 1);
                }
                else
                {
                    const int blockSize = 256;
                    int gridSize = (length + blockSize - 1) / blockSize;
                    this.Kernel.BlockDimensions = new dim3(blockSize, 1, 1);
                    this.Kernel.GridDimensions = new dim3(gridSize, 1, 1);
                }

                response.ResultPointers = this.SetArgumentValues(ref arguments, argTypes)?.Select(np => np.ToString()).ToArray() ?? null;
                if (response.ResultPointers == null)
                {
                    StaticLogger.LogError("SetArgumentValues() returned null which means that at least one arg could not been set or pointer(s) null.");
                    return response;
                }

                // EXEC. Use the synchronous launch here so the configured grid and block dimensions
                // are applied by the same path as the dedicated image launcher before the output is read.
                this.Kernel.Run(arguments);
                this.Context.Synchronize();
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to execute kernel '{this.KernelName ?? "N/A"}': {ex.Message}");
                return null;
            }
            finally
            {
                if (unloadWhenExecuted)
                {
                    this.Compiler.UnloadKernel(null);
                }
            }

            response.ElapsedMs = (int) (DateTime.Now - startTime).TotalMilliseconds;
            StaticLogger.Log($"Kernel executed '{this.KernelName ?? "N/A"}' in {response.ElapsedMs} ms");
            return response;
        }


        internal IntPtr[]? SetArgumentValues(ref object[] arguments, Type[] argumentTypes)
        {
            List<IntPtr> newAllocated = [];
            int? count = arguments.Length == argumentTypes.Length ? arguments.Length : null;
            if (count == null)
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                object arg = arguments[i];
                Type t = argumentTypes[i];

                int maxStride = 1;
                CudaMem? mem = null;

                if (arg is IntPtr ptr)
                {
                    if (ptr != IntPtr.Zero)
                    {
                        mem = this.Register[ptr] as CudaMem;
                        arguments[i] = new CUdeviceptr(ptr);
                    }
                    else
                    {
                        if (t.IsPointer)
                        {
                            t = t.MakeGenericType();
                            var allocMethod = maxStride <= 1
                    ? typeof(IRuntimeRegister).GetMethod(nameof(IRuntimeRegister.AllocateSingle), [typeof(IntPtr), typeof(bool)])
                    : typeof(IRuntimeRegister).GetMethod(nameof(IRuntimeRegister.AllocateGroup), [typeof(IntPtr[]), typeof(bool)]);

                            mem = allocMethod?.MakeGenericMethod(t).Invoke(this, mem?.PointerLengths.Cast<object>().ToArray()) as CudaMem;
                            if (mem != null)
                            {
                                newAllocated.AddRange(mem.Pointers);
                                arguments[i] = new CUdeviceptr(mem.IndexPointer);
                            }
                            else
                            {
                                newAllocated.Add(IntPtr.Zero);
                            }
                        }
                    }
                }
            }

            return newAllocated.ToArray();
        }


        // Dispose
        /// <summary>
        /// Releases the resources used by the <see cref="CudaLauncher"/>.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

    }
}
