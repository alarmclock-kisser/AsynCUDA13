using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.NVRTC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using System.Reflection;
using AsynCUDA13.Shared.Serialization;

namespace AsynCUDA13.Runtime
{
    /// <summary>
    /// Discovers, compiles, loads and inspects CUDA kernels for the runtime.
    /// The compiler manages the on-disk kernel directory structure (CU sources, PTX output and logs),
    /// compiles <c>.cu</c> files or raw kernel strings to PTX via NVRTC, loads PTX modules into the CUDA
    /// context, and parses kernel signatures to map CUDA argument types to .NET types and build ordered
    /// argument arrays for execution.
    /// </summary>
    internal class CudaCompiler : IRuntimeCompiler, IDisposable
    {
        private readonly IRollingFileMemoryLogger Logger;

        // Fields
        /// <summary>The CUDA primary context used for compilation and kernel loading.</summary>
        private readonly PrimaryContext Context;

        /// <summary>
        /// The CUDA register that manages device memory and kernel execution.
        /// </summary>
        private readonly CudaRegister Register;

        /// <summary>The currently loaded CUDA kernel, or <c>null</c> if none is loaded.</summary>
        internal CudaKernel? Kernel = null;

        /// <summary>
        /// Gets the currently loaded CUDA kernel by name, or <c>null</c> if no kernel is loaded or the name does not match.
        /// </summary>
        /// <param name="name">The name of the kernel to get.</param>
        /// <returns>The currently loaded CUDA kernel, or <c>null</c> if no kernel is loaded or the name does not match.</returns>
        public object? GetKernel(string name)
        {
            return this.Kernel != null && string.Equals(this.KernelName, name, StringComparison.OrdinalIgnoreCase) ?  this.Kernel : (Object?) null;
        }

        /// <summary>
        /// Checks if a kernel with the specified name is currently loaded.
        /// </summary>
        /// <param name="name">The name of the kernel to check.</param>
        /// <returns><c>true</c> if the kernel is currently loaded; otherwise, <c>false</c>.</returns>
        public bool HasKernel(string name)
        {
            return this.Kernel != null && string.Equals(this.KernelName, name, StringComparison.OrdinalIgnoreCase);
        }

        public bool LoadKernel(string name)
        {
            var kernel = this.LoadKernel(name, false);
            return kernel != null;
        }

        /// <summary>The name of the currently loaded kernel, or <c>null</c> if none is loaded.</summary>
        public string? KernelName { get; private set; } = null;

        /// <summary>The PTX file path of the currently loaded kernel, or <c>null</c> if none is loaded.</summary>
        public string? KernelFile { get; private set; } = null;

        /// <summary>The source code (<c>.cu</c> content) of the currently loaded kernel, or <c>null</c> if unavailable.</summary>
        public string? KernelCode { get; private set; } = null;

        // Properties (static)
        /// <summary>Gets the resolved root directory that contains the CU, PTX and Logs sub-folders for kernels.</summary>
        public static string KernelPath = EnsureKernelDirectory();

        /// <summary>
        /// Gets the directory that contains the CUDA source (<c>.cu</c>) files for kernels.
        /// </summary>
        public string KernelDirectory => Path.Join(KernelPath, "CU");

        /// <summary>Gets the list of available CUDA source (<c>.cu</c>) files.</summary>
        public static List<string> SourceFiles => GetCuFiles();

        /// <summary>Gets the list of compiled PTX (<c>.ptx</c>) files.</summary>
        public static List<string> CompiledFiles => GetPtxFiles();

        /// <summary>
        /// Gets the list of available CUDA source files as an array of strings.
        /// </summary>
        /// <returns>An array of file paths to available CUDA source files.</returns>
        public string[] GetSourceFiles() => SourceFiles.ToArray();

        /// <summary>
        /// Gets the list of compiled PTX files as an array of strings.
        /// </summary>
        /// <returns>An array of file paths to compiled PTX files.</returns>
        public string[] GetCompiledFiles() => CompiledFiles.ToArray();

