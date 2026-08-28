using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Compute.OpenCL;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Serialization;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Discovers OpenCL kernel source files (<c>*.cl</c>), compiles them in memory for a float device and
    /// exposes the compiled kernels by name. No binaries are written to disk; the programs and kernels live
    /// only in memory for the lifetime of this compiler.
    /// </summary>
    internal sealed class OpenClCompiler : IRuntimeCompiler, IDisposable
    {
        /// <summary>
        /// Matches the name of every <c>__kernel void</c> entry point in a kernel source file.
        /// </summary>
        private static readonly Regex KernelNameRegex = new(
            @"__kernel\s+void\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);

        /// <summary>
        /// The OpenCL context used for compiling kernels.
        /// </summary>
        private readonly CLContext Context;

        /// <summary>
        /// The OpenCL device used for compiling kernels.
        /// </summary>
        private readonly CLDevice Device;

        /// <summary>
        /// The OpenClRegister instance used for managing memory objects.
        /// </summary>
        private readonly OpenClRegister Register;

        /// <summary>
        /// The list of compiled OpenCL programs. Each program may contain one or more kernels.
        /// </summary>
        private readonly List<CLProgram> Programs = [];

        /// <summary>
        /// The dictionary of compiled kernels, keyed by kernel name. Each kernel is associated with its corresponding CLKernel object.
        /// </summary>
        private readonly Dictionary<string, CLKernel> ClKernels = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Indicates whether the compiler has been disposed. Once disposed, the compiler cannot be used to compile or retrieve kernels.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Gets the directory the kernel source files were loaded from.
        /// </summary>
        public string KernelDirectory => EnsureKernelDirectory();

        /// <summary>
        /// Gets the names of all successfully compiled kernels.
        /// </summary>
        public IReadOnlyCollection<string> KernelNames => this.ClKernels.Keys;

        /// <summary>
        /// Gets the IRuntimeCompiler interface for this instance.
        /// </summary>
        public IRuntimeCompiler Compiler => this;

        /// <summary>
        /// Gets the list of available kernel source files.
        /// </summary>
        /// <returns>An array of file paths to available kernel source files.</returns>
        public string[] GetSourceFiles() => this.GetClFiles();

        /// <summary>
        /// Gets the list of compiled kernel files.
        /// </summary>
        /// <returns>An array of file paths to compiled kernel files.</returns>
        public string[] GetCompiledFiles() => this.GetClFiles().Select(s => this.ClKernels.ContainsKey(Path.GetFileNameWithoutExtension(s)) ? s : null).Where(s => s != null).Cast<string>().ToArray();

        /// <summary>
        /// Gets the source file of the kernel with the specified name. Returns null if the kernel name is not set or if the file does not exist.
        /// </summary>
        /// <param name="name">The name of the kernel.</param>
        /// <returns>The file path of the kernel source file, or <c>null</c> if not found.</returns>
        public string? GetKernelSourceFile(string name)
        {
            if (string.IsNullOrEmpty(this.KernelName))
            {
                return null;
            }

            if (File.Exists(name) && string.Equals(Path.GetExtension(name), ".cl", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(name);
            }

            string clPath = Path.Combine(this.KernelDirectory, name + ".cl");
            if (File.Exists(clPath))
            {
                return Path.GetFullPath(clPath);
            }
            return null;
        }

        /// <summary>
        /// Gets the source code of the kernel with the specified name.
        /// </summary>
        /// <param name="kernelName">The name of the kernel.</param>
        /// <returns>The source code of the kernel, or <c>null</c> if not found.</returns>
        public string? GetKernelCode(string? kernelName)
        {
            if (string.IsNullOrWhiteSpace(kernelName))
            {
                return null;
            }
            // Check if filePath
            if (File.Exists(kernelName) && string.Equals(Path.GetExtension(kernelName), ".cl", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadAllText(kernelName);
            }

            string filePath = Path.Combine(this.KernelDirectory, kernelName + ".cl");
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            return null;
        }

        /// <summary>
        /// Gets the function name of the specified kernel.
        /// </summary>
        /// <param name="kernel">The name or path of the kernel.</param>
        /// <returns>The function name of the kernel, or <c>null</c> if not found.</returns>
        public string? GetFunctionName(string? kernel)
        {
            kernel ??= this.KernelName;
            if (string.IsNullOrWhiteSpace(kernel))
            {
                return null;
            }

            // If the kernel is a file path, read its content
            if (File.Exists(kernel) && string.Equals(Path.GetExtension(kernel), ".cl", StringComparison.OrdinalIgnoreCase))
            {
                string kernelName = Path.GetFileNameWithoutExtension(kernel);
                if (this.ClKernels.ContainsKey(kernelName))
                {
                    return this.ClKernels.First(k => string.Equals(k.Key, kernelName, StringComparison.OrdinalIgnoreCase)).Key;
                }

                kernel = File.ReadAllText(kernel);
            }

            return this.PrecompileKernel(kernel);
        }

        /// <summary>
        /// Gets the currently loaded kernel as a <see cref="CLKernel"/>. Returns null if no kernel is loaded.
        /// </summary>
        internal CLKernel? Kernel { get; private set; }

        /// <summary>
        /// Gets the name of the currently loaded kernel, always null, since OpenCL does not have CLKernels loaded, they persist, once compiled, in memory.
        /// </summary>
        public string? KernelName { get; private set; }

        /// <summary>
        /// Attempts to load and compile a kernel by name from the kernel directory. Returns true if successful, false otherwise.
        /// </summary>
        /// <param name="kernel">The name or path of the kernel to load.</param>
        /// <returns>True if the kernel was successfully loaded and compiled; otherwise, false.</returns>
        public bool LoadKernel(string kernel)
        {
            if (File.Exists(kernel) && string.Equals(Path.GetExtension(kernel), ".cl", StringComparison.OrdinalIgnoreCase))
            {
                kernel = Path.GetFileNameWithoutExtension(kernel);
            }

            if (this.ClKernels.Keys.Any(k => string.Equals(k, kernel, StringComparison.OrdinalIgnoreCase)))
            {
                this.Kernel = this.ClKernels.First(k => string.Equals(k.Key, kernel, StringComparison.OrdinalIgnoreCase)).Value;
                this.KernelName = this.ClKernels.Keys.First(k => string.Equals(k, kernel, StringComparison.OrdinalIgnoreCase));
                return true;
            }
            else
            {
                string? code = this.GetKernelCode(kernel);
                if (!string.IsNullOrEmpty(code))
                {
                    this.CompileKernel(code);
                }
                else
                {
                    string kernelFile = Path.Combine(this.KernelDirectory, kernel + ".cl");
                    if (!File.Exists(kernelFile))
                    {
                        StaticLogger.LogWarning($"OpenClCompiler: kernel source file '{kernelFile}' not found.");
                        return false;
                    }

                    try
                    {
                        this.CompileFile(kernelFile);
                        return this.ClKernels.ContainsKey(kernel);
                    }
                    catch (Exception ex)
                    {
                        StaticLogger.Log($"OpenClCompiler: failed to load kernel '{kernel}' from '{kernelFile}'.", ex);
                        return false;
                    }
                }
            }

            string filePath = Path.Combine(this.KernelDirectory, kernel + ".cl");
            if (File.Exists(filePath))
            {
                try
                {
                    this.CompileFile(filePath);
                    return this.ClKernels.ContainsKey(kernel);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log($"OpenClCompiler: failed to load kernel '{kernel}' from '{filePath}'.", ex);
                    return false;
                }
            }
            else
            {
                StaticLogger.LogWarning($"OpenClCompiler: kernel source file '{filePath}' not found.");
                return false;
            }
        }

        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClCompiler"/> class and compiles every
        /// kernel source file found in the resolved kernel directory.
        /// </summary>
        /// <param name="context">The OpenCL context to compile for.</param>
        /// <param name="device">The device to build the programs for.</param>
        /// <param name="kernelDirectory">An optional explicit kernel directory; auto-resolved when omitted.</param>
        internal OpenClCompiler(CLContext context, CLDevice device, OpenClRegister register, bool compileAll = true)
        {
            this.Context = context;
            this.Device = device;
            this.Register = register;

            if (compileAll)
            {
                this.CompileAll();
            }
        }



        // Directory resolution
        /// <summary>
        /// Resolves the directory that holds the <c>*.cl</c> kernel sources, preferring a "Kernels" folder
        /// next to the running assembly and walking up to the project folder when necessary.
        /// </summary>
        private static string EnsureKernelDirectory()
        {
            string baseDir = AppContext.BaseDirectory;
            string candidate = Path.Combine(baseDir, "Kernels");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? current = new(baseDir);
            for (int i = 0; i < 10 && current != null; i++)
            {
                string projectKernels = Path.Combine(current.FullName, "AsynCUDA13.OpenCl", "Kernels");
                if (Directory.Exists(projectKernels))
                {
                    return projectKernels;
                }

                string localKernels = Path.Combine(current.FullName, "Kernels");
                if (Directory.Exists(localKernels))
                {
                    return localKernels;
                }

                current = current.Parent;
            }

            return candidate;
        }

        /// <summary>
        /// Returns the full paths of all <c>*.cl</c> files in the kernel directory.
        /// </summary>
        public string[] GetClFiles(bool recursive = true)
        {
            if (!Directory.Exists(this.KernelDirectory))
            {
                return [];
            }

            return Directory.GetFiles(this.KernelDirectory, "*.cl", enumerationOptions: new EnumerationOptions { RecurseSubdirectories = recursive });
        }



        // Compilation
        /// <summary>
        /// Compiles every kernel source file found in <see cref="KernelDirectory"/> and registers their kernels.
        /// </summary>
        private void CompileAll()
        {
            string[] files = this.GetClFiles();
            if (files.Length == 0)
            {
                StaticLogger.LogWarning($"OpenClCompiler: no .cl files found in '{this.KernelDirectory}'.");
                return;
            }

            foreach (string file in files)
            {
                try
                {
                    this.CompileFile(file);
                }
                catch (Exception ex)
                {
                    StaticLogger.Log($"OpenClCompiler: failed to compile '{Path.GetFileName(file)}'", ex);
                }
            }

            StaticLogger.LogSuccess($"OpenClCompiler: compiled {this.ClKernels.Count} kernel(s) from {files.Length} file(s).");
        }

        /// <summary>
        /// Compiles a float kernel source file into a program and registers each <c>__kernel</c> it defines.
        /// </summary>
        /// <param name="file">The full path of the <c>*.cl</c> file to compile.</param>
        private void CompileFile(string file)
        {
            string source = ReadAllTextWithRetry(file);
            if (string.IsNullOrWhiteSpace(source))
            {
                StaticLogger.LogWarning($"OpenClCompiler: '{Path.GetFileName(file)}' is empty, skipped.");
                return;
            }

            CLProgram program = CL.CreateProgramWithSource(this.Context, source, out CLResultCode createCode);
            if (createCode != CLResultCode.Success)
            {
                StaticLogger.LogError($"OpenClCompiler: CreateProgramWithSource failed for '{Path.GetFileName(file)}' ({createCode}).");
                return;
            }

            // Use the callback-free BuildProgram overload (numDevices, devices, options, userData, callback).
            // OpenTK 4.9.x's delegate-based overload unconditionally marshals the callback delegate and throws
            // ArgumentNullException ("d") for a null callback; passing IntPtr.Zero performs a blocking build
            // without any notification callback, which is exactly what an in-memory compiler needs.
            CLResultCode buildCode = CL.BuildProgram(program, 1, [this.Device], string.Empty, IntPtr.Zero, IntPtr.Zero);
            if (buildCode != CLResultCode.Success)
            {
                string log = this.GetBuildLog(program);
                StaticLogger.LogError($"OpenClCompiler: build failed for '{Path.GetFileName(file)}' ({buildCode}). Build log: {log}");
                CL.ReleaseProgram(program);
                return;
            }

            this.Programs.Add(program);

            foreach (string kernelName in ExtractKernelNames(source))
            {
                CLKernel kernel = CL.CreateKernel(program, kernelName, out CLResultCode kernelCode);
                if (kernelCode != CLResultCode.Success)
                {
                    StaticLogger.LogError($"OpenClCompiler: CreateKernel '{kernelName}' failed ({kernelCode}).");
                    continue;
                }

                this.ClKernels[kernelName] = kernel;
            }
        }

        /// <summary>
        /// Compiles a kernel source string into a program and registers each <c>__kernel</c> it defines, returning the names of the compiled kernels.
        /// </summary>
        /// <param name="kernelCode">The source code of the kernel to compile.</param>
        /// <returns>A comma-separated list of the names of the compiled kernels.</returns>
        /// <exception cref="ArgumentException">Thrown if the kernel code is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the compilation or kernel creation fails.</exception>
        public string CompileKernel(string kernelCode)
        {
            if (string.IsNullOrWhiteSpace(kernelCode))
            {
                throw new ArgumentException("Kernel code cannot be null or whitespace.", nameof(kernelCode));
            }
            CLProgram program = CL.CreateProgramWithSource(this.Context, kernelCode, out CLResultCode createCode);
            if (createCode != CLResultCode.Success)
            {
                throw new InvalidOperationException($"CreateProgramWithSource failed ({createCode}).");
            }
            CLResultCode buildCode = CL.BuildProgram(program, 1, [this.Device], string.Empty, IntPtr.Zero, IntPtr.Zero);
            if (buildCode != CLResultCode.Success)
            {
                string log = this.GetBuildLog(program);
                CL.ReleaseProgram(program);
                throw new InvalidOperationException($"BuildProgram failed ({buildCode}). Build log: {log}");
            }
            this.Programs.Add(program);
            foreach (string kernelName in ExtractKernelNames(kernelCode))
            {
                CLKernel kernel = CL.CreateKernel(program, kernelName, out CLResultCode resultCode);
                if (resultCode != CLResultCode.Success)
                {
                    throw new InvalidOperationException($"CreateKernel '{kernelName}' failed ({kernelCode}).");
                }
                this.ClKernels[kernelName] = kernel;
            }
            return string.Join(", ", ExtractKernelNames(kernelCode));
        }

        /// <summary>
        /// Precompiles an OpenCL kernel string by checking for required keywords and balanced brackets, returning the kernel name if valid.
        /// </summary>
        /// <param name="code">The source code of the OpenCL kernel to precompile.</param>
        /// <returns>The name of the kernel if the precompilation was successful; otherwise <c>null</c>.</returns>
        public string? PrecompileKernel(string code)
        {
            bool silent = false;

            // Check contains "__kernel" or "kernel"
            if (!code.Contains("__kernel") && !code.Contains("kernel "))
            {
                if (!silent)
                {
                    StaticLogger.Log("Kernel string does not contain '__kernel' or 'kernel'");
                }
                return null;
            }

            // Check contains "void "
            if (!code.Contains("void "))
            {
                if (!silent)
                {
                    StaticLogger.Log("Kernel string does not contain 'void '");
                }
                return null;
            }

            // Check contains int, long, or size_t (typical index types in OpenCL)
            if (!code.Contains("int ") && !code.Contains("long ") && !code.Contains("size_t "))
            {
                if (!silent)
                {
                    StaticLogger.Log("Kernel string does not contain 'int ', 'long ' or 'size_t ' (for array length/indexing)");
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
                    StaticLogger.Log("Kernel string has unbalanced brackets { }");
                }
                return null;
            }

            open = code.Count(c => c == '(');
            close = code.Count(c => c == ')');
            if (open != close)
            {
                if (!silent)
                {
                    StaticLogger.Log("Kernel string has unbalanced brackets ( )");
                }
                return null;
            }

            open = code.Count(c => c == '[');
            close = code.Count(c => c == ']');
            if (open != close)
            {
                if (!silent)
                {
                    StaticLogger.Log("Kernel string has unbalanced brackets [ ]");
                }
                return null;
            }

            // Check if kernel contains OpenCL thread indexing functions
            if (!code.Contains("get_global_id") && !code.Contains("get_local_id"))
            {
                if (!silent)
                {
                    StaticLogger.Log("Kernel string should contain 'get_global_id' or 'get_local_id'");
                }
            }

            // Get name between "void " and "("
            int start = code.IndexOf("void ") + "void ".Length;
            int end = code.IndexOf("(", start);

            if (start < 0 || end < 0 || end <= start)
            {
                if (!silent)
                {
                    StaticLogger.Log("Could not parse kernel function name");
                }
                return null;
            }

            string name = code.Substring(start, end - start).Trim();

            // Trim line ends from empty spaces
            code = string.Join("\n", code.Split('\n').Select(x => x.TrimEnd()));

            // Log name
            if (!silent)
            {
                StaticLogger.Log($"Successfully precompiled OpenCL kernel string '{name}'");
            }

            return name;
        }


        /// <summary>
        /// Reads the program build log for diagnostics.
        /// </summary>
        private string GetBuildLog(CLProgram program)
        {
            try
            {
                if (CL.GetProgramBuildInfo(program, this.Device, ProgramBuildInfo.Log, out Byte[] bytes) == CLResultCode.Success && bytes != null)
                {
                    int length = bytes.Length;
                    if (length > 0 && bytes[length - 1] == 0)
                    {
                        length--;
                    }

                    return Encoding.ASCII.GetString(bytes, 0, length).Trim();
                }
            }
            catch
            {
            }

            return "(unavailable)";
        }

        /// <summary>
        /// Extracts the names of every <c>__kernel void</c> entry point in the given source.
        /// </summary>
        private static IEnumerable<string> ExtractKernelNames(string source)
        {
            HashSet<string> seen = [];
            foreach (Match match in KernelNameRegex.Matches(source))
            {
                string name = match.Groups["name"].Value;
                if (seen.Add(name))
                {
                    yield return name;
                }
            }
        }

        /// <summary>
        /// Reads a file with a few retries to tolerate transient sharing violations during builds.
        /// </summary>
        private static string ReadAllTextWithRetry(string path, int attempts = 3)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException) when (i < attempts - 1)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }

            return File.ReadAllText(path);
        }



        // Access
        /// <summary>
        /// Gets a compiled kernel by name or file path, returning it as a <see cref="CLKernel"/>.
        /// </summary>
        /// <param name="kernel">The kernel entry-point name or file path.</param>
        /// <returns>The compiled <see cref="CLKernel"/>, or <c>null</c> when no kernel with that name exists.</returns>
        internal CLKernel? GetClKernel(string? kernel)
        {
            if (string.IsNullOrEmpty(kernel))
            {
                return this.Kernel;
            }

            if (File.Exists(kernel))
            {
                kernel = Path.GetFileNameWithoutExtension(kernel);
            }

            if (this.ClKernels.TryGetValue(kernel, out CLKernel k))
            {
                return k;
            }

            StaticLogger.LogError($"OpenClCompiler: kernel '{kernel}' not found.");
            return null;
        }

        /// <summary>
        /// Gets the compiled program that contains the specified kernel name, optionally filtering by a specific kernel instance.
        /// </summary>
        /// <param name="name">The name of the kernel.</param>
        /// <param name="kernel">The specific kernel instance to filter by.</param>
        /// <returns>The compiled <see cref="CLProgram"/>, or <c>null</c> when no program with that kernel name exists.</returns>
        internal CLProgram? GetClProgram(string? name, CLKernel? kernel = null)
        {
            if (kernel.HasValue)
            {
                foreach (CLProgram program in this.Programs)
                {
                    if (CL.GetProgramInfo(program, ProgramInfo.KernelNames, out byte[] kernelNames) == CLResultCode.Success)
                    {
                        string[] names = Encoding.ASCII.GetString(kernelNames).Split(';', StringSplitOptions.RemoveEmptyEntries);
                        if (Array.Exists(names, k => k == name))
                        {
                            return program;
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(name))
            {
                foreach (CLProgram program in this.Programs)
                {
                    if (CL.GetProgramInfo(program, ProgramInfo.KernelNames, out byte[] kernelNames) == CLResultCode.Success)
                    {
                        string[] names = Encoding.ASCII.GetString(kernelNames).Split(';', StringSplitOptions.RemoveEmptyEntries);
                        if (Array.Exists(names, k => k == name))
                        {
                            return program;
                        }
                    }
                }
            }

            StaticLogger.LogError($"OpenClCompiler: program for kernel '{name}' not found.");
            return null;
        }

        /// <summary>
        /// Gets a compiled kernel by name, returning it as an object to satisfy the IRuntimeCompiler interface.
        /// </summary>
        /// <param name="name">The name of the kernel.</param>
        /// <returns>The compiled kernel as an object, or <c>null</c> if not found.</returns>
        public object? GetKernel(string name) => this.GetClKernel(name);

        /// <summary>
        /// Determines whether a kernel with the given name has been compiled.
        /// </summary>
        public bool HasKernel(string name)
        {
            return this.ClKernels.ContainsKey(name);
        }

        /// <summary>
        /// Gets the arguments of the kernel with the specified name, mapping OpenCL types to C# types.
        /// </summary>
        /// <param name="kernel">The name, file path, or source code of the kernel.</param>
        /// <returns>A dictionary mapping argument names to their corresponding C# types.</returns>
        public Dictionary<string, Type> GetArguments(string? kernel)
        {
            var arguments = new Dictionary<string, Type>(StringComparer.Ordinal);
            kernel ??= this.KernelName;
            if (string.IsNullOrWhiteSpace(kernel) && this.Kernel == null)
            {
                StaticLogger.LogWarning("OpenClCompiler: GetArguments called with null or empty kernel name, returning empty arguments dictionary.");
                return arguments;
            }

            CLKernel? clK = this.GetClKernel(kernel) ?? this.Kernel;
            if (!clK.HasValue)
            {
                return arguments;
            }

            string[] argNames = ExtractArgumentNames(this.GetKernelCode(kernel));
            string[] argTypes = ExtractArgumentTypes(this.GetKernelCode(kernel));

            int argCount = CL.GetKernelInfo(clK.Value, KernelInfo.NumberOfArguments, out byte[] count) == CLResultCode.Success ? BitConverter.ToInt32(count, 0) : 0;
            for (int i = 0; i < argCount; i++)
            {
                string argName = CL.GetKernelArgInfo(clK.Value, (uint)i, KernelArgInfo.Name, out byte[] nameBytes) == CLResultCode.Success
                    ? Encoding.ASCII.GetString(nameBytes).TrimEnd('\0')
                    : argNames[i];
                string argTypeStr = CL.GetKernelArgInfo(clK.Value, (uint)i, KernelArgInfo.TypeName, out byte[] typeBytes) == CLResultCode.Success
                    ? Encoding.ASCII.GetString(typeBytes).TrimEnd('\0')
                    : argTypes[i];
                Type argType = MapOpenClTypeToCSharp(argTypeStr);
                arguments[argName] = argType;
            }

            return arguments;
        }

        /// <summary>
        /// Unloads the specified kernel, releasing its resources.
        /// </summary>
        /// <param name="name">The name of the kernel to unload.</param>
        public void UnloadKernel(string? name)
        {
            if (this._disposed)
            {
                return;
            }

            var clKernel = this.GetClKernel(name ?? string.Empty);
            if (clKernel != null)
            {
                try
                {
                    CL.ReleaseKernel(clKernel.Value);
                }
                catch
                {
                }
                this.ClKernels.Remove(name ?? string.Empty);

                var clProgram = this.GetClProgram(name, clKernel);
                if (clProgram != null)
                {
                    try
                    {
                        CL.ReleaseProgram(clProgram.Value);
                    }
                    catch
                    {
                    }
                    this.Programs.Remove(clProgram.Value);
                }
            }
        }

        // Disposal
        /// <summary>
        /// Releases all compiled kernels and programs.
        /// </summary>
        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            foreach (CLKernel kernel in this.ClKernels.Values)
            {
                try
                {
                    CL.ReleaseKernel(kernel);
                }
                catch
                {
                }
            }

            this.ClKernels.Clear();

            foreach (CLProgram program in this.Programs)
            {
                try
                {
                    CL.ReleaseProgram(program);
                }
                catch
                {
                }
            }

            this.Programs.Clear();
            this._disposed = true;
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
                StaticLogger.LogError("OpenClCompiler: MergeArgumentsImage called with no kernel loaded, returning empty arguments array.");
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
            nint expectedLen = checked((nint)((long)width * height * channels));
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
                    StaticLogger.Log($"In-pointer: <{inPtr}>");
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
                    StaticLogger.Log($"Out-pointer: <{outPtr}>");
                }
                else if (name.Contains("width") && type == typeof(int))
                {
                    kernelArgs[i] = width;

                    StaticLogger.Log($"Width: {name}=[{width}]");
                }
                else if (name.Contains("height") && type == typeof(int))
                {
                    kernelArgs[i] = height;

                    StaticLogger.Log($"Height: {name}=[{height}]");
                }
                else if (name.Contains("chan") && type == typeof(int))
                {
                    kernelArgs[i] = channels;
                    StaticLogger.Log($"Channels: {name}=[{channels}]");
                }
                else if (name.Contains("bit") && type == typeof(int))
                {
                    kernelArgs[i] = bitdepth;
                    StaticLogger.Log($"Bits: {name}=[{bitdepth}]");
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
                    StaticLogger.Log($"{additionalArgs.Length - userArgIndex} unused user arguments for kernel '{kernel}': {string.Join(", ", additionalArgs.Skip(userArgIndex))}");
                }
            }

            // DEBUG LOG
            //StaticLogger.Log("Kernel arguments: " + string.Join(", ", kernelArgs.Select(x => x.ToString())), "", 1);

            // Return kernel arguments
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
                    StaticLogger.Log($"In-pointer: <{inputPointer}>");
                }
                else if (pointersCount == 1 && type == typeof(IntPtr))
                {
                    kernelArgs[i] = outputPointer;
                    pointersCount++;
                    StaticLogger.Log($"Out-pointer: <{outputPointer}>");
                }
                else if (name.Contains("sample") && type == typeof(int))
                {
                    StaticLogger.Log($"SampleRate: [{sampleRate}]");
                }
                else if (name.Contains("chan") && type == typeof(int))
                {
                    kernelArgs[i] = channels;
                    StaticLogger.Log($"Channels: [{channels}]");
                }
                else if (name.Contains("bit") && type == typeof(int))
                {
                    kernelArgs[i] = bitdepth;
                    StaticLogger.Log($"Bits: [{bitdepth}]");
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
                                    StaticLogger.Log($"Named argument: {name} = {value}");
                                    break;
                                }
                                else
                                {
                                    StaticLogger.Log($"Named argument '{name}' not found in provided arguments");
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



        // Static helpers
        public static Type MapOpenClTypeToCSharp(string openClType)
        {
            if (string.IsNullOrWhiteSpace(openClType))
            {
                return typeof(object);
            }

            bool isPointer = openClType.Contains('*');
            string typeLower = openClType.ToLowerInvariant();

            Type baseType = typeLower switch
            {
                var t when t.Contains("uchar") => typeof(byte),
                var t when t.Contains("sbyte") || (t.Contains("char") && !t.Contains("uchar")) => typeof(sbyte),
                var t when t.Contains("ushort") => typeof(ushort),
                var t when t.Contains("short") => typeof(short),
                var t when t.Contains("uint") => typeof(uint),
                var t when t.Contains("int") => typeof(int),
                var t when t.Contains("ulong") => typeof(ulong),
                var t when t.Contains("long") => typeof(long),
                var t when t.Contains("float") => typeof(float),
                var t when t.Contains("double") => typeof(double),
                _ => typeof(object),
            };

            return isPointer ? baseType.MakePointerType() : baseType;
        }

        public static bool IsNullPointer(IntPtr? pointer)
        {
            return !pointer.HasValue || pointer.Value == IntPtr.Zero;
        }

        public static string[] ExtractArgumentNames(string? sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                return [];
            }
            // Sucht nach Mustern wie: kernel void myKernel(const float* input, float* output)
            // Dies ist eine vereinfachte Regex; echte C-Parser sind komplexer
            var match = Regex.Match(sourceCode, @"kernel\s+void\s+\w+\s*\((.*?)\)", RegexOptions.Singleline);
            if (!match.Success)
            {
                return [];
            }

            var argsString = match.Groups[1].Value;
            var args = argsString.Split(',');

            return args.Select(a => {
                // Entferne Typen, Qualifier (const, restrict) und das Komma
                // Nimmt das letzte Wort vor dem Komma/Klammer als Namen
                var parts = a.Trim().Split(' ');
                return parts.Last().TrimEnd('*').Trim();
            }).ToArray();
        }

        public static string[] ExtractArgumentTypes(string? sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                return [];
            }
            // Sucht nach Mustern wie: kernel void myKernel(const float* input, float* output)
            var match = Regex.Match(sourceCode, @"kernel\s+void\s+\w+\s*\((.*?)\)", RegexOptions.Singleline);
            if (!match.Success)
            {
                return [];
            }
            var argsString = match.Groups[1].Value;
            var args = argsString.Split(',');
            return args.Select(a =>
            {
                // Entferne Qualifier (const, restrict) und das Komma
                var parts = a.Trim().Split(' ');
                return string.Join(" ", parts.Take(parts.Length - 1)).Trim();
            }).ToArray();
        }
    }
}
