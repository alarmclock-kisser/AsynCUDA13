using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Compute.OpenCL;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Discovers OpenCL kernel source files (<c>*.cl</c>), compiles them in memory for a float device and
    /// exposes the compiled kernels by name. No binaries are written to disk; the programs and kernels live
    /// only in memory for the lifetime of this compiler.
    /// </summary>
    internal sealed class OpenClCompiler : IRuntimeCompiler, IDisposable
    {
        private static readonly Regex KernelNameRegex = new(
            @"__kernel\s+void\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);

        private readonly CLContext _context;
        private readonly CLDevice _device;
        private readonly List<CLProgram> _programs = [];
        private readonly Dictionary<string, CLKernel> _kernels = new(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>
        /// Gets the directory the kernel source files were loaded from.
        /// </summary>
        public string KernelDirectory { get; }

        /// <summary>
        /// Gets the names of all successfully compiled kernels.
        /// </summary>
        public IReadOnlyCollection<string> KernelNames => this._kernels.Keys;

        /// <summary>
        /// Gets the IRuntimeCompiler interface for this instance.
        /// </summary>
        public IRuntimeCompiler Compiler => this;


        public string[] GetSourceFiles() => this.GetClFiles();
        public string? KernelName => null;

        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClCompiler"/> class and compiles every
        /// kernel source file found in the resolved kernel directory.
        /// </summary>
        /// <param name="context">The OpenCL context to compile for.</param>
        /// <param name="device">The device to build the programs for.</param>
        /// <param name="kernelDirectory">An optional explicit kernel directory; auto-resolved when omitted.</param>
        internal OpenClCompiler(CLContext context, CLDevice device, string? kernelDirectory = null)
        {
            this._context = context;
            this._device = device;
            this.KernelDirectory = string.IsNullOrWhiteSpace(kernelDirectory)
                ? EnsureKernelDirectory()
                : kernelDirectory;

            this.CompileAll();
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
        public string[] GetClFiles()
        {
            if (!Directory.Exists(this.KernelDirectory))
            {
                return [];
            }

            return Directory.GetFiles(this.KernelDirectory, "*.cl");
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

            StaticLogger.LogSuccess($"OpenClCompiler: compiled {this._kernels.Count} kernel(s) from {files.Length} file(s).");
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

            CLProgram program = CL.CreateProgramWithSource(this._context, source, out CLResultCode createCode);
            if (createCode != CLResultCode.Success)
            {
                StaticLogger.LogError($"OpenClCompiler: CreateProgramWithSource failed for '{Path.GetFileName(file)}' ({createCode}).");
                return;
            }

            // Use the callback-free BuildProgram overload (numDevices, devices, options, userData, callback).
            // OpenTK 4.9.x's delegate-based overload unconditionally marshals the callback delegate and throws
            // ArgumentNullException ("d") for a null callback; passing IntPtr.Zero performs a blocking build
            // without any notification callback, which is exactly what an in-memory compiler needs.
            CLResultCode buildCode = CL.BuildProgram(program, 1, [this._device], string.Empty, IntPtr.Zero, IntPtr.Zero);
            if (buildCode != CLResultCode.Success)
            {
                string log = this.GetBuildLog(program);
                StaticLogger.LogError($"OpenClCompiler: build failed for '{Path.GetFileName(file)}' ({buildCode}). Build log: {log}");
                CL.ReleaseProgram(program);
                return;
            }

            this._programs.Add(program);

            foreach (string kernelName in ExtractKernelNames(source))
            {
                CLKernel kernel = CL.CreateKernel(program, kernelName, out CLResultCode kernelCode);
                if (kernelCode != CLResultCode.Success)
                {
                    StaticLogger.LogError($"OpenClCompiler: CreateKernel '{kernelName}' failed ({kernelCode}).");
                    continue;
                }

                this._kernels[kernelName] = kernel;
            }
        }

        /// <summary>
        /// Reads the program build log for diagnostics.
        /// </summary>
        private string GetBuildLog(CLProgram program)
        {
            try
            {
                if (CL.GetProgramBuildInfo(program, this._device, ProgramBuildInfo.Log, out Byte[] bytes) == CLResultCode.Success && bytes != null)
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
        /// Gets a compiled kernel by name.
        /// </summary>
        /// <param name="name">The kernel entry-point name.</param>
        /// <returns>The compiled <see cref="CLKernel"/>, or <c>null</c> when no kernel with that name exists.</returns>
        internal CLKernel? GetClKernel(string name)
        {
            if (this._kernels.TryGetValue(name, out CLKernel kernel))
            {
                return kernel;
            }

            StaticLogger.LogError($"OpenClCompiler: kernel '{name}' not found.");
            return null;
        }

        internal CLProgram? GetClProgram(string? name, CLKernel? kernel = null)
        {
            if (kernel.HasValue)
            {
                foreach (CLProgram program in this._programs)
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
                foreach (CLProgram program in this._programs)
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

        public object? GetKernel(string name) => this.GetClKernel(name);

        /// <summary>
        /// Determines whether a kernel with the given name has been compiled.
        /// </summary>
        public bool HasKernel(string name)
        {
            return this._kernels.ContainsKey(name);
        }


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
                this._kernels.Remove(name ?? string.Empty);

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
                    this._programs.Remove(clProgram.Value);
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

            foreach (CLKernel kernel in this._kernels.Values)
            {
                try
                {
                    CL.ReleaseKernel(kernel);
                }
                catch
                {
                }
            }

            this._kernels.Clear();

            foreach (CLProgram program in this._programs)
            {
                try
                {
                    CL.ReleaseProgram(program);
                }
                catch
                {
                }
            }

            this._programs.Clear();
            this._disposed = true;
        }
    }
}