        /// <summary>
        /// Gets the source file of the kernel with the specified name.
        /// </summary>
        /// <param name="name">The name of the kernel.</param>
        /// <returns>The file path of the kernel source file, or <c>null</c> if not found.</returns>
        public string? GetKernelSourceFile(string name)
        {
            if (string.IsNullOrEmpty(this.KernelName))
            {
                return null;
            }

            if (File.Exists(name) && string.Equals(Path.GetExtension(name), ".cu", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(name);
            }

            string cuPath = Path.Combine(KernelPath, "CU", name + ".cu");
            return File.Exists(cuPath) ? cuPath :  null;
        }

        /// <summary>
        /// Gets the source code of the kernel with the specified name.
        /// </summary>
        /// <param name="kernel">The name or file path of the kernel.</param>
        /// <returns>The source code of the kernel, or <c>null</c> if not found.</returns>
        public string? GetKernelCode(string? kernel)
        {
            kernel ??= this.KernelName;
            if (string.IsNullOrWhiteSpace(kernel))
            {
                return null;
            }

            // If it's an existing file path, read it directly
            if (File.Exists(kernel))
            {
                return File.ReadAllText(kernel);
            }

            // Extract the base name (without extension)
            string fileName = Path.GetFileNameWithoutExtension(kernel);
            // Try in KernelPath/CU (most reliable location for .cu files)
            string cuPath = Path.Combine(KernelPath, "CU", fileName + ".cu");
            if (File.Exists(cuPath))
            {
                return File.ReadAllText(cuPath);
            }

            // Try to find .cu file by name or full path
            if (!Path.HasExtension(kernel))
            {
                // Already tried KernelPath/CU above
                // Try in the project's Kernels/CU directory (relative to this assembly)
                string assemblyDir = AppContext.BaseDirectory;
                string projectCuPath = Path.Combine(assemblyDir, "..", "..", "..", "Kernels", "CU", kernel + ".cu");
                if (File.Exists(projectCuPath))
                {
                    return File.ReadAllText(projectCuPath);
                }
            }
            else
            {
                // Try with the full path (handle both .cu and .ptx extensions)
                string dir = Path.GetDirectoryName(kernel) ?? "";
                if (!string.IsNullOrEmpty(dir))
                {
                    // Try co-located .cu file in the same directory as the .ptx/.cu file
                    string coLocatedCu = Path.Combine(dir, fileName + ".cu");
                    if (File.Exists(coLocatedCu))
                    {
                        return File.ReadAllText(coLocatedCu);
                    }
                    // Also try in KernelPath/CU for PTX files
                    string kernelPathCu = Path.Combine(KernelPath, "CU", fileName + ".cu");
                    if (File.Exists(kernelPathCu))
                    {
                        return File.ReadAllText(kernelPathCu);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the function name of the kernel with the specified name, file path, or source code.
        /// </summary>
        /// <param name="kernel">The name, file path, or source code of the kernel.</param>
        /// <returns>The function name of the kernel, or <c>null</c> if not found.</returns>
        public string? GetFunctionName(string? kernel)
        {
            kernel ??= this.KernelName;
            if (string.IsNullOrWhiteSpace(kernel))
            {
                return null;
            }

            // If it's an existing file path, read it directly
            if (File.Exists(kernel))
            {
                kernel = File.ReadAllText(kernel);
            }

            return this.PrecompileKernel(kernel);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaCompiler"/> class.
        /// </summary>
        /// <param name="context">The CUDA primary context to use for compilation and kernel loading.</param>
        public CudaCompiler(PrimaryContext context, CudaRegister register, IRollingFileMemoryLogger logger)
        {
            this.Context = context;
            this.Register = register;
            this.Logger = logger;

            KernelPath = EnsureKernelDirectory();
            try
            {
                Directory.CreateDirectory(KernelPath);
                Directory.CreateDirectory(Path.Combine(KernelPath, "CU"));
                Directory.CreateDirectory(Path.Combine(KernelPath, "PTX"));
                Directory.CreateDirectory(Path.Combine(KernelPath, "Logs"));
            }
            catch (Exception ex)
            {
                this.Logger.Log("Failed to create kernel directory, using temporary path", ex);
                KernelPath = Path.Combine(Path.GetTempPath(), "AsynCUDA13", "Kernels");
                Directory.CreateDirectory(KernelPath);
                Directory.CreateDirectory(Path.Combine(KernelPath, "CU"));
                Directory.CreateDirectory(Path.Combine(KernelPath, "PTX"));
                Directory.CreateDirectory(Path.Combine(KernelPath, "Logs"));
            }

            // Compile all kernels
            this.CompileAll(false, true);
        }

        /// <summary>
        /// Gets the IRuntimeCompiler interface for this instance.
        /// </summary>
        public IRuntimeCompiler Compiler => this;

        /// <summary>
        /// Attempts to create a directory at the specified path.
        /// </summary>
        /// <param name="path">The path of the directory to create.</param>
        /// <param name="createdPath">When this method returns, contains the path if the directory was successfully created; otherwise, an empty string.</param>
        /// <returns>True if the directory was successfully created; otherwise, false.</returns>
        private static bool TryCreateDirectory(string path, out string createdPath)
        {
            try
            {
                Directory.CreateDirectory(path);
                createdPath = path;
                return true;
            }
            catch
            {
                createdPath = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Ensures that the kernel directory exists, attempting to find a suitable location in the application structure.
        /// </summary>
        /// <param name="differentPath">An optional alternative path to use instead of the default kernel directory.</param>
        /// <returns>The path to the kernel directory.</returns>
        private static string EnsureKernelDirectory(string? differentPath = null)
        {
            if (!string.IsNullOrWhiteSpace(differentPath) && TryCreateDirectory(differentPath, out var customDir))
            {
                return customDir;
            }

            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "AsynCUDA";
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && current != null; i++)
            {
                if (string.Equals(current.Name, assemblyName + ".Runtime", StringComparison.OrdinalIgnoreCase))
                {
                    string runtimeDir = Path.Combine(current.FullName, "Kernels");
                    if (TryCreateDirectory(runtimeDir, out var createdRuntimeDir))
                    {
                        return createdRuntimeDir;
                    }
                }

                string siblingRuntime = Path.Combine(current.FullName, assemblyName + ".Runtime");
                if (Directory.Exists(siblingRuntime))
                {
                    string candidate = Path.Combine(siblingRuntime, "Kernels");
                    if (TryCreateDirectory(candidate, out var created))
                    {
                        return created;
                    }
                }

                current = current.Parent;
            }

            if (TryCreateDirectory(Path.Combine(AppContext.BaseDirectory, "Kernels"), out var assemblyDir))
            {
                return assemblyDir;
            }

            string fallbackPath = Path.Combine(Path.GetTempPath(), assemblyName, "Kernels");
            Directory.CreateDirectory(fallbackPath);
            return fallbackPath;
        }

        /// <summary>
        /// Reads all bytes from a file with a retry mechanism to handle potential file locking issues.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        /// <param name="retries">The number of times to retry reading the file if an IOException occurs.</param>
        /// <param name="delayMs">The delay in milliseconds between retries.</param>
        /// <returns>A byte array containing the file contents.</returns>
        /// <exception cref="IOException">Thrown if the file cannot be read after all retry attempts.</exception>
        private static Byte[] ReadAllBytesWithRetry(string path, int retries = 3, int delayMs = 50)
        {
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using MemoryStream ms = new();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
                catch (IOException) when (attempt < retries)
                {
                    Thread.Sleep(delayMs);
                }
            }

            using FileStream finalStream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using MemoryStream finalMs = new();
            finalStream.CopyTo(finalMs);
            return finalMs.ToArray();
        }




        /// <summary>
        /// Gets a list of all PTX files in the specified directory.
        /// </summary>
        /// <param name="path">The directory to search for PTX files. If null, uses the default KernelPath/PTX directory.</param>
        /// <returns>A list of full paths to the found PTX files.</returns>
        public static List<string> GetPtxFiles(string? path = null)
        {
            path ??= Path.Combine(KernelPath, "PTX");

            // Get all PTX files in kernel path
            string[] files = Directory.GetFiles(path, "*.ptx").Select(f => Path.GetFullPath(f)).ToArray();

            // Return files
            return files.ToList();
        }

        /// <summary>
        /// Gets a list of all CU files in the specified directory.
        /// </summary>
        /// <param name="path">The directory to search for CU files. If null, uses the default KernelPath/CU directory.</param>
        /// <returns>A list of full paths to the found CU files.</returns>
        public static List<string> GetCuFiles(string? path = null)
        {
            path ??= Path.Combine(KernelPath, "CU");

            // Get all CU files in kernel path
            string[] files = Directory.GetFiles(path, "*.cu").Select(f => Path.GetFullPath(f)).ToArray();

            // Return files
            return files.ToList();
        }

        /// <summary>
        /// Unloads the currently loaded CUDA kernel and clears the kernel state.
        /// </summary>
        public void UnloadKernel(string? name)
        {
            // Set context for thread-affine CUDA operations
            this.Context.SetCurrent();

            // Unload kernel
            if (this.Kernel != null)
            {
                try
                {
                    this.Context.UnloadKernel(this.Kernel);
                }
                catch (Exception ex)
                {
                    this.Logger.Log("Failed to unload kernel", ex);
                }
                this.Kernel = null;
            }

            this.KernelName = null;
            this.KernelFile = null;
            this.KernelCode = null;
        }

        /// <summary>
        /// Loads a CUDA kernel from a PTX file or a .cu file.
        /// </summary>
        /// <param name="kernelName">The name of the kernel to load. Can be a filename or a kernel name.</param>
        /// <param name="silent">If true, suppresses logging during the loading process.</param>
        /// <returns>The loaded <see cref="CudaKernel"/>, or null if loading failed.</returns>
        internal CudaKernel? LoadKernel(string kernelName, bool silent = false)
        {
            if (this.Context == null)
            {
                this.Logger.Log("No CUDA context available");
                return null;
            }

            // Set context for thread-affine CUDA operations
            this.Context.SetCurrent();

            // Unload?
            if (this.Kernel != null)
            {
                this.UnloadKernel(this.KernelName);
            }

            string displayName = kernelName;
            string ptxPath;
            string cuPath;
            bool isPtxPath = File.Exists(kernelName) && string.Equals(Path.GetExtension(kernelName), ".ptx", StringComparison.OrdinalIgnoreCase);
            if (isPtxPath)
            {
                ptxPath = Path.GetFullPath(kernelName);
                displayName = Path.GetFileNameWithoutExtension(ptxPath);
                string ptxDirectory = Path.GetDirectoryName(ptxPath) ?? string.Empty;
                string coLocatedCu = Path.Combine(ptxDirectory, displayName + ".cu");
                cuPath = File.Exists(coLocatedCu) ? coLocatedCu : Path.Combine(KernelPath, "CU", displayName + ".cu");
            }
            else
            {
                ptxPath = Path.Combine(KernelPath, "PTX", displayName + ".ptx");
                cuPath = Path.Combine(KernelPath, "CU", displayName + ".cu");
            }

            // Log
            Stopwatch sw = Stopwatch.StartNew();
            if (!silent)
            {
                this.Logger.Log("Started loading kernel " + displayName);
            }
            string logpath = Path.Combine(KernelPath, "Logs", displayName + "_load.log");

            if (File.Exists(cuPath) && (!File.Exists(ptxPath) || File.GetLastWriteTimeUtc(cuPath) > File.GetLastWriteTimeUtc(ptxPath)))
            {
                if (this.CompileKernel(cuPath) == null)
                {
                    if (!silent)
                    {
                        this.Logger.Log("Failed to compile updated kernel " + displayName);
                    }

                    return null;
                }
            }

            // Try to load kernel
            try
            {
                // Load ptx code
                Byte[] ptxCode = ReadAllBytesWithRetry(ptxPath);

                // Load kernel
                this.Kernel = this.Context.LoadKernelPTX(ptxCode, displayName);
                this.KernelName = displayName;
                this.KernelFile = ptxPath;
                this.KernelCode = File.Exists(cuPath) ? File.ReadAllText(cuPath) : null;

                // Log
                sw.Stop();
                long deltaMicros = sw.ElapsedTicks / (Stopwatch.Frequency / (1000L * 1000L));
                if (!silent)
                {
                    this.Logger.Log($"Kernel loaded within {deltaMicros.ToString("N0")} µs");
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    this.Logger.Log("Failed to load kernel " + displayName, ex);
                    string logMsg = ex.Message + Environment.NewLine + Environment.NewLine + ex.InnerException?.Message ?? "";
                    File.WriteAllText(logpath, logMsg);
                }
                this.Kernel = null;
            }

            return this.Kernel;
        }


        /// <summary>
        /// Compiles all source files found in the kernel directory.
        /// </summary>
        /// <param name="silent">If true, suppresses logging during compilation.</param>
        /// <param name="logErrors">If true, logs errors for each failed compilation.</param>
        public void CompileAll(bool silent = false, bool logErrors = false)
        {
            List<string> sourceFiles = SourceFiles;

            // Compile all source files
            foreach (string sourceFile in sourceFiles)
            {
                string? ptx = this.CompileKernel(sourceFile);
                if (string.IsNullOrEmpty(ptx) && logErrors)
                {
                    this.Logger.Log($"Compilation failed: {Path.GetFileNameWithoutExtension(sourceFile)}");
                }
            }
        }

        /// <summary>
        /// Compiles a float CUDA kernel from a file or a string.
        /// </summary>
        /// <param name="filepath">The path to the .cu file, or a raw kernel string if the extension is not .cu.</param>
        /// <param name="silent">If true, suppresses logging during compilation.</param>
        /// <returns>The path to the generated PTX file, or build log (which is not a File that exists) if compilation failed. Returns null if CUDA was not initialized or not available.</returns>
        public string CompileKernel(string filepath)
        {
            bool silent = false;
            if (this.Context == null)
            {
                if (!silent)
                {
                    this.Logger.Log("No CUDA initialized");
                }
                return "";
            }

            // Set context for thread-affine CUDA operations
            this.Context.SetCurrent();

            // If file is not a .cu file, but raw kernel string, compile that
            if (Path.GetExtension(filepath) != ".cu")
            {
                return this.CompileString(filepath, silent) ?? "CUDA not initialized!";
            }

            string kernelName = Path.GetFileNameWithoutExtension(filepath);

            string logpath = Path.Combine(KernelPath, "Logs", kernelName + ".log");

            Stopwatch sw = Stopwatch.StartNew();
            if (!silent)
            {
                this.Logger.Log("Compiling kernel '" + kernelName + "'");
            }

            // Load kernel file
            string kernelCode = File.ReadAllText(filepath);


            CudaRuntimeCompiler rtc = new(kernelCode, kernelName);
            string log = string.Empty;

            try
            {
                // Compile kernel
                rtc.Compile([]);
                log = rtc.GetLogAsString();

                if (log.Length > 0)
                {
                    // Count double \n
                    int count = log.Split(["\n\n"], StringSplitOptions.None).Length - 1;
                    if (!silent)
                    {
                        this.Logger.Log($"Compiled with {count} warnings");
                    }
                    File.WriteAllText(logpath, log);
                }

                sw.Stop();
                long deltaMicros = sw.ElapsedTicks / (Stopwatch.Frequency / (1000L * 1000L));
                if (!silent)
                {
                    this.Logger.Log($"Compiled within {deltaMicros} µs. Repo\\" + Path.GetRelativePath(KernelPath, logpath));
                }

                // Get ptx code
                Byte[] ptxCode = rtc.GetPTX();

                // Export ptx
                string ptxPath = Path.Combine(KernelPath, "PTX", kernelName + ".ptx");
                File.WriteAllBytes(ptxPath, ptxCode);

                if (!silent)
                {
                    this.Logger.Log($"PTX exported: {ptxPath}");
                }

                return ptxPath;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logpath, log);
                this.Logger.Log(ex);

                return log;
            }

        }

        /// <summary>
        /// Compiles a CUDA kernel from a raw string.
        /// </summary>
        /// <param name="kernelstring">The raw CUDA kernel source code.</param>
        /// <param name="silent">If true, suppresses logging during compilation.</param>
        /// <returns>The path to the generated PTX file, or the build log if compilation failed. Returns null if CUDA was not available or initialized.</returns>
        public string? CompileString(string kernelstring, bool silent = false)
        {
            if (this.Context == null)
            {
                if (!silent)
                {
                    this.Logger.Log("No CUDA initialized");
                }
                return null;
            }

            // Set context for thread-affine CUDA operations
            this.Context.SetCurrent();

            string kernelName = kernelstring.Split("void ")[1].Split("(")[0];

            string logpath = Path.Combine(KernelPath, "Logs", kernelName + ".log");

            Stopwatch sw = Stopwatch.StartNew();
            if (!silent)
            {
                this.Logger.Log("Compiling kernel '" + kernelName + "'");
            }

            // Load kernel file
            string kernelCode = kernelstring;

            // Save also the kernel string as .c file
            string cPath = Path.Combine(KernelPath, "CU", kernelName + ".cu");
            File.WriteAllText(cPath, kernelCode);


            CudaRuntimeCompiler rtc = new(kernelCode, kernelName);
            string log = string.Empty;

            try
            {
                // Compile kernel
                rtc.Compile([]);
                log = rtc.GetLogAsString();

                if (log.Length > 0)
                {
                    // Count double \n
                    int count = log.Split(["\n\n"], StringSplitOptions.None).Length - 1;
                    if (!silent)
                    {
                        this.Logger.Log($"Compiled with {count} warnings");
                    }
                    File.WriteAllText(logpath, rtc.GetLogAsString());
                }


                sw.Stop();
                long deltaMicros = sw.ElapsedTicks / (Stopwatch.Frequency / (1000L * 1000L));
                if (!silent)
                {
                    this.Logger.Log($"Compiled within {deltaMicros} µs. Repo\\" + Path.GetRelativePath(KernelPath, logpath));
                }


                // Get ptx code
                Byte[] ptxCode = rtc.GetPTX();

                // Export ptx
                string ptxPath = Path.Combine(KernelPath, "PTX", kernelName + ".ptx");
                File.WriteAllBytes(ptxPath, ptxCode);

                if (!silent)
                {
                    this.Logger.Log($"PTX exported: {ptxPath}");
                }

                return ptxPath;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logpath, log);
                this.Logger.Log(ex);

                return log;
            }
        }

        /// <summary>
        /// Performs a preliminary check on a kernel string to ensure it follows expected patterns.
        /// </summary>
        /// <param name="code">The raw CUDA kernel source code to check.</param>
        /// <param name="silent">If true, suppresses logging during the check.</param>
        /// <returns>The extracted kernel name if valid; otherwise, null.</returns>
        public string? PrecompileKernel(string code)
        {
            bool silent = false;
            // Check contains "extern c"
            if (!code.Contains("extern \"C\""))
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string does not contain 'extern \"C\"'");
                }
                return null;
            }

            // Check contains "__global__ "
            if (!code.Contains("__global__"))
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string does not contain '__global__'");
                }
                return null;
            }

            // Check contains "void "
            if (!code.Contains("void "))
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string does not contain 'void '");
                }
                return null;
            }

            // Check contains int
            if (!code.Contains("int ") && !code.Contains("long "))
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string does not contain 'int ' (for array length)");
                }
                return null;
            }

            // Check if every bracket is closed (even amount) for {} and () and []
            int open = code.Count(c => c == '{');
            int close = code.Count(c => c == '}');
            if (open != close)
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string has unbalanced brackets { } ");
                }
                return null;
            }
            open = code.Count(c => c == '(');
            close = code.Count(c => c == ')');
            if (open != close)
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string has unbalanced brackets ( ) ");
                }
                return null;
            }
            open = code.Count(c => c == '[');
            close = code.Count(c => c == ']');
            if (open != close)
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string has unbalanced brackets [ ] ");
                }
                return null;
            }

            // Check if kernel contains "blockIdx.x" and "blockDim.x" and "threadIdx.x"
            if (!code.Contains("blockIdx.x") || !code.Contains("blockDim.x") || !code.Contains("threadIdx.x"))
            {
                if (!silent)
                {
                    this.Logger.Log("Kernel string should contain 'blockIdx.x', 'blockDim.x' and 'threadIdx.x'");
                }
            }

            // Get name between "void " and "("
            int start = code.IndexOf("void ") + "void ".Length;
            int end = code.IndexOf("(", start);
            string name = code.Substring(start, end - start);

            // Trim every line ends from empty spaces (split -> trim -> aggregate)
            code = code.Split("\n").Select(x => x.TrimEnd()).Aggregate((x, y) => x + "\n" + y);

            // Log name
            if (!silent)
            {
                this.Logger.Log($"Succesfully precompiled kernel string '{name}'");
            }

            return name;
        }


        // Methods (Arguments)
        /// <summary>
        /// Maps a CUDA type name string to its corresponding .NET <see cref="Type"/>.
        /// </summary>
        /// <param name="typeName">The name of the type (e.g., "int", "float", "double").</param>
        /// <returns>The corresponding .NET <see cref="Type"/>.</returns>
        public Type GetArgumentType(string typeName)
        {
            // Pointers are always IntPtr (containing *)
            bool isPtr = typeName.Contains("*");
            typeName = typeName.Replace("*", "").Trim();

            string typeIdentifier = typeName.Split(' ').LastOrDefault()?.Trim() ?? "void";
            Type type = typeIdentifier switch
            {
                "int" => typeof(int),
                "float" => typeof(float),
                "double" => typeof(double),
                "char" => typeof(Char),
                "bool" => typeof(bool),
                "void" => typeof(void),
                "byte" => typeof(Byte),
                _ => typeof(void)
            };

            if (isPtr)
            {
                type = typeof(IntPtr);
            }

            return type;
        }

        /// <summary>
        /// Parses a kernel's source code to extract its argument names and types.
        /// </summary>
        /// <param name="kernel">The source code of the kernel, or a path/name to resolve it.</param>
        /// <param name="silent">If true, suppresses logging during parsing.</param>
        /// <returns>A dictionary mapping argument names to their .NET <see cref="Type"/>.</returns>
        public Dictionary<string, Type> GetArguments(string? kernel = null)
        {
            string? sourceCode = null;

            // If the input looks like source code (contains __global__), parse it directly
            if (!string.IsNullOrWhiteSpace(kernel) && kernel.Contains("__global__"))
            {
                sourceCode = kernel;
            }
            else
            {
                // Otherwise, try to resolve it as a file path or name
                sourceCode = this.GetKernelCode(kernel);
            }

            // If no source code yet, try to load from KernelCode property
            if (string.IsNullOrEmpty(sourceCode))
            {
                sourceCode = this.KernelCode;
            }

            // If no code is available yet, try to resolve the co-located .cu file based on KernelFile
            if (string.IsNullOrEmpty(sourceCode) && !string.IsNullOrEmpty(this.KernelFile))
            {
                string displayName = Path.GetFileNameWithoutExtension(this.KernelFile);
                string ptxDirectory = Path.GetDirectoryName(this.KernelFile) ?? string.Empty;
                string coLocatedCu = Path.Combine(ptxDirectory, displayName + ".cu");
                string fallbackCu = Path.Combine(KernelPath, "CU", displayName + ".cu");
                string resolvedCu = File.Exists(coLocatedCu) ? coLocatedCu : fallbackCu;

                if (File.Exists(resolvedCu))
                {
                    sourceCode = File.ReadAllText(resolvedCu);
                }
            }

            if (string.IsNullOrEmpty(sourceCode))
            {
                return [];
            }

            Dictionary<string, Type> arguments = [];

            // Regex to find kernel signature: __global__ followed by optional extern "C", then void function_name(args)
            // Handles various whitespace/newline formats
            var pattern = new Regex(
                @"__global__\s+(?:extern\s+""C""\s+)?void\s+(\w+)\s*\(([^)]*)\)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            var match = pattern.Match(sourceCode);
            if (!match.Success)
            {
                return [];
            }

            string argsstring = match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(argsstring))
            {
                return arguments;
            }

            string[] args = argsstring.Split(',').Select(x => x.Trim()).ToArray();

            // Get loaded kernels function args
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                // Parse argument: "type name" or "type* name" or "type& name" etc.
                // The name is always the last word, everything before is the type
                string[] parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                string name = parts[^1]; // Last element is the name
                string typeName = string.Join(" ", parts.Take(parts.Length - 1)).Trim();
                Type type = this.GetArgumentType(typeName);

                // Add to dictionary
                arguments.Add(name, type);
            }

            return arguments;
        }

        /// <summary>
        /// Counts the number of pointer arguments (IntPtr) in a kernel.
        /// </summary>
        /// <param name="kernelCode">The source code of the kernel, or a path/name to resolve it.</param>
        /// <param name="silent">If true, suppresses logging.</param>
        /// <returns>The number of pointer arguments.</returns>
        public int GetPointerArgsCount(string? kernelCode = null, bool silent = false)
        {
            kernelCode ??= this.KernelCode;
            if (string.IsNullOrEmpty(kernelCode) || this.Kernel == null)
            {
                if (!silent)
                {
                    this.Logger.Log($"Kernel code is empty '{this.KernelName ?? "N/A"}'");
                }
                return 0;
            }

            Dictionary<string, Type> args = this.GetArguments(kernelCode);

            return args.Values.Count(t => t == typeof(IntPtr));
        }


        // Merge args for execution
        /// <summary>
        /// Merges a provided array of values into a correctly ordered array for kernel execution based on argument definitions.
        /// </summary>
        /// <param name="arguments">The array of values to merge.</param>
        /// <returns>An array of objects ordered for kernel execution.</returns>
        public object[] MergeArgumentsRaw(object[]? arguments)
        {
            // Get kernel argument definitions
            Dictionary<string, Type> args = this.GetArguments(null);

            arguments = DataParser.AreAllArgumentsString(arguments) ? DataParser.ParseArgumentValues(arguments, args.Values) : arguments ?? [];

            // Create array for kernel arguments
            object[] kernelArgs = new object[args.Count];
            // Integrate invariables if name fits (contains)
            for (int i = 0; i < kernelArgs.Length; i++)
            {
                string name = args.ElementAt(i).Key;
                // Check if argument is in arguments array
                for (int j = 0; j < arguments.Length; j++)
                {
                    if (name == args.ElementAt(j).Key)
                    {
                        kernelArgs[i] = arguments[j];
                        break;
                    }
                }
                // If not found, set to 0
                if (kernelArgs[i] == null)
                {
                    kernelArgs[i] = 0;
                }
            }
            return kernelArgs;
        }

        /// <summary>
        /// Merges audio-specific parameters into a kernel argument array.
        /// </summary>
        /// <param name="inputPointer">The input data pointer.</param>
        /// <param name="outputPointer">The output data pointer.</param>
        /// <param name="sampleRate">The audio sample rate.</param>
        /// <param name="channels">The number of audio channels.</param>
        /// <param name="bitdepth">The audio bit depth.</param>
        /// <param name="namedArguments">Optional dictionary of additional named arguments.</param>
        /// <returns>An array of objects ordered for kernel execution.</returns>
        public object[] MergeArgumentsAudio(IntPtr inputPointer, IntPtr outputPointer, int sampleRate = 44100, int channels = 2, int bitdepth = 32, Dictionary<string, object>? namedArguments = null)
        {
            // Get kernel argument definitions
            Dictionary<string, Type> args = this.GetArguments(null);

            // Create array for kernel arguments
            object[] kernelArgs = new object[args.Count];
            int pointersCount = 0;

            // Integrate invariables if name fits (contains)
            for (int i = 0; i < kernelArgs.Length; i++)
            {
                string name = args.ElementAt(i).Key;
                Type type = args.ElementAt(i).Value;
                if (pointersCount == 0 && type == typeof(IntPtr))
                {
                    kernelArgs[i] = inputPointer;
                    pointersCount++;
                    this.Logger.Log($"In-pointer: <{inputPointer}>");
                }
                else if (pointersCount == 1 && type == typeof(IntPtr))
                {
                    kernelArgs[i] = outputPointer;
                    pointersCount++;
                    this.Logger.Log($"Out-pointer: <{outputPointer}>");
                }
                else if (name.Contains("sample") && type == typeof(int))
                {
                    this.Logger.Log($"SampleRate: [{sampleRate}]");
                }
                else if (name.Contains("chan") && type == typeof(int))
                {
                    kernelArgs[i] = channels;
                    this.Logger.Log($"Channels: [{channels}]");
                }
                else if (name.Contains("bit") && type == typeof(int))
                {
                    kernelArgs[i] = bitdepth;
                    this.Logger.Log($"Bits: [{bitdepth}]");
                }
                else
                {
                    // Check if argument is in arguments array
                    if (namedArguments != null && namedArguments.Count > 0)
                    {
                        for (int j = 0; j < namedArguments.Count; j++)
                        {
                            if (name.Equals(args.ElementAt(j).Key, StringComparison.CurrentCultureIgnoreCase))
                            {
                                if (namedArguments.TryGetValue(name, out object? value))
                                {
                                    kernelArgs[i] = value;
                                    this.Logger.Log($"Named argument: {name} = {value}");
                                    break;
                                }
                                else
                                {
                                    this.Logger.Log($"Named argument '{name}' not found in provided arguments");
                                    kernelArgs[i] = 0;
                                }
                            }
                        }
                    }

                    // If not found, set to 0
                    if (kernelArgs[i] == null)
                    {
                        kernelArgs[i] = 0;
                    }
                }
            }

            return kernelArgs;
        }

        /// <summary>
        /// Merges the provided input and output pointers, image dimensions, channels, bit depth, and user-supplied arguments into a single array of kernel arguments for OpenCL execution.
        /// </summary>
        /// <param name="inputPointer">The pointer to the input image data.</param>
        /// <param name="outputPointer">The pointer to the output image data.</param>
        /// <param name="width">The width of the image.</param>
        /// <param name="height">The height of the image.</param>
        /// <param name="channels">The number of channels in the image.</param>
        /// <param name="bitdepth">The bit depth of the image.</param>
        /// <param name="additionalArgs">The user-supplied arguments for the kernel.</param>
        /// <returns>An array of merged kernel arguments.</returns>
        /// <exception cref="ArgumentException">Thrown if an argument type does not match the expected type.</exception>
        public object[] MergeArgumentsImage(IntPtr? inputPointer, IntPtr? outputPointer, int width, int height, int channels = 4, int bitdepth = 32, object[]? additionalArgs = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

            string? kernel = this.KernelName;
            if (kernel == null)
            {
                this.Logger.LogError("OpenClCompiler: MergeArgumentsImage called with no kernel loaded, returning empty arguments array.");
                return [];
            }

            // Get kernel argument definitions
            Dictionary<string, Type> args = this.GetArguments(null);
            additionalArgs = DataParser.AreAllArgumentsString(additionalArgs) ? DataParser.ParseArgumentValues(additionalArgs, args.Values) : additionalArgs ?? [];

            // If single pointer argument and input is null, use output pointer instead (for in-place operations)
            int pointerArgumentCount = args.Values.Count(type => type.IsPointer);
            if (pointerArgumentCount == 1 && IsNullPointer(inputPointer) && !IsNullPointer(outputPointer))
            {
                inputPointer = outputPointer;
            }

            // Calculate expected length of input data based on width, height, and channels as a fallback if when there is no reference input MemObj TotalLength to get
            nint expectedLen = checked((nint) ((long) width * height * channels));
            nint? inputPtrLen = inputPointer.HasValue ? (nint) (this.Register[inputPointer.Value]?.TotalLength ?? expectedLen) : null;

            // Create array for kernel arguments with pointers and index counters
            object[] kernelArgs = new object[args.Count];
            int pointersCount = 0;
            int userArgIndex = 0;

            // Integrate invariables if name fits (contains)
            for (int i = 0; i < kernelArgs.Length; i++)
            {
                // Get argument name and type
                string name = args.ElementAt(i).Key;
                Type type = args.ElementAt(i).Value;

                // Handle first pointer argument (input)
                if (pointersCount == 0 && type.IsPointer)
                {
                    // If inputPointer is null, allocate a new buffer for the input data, throw if that failed somehow
                    if (IsNullPointer(inputPointer))
                    {
                        inputPointer = this.Register.AllocateSingle<byte>(inputPtrLen ?? expectedLen)?.IndexPointer;
                        if (IsNullPointer(inputPointer))
                        {
                            throw new ArgumentException("Input pointer is null and could not be allocated.");
                        }
                    }

                    // Verify that the input pointer is registered in OpenClRegister and compare its index pointer
                    IntPtr inPtr = this.Register[inputPointer!.Value]?.IndexPointer ?? IntPtr.Zero;
                    if (IsNullPointer(inPtr) || inPtr != inputPointer.Value)
                    {
                        throw new ArgumentException($"Input pointer {inputPointer} is not registered in OpenClRegister, or it returned another pointer (<{inPtr}> != <{inputPointer.Value}>)");
                    }

                    // Set the kernel arg to that input pointer and increment the pointer count
                    kernelArgs[i] = inPtr;
                    pointersCount++;
                    this.Logger.Log($"In-pointer: <{inPtr}>");
                }
                // Handle second pointer argument (output)
                else if (pointersCount == 1 && type.IsPointer)
                {
                    // If outputPointer is null, allocate a new buffer, throw if failed
                    if (IsNullPointer(outputPointer))
                    {
                        outputPointer = this.Register.AllocateSingle<byte>(inputPtrLen ?? expectedLen)?.IndexPointer;
                        if (IsNullPointer(outputPointer))
                        {
                            throw new ArgumentException("Output pointer is null and could not be allocated.");
                        }
                    }

                    // Verify that the output pointer is registered in OpenClRegister and compare its index pointer
                    IntPtr outPtr = this.Register[outputPointer!.Value]?.IndexPointer ?? IntPtr.Zero;
                    if (IsNullPointer(outPtr) || outPtr != outputPointer.Value)
                    {
                        throw new ArgumentException($"Output pointer {outputPointer} is not registered in OpenClRegister, or it returned another pointer (<{outPtr}> != <{outputPointer.Value}>)");
                    }

                    // Set the kernel arg to that output pointer and increment the pointer count
                    kernelArgs[i] = outPtr;
                    pointersCount++;
                    this.Logger.Log($"Out-pointer: <{outPtr}>");
                }
                else if (name.Contains("width") && type == typeof(int))
                {
                    kernelArgs[i] = width;

                    this.Logger.Log($"Width: {name}=[{width}]");
                }
                else if (name.Contains("height") && type == typeof(int))
                {
                    kernelArgs[i] = height;

                    this.Logger.Log($"Height: {name}=[{height}]");
                }
                else if (name.Contains("chan") && type == typeof(int))
                {
                    kernelArgs[i] = channels;
                    this.Logger.Log($"Channels: {name}=[{channels}]");
                }
                else if (name.Contains("bit") && type == typeof(int))
                {
                    kernelArgs[i] = bitdepth;
                    this.Logger.Log($"Bits: {name}=[{bitdepth}]");
                }
                else
                {
                    // Every remaining slot is a user-supplied scalar. The caller passes these in the exact same
                    // order the kernel declares them (pointers and width/height/chan/bit excluded), so consume
                    // them sequentially instead of matching by name/index which mixed up the two lists before.
                    if (userArgIndex < additionalArgs.Length)
                    {
                        // If the argument is a string, try to parse it to the correct type
                        if ((additionalArgs[userArgIndex] is string stringValue) && !type.IsPointer)
                        {
                            try
                            {
                                kernelArgs[i] = Convert.ChangeType(stringValue, type);
                            }
                            catch (Exception ex)
                            {
                                throw new ArgumentException($"Failed to parse argument '{name}' of type '{type.Name}' with value '{stringValue}': {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            kernelArgs[i] = additionalArgs[userArgIndex];
                        }
                        userArgIndex++;
                    }
                    else
                    {
                        kernelArgs[i] = type.IsValueType ? Activator.CreateInstance(type) ?? 0U : 0U;
                    }
                }

                if (userArgIndex != additionalArgs.Length)
                {
                    this.Logger.Log($"{additionalArgs.Length - userArgIndex} unused user arguments for kernel '{kernel}': {string.Join(", ", additionalArgs.Skip(userArgIndex))}");
                }
            }

            // Return kernel arguments
            return kernelArgs;
        }

        internal object[] MergeArgumentsImage(CudaMem inputMem, CudaMem outputMem, int width, int height, int channels, int bitdepth, object[] arguments)
        {
            return this.MergeArgumentsImage(inputMem.IndexPointer, outputMem.IndexPointer, width, height, channels, bitdepth, arguments);
        }






        /// <summary>
        /// Releases the resources used by the <see cref="CudaCompiler"/>.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }




        // Static helpers
        public static bool IsNullPointer(IntPtr? pointer)
        {
            return !pointer.HasValue || pointer.Value == IntPtr.Zero;
        }


    }
}
