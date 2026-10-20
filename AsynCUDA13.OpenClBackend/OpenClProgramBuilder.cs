using System.Text;
using AsynCUDA13.Shared.Interfaces;
using OpenTK.Compute.OpenCL;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Records blocking OpenCL builds. Delegates allow diagnostics to be tested without loading an ICD.
    /// A fatal native compiler exit cannot be intercepted: only the pre-build record then survives.
    /// </summary>
    internal sealed class OpenClProgramBuilder(IRollingFileMemoryLogger logger)
    {
        private bool _diagnosticPathAnnounced;

        internal void Build(string sourceName, string device, string options, Func<CLResultCode> build,
            Func<ProgramBuildInfo, (CLResultCode Code, byte[]? Bytes)> getBuildInfo)
        {
            string context = $"Source='{sourceName}'; Device=[{device}]; Requested options='{options}'";
            string start = $"[BUILD START] {context}";
            this.WriteDiagnostic(start);
            logger.LogInfo($"OpenClCompiler: {start}");

            CLResultCode result = this.ExecuteBuild(build, context);
            // Query even on success: compilers can return warnings with CL_SUCCESS.
            string status = ReadBuildInfo(getBuildInfo, ProgramBuildInfo.Status, out bool statusFailed);
            string reportedOptions = ReadBuildInfo(getBuildInfo, ProgramBuildInfo.Options, out bool optionsFailed);
            string buildLog = ReadBuildInfo(getBuildInfo, ProgramBuildInfo.Log, out bool logFailed);
            string diagnostics = $"{context}; Driver options='{reportedOptions}'; Build status={status}; Build log:{Environment.NewLine}{buildLog}";
            this.WriteDiagnostic($"[BUILD END] Result={result} ({(int)result}); {diagnostics}");

            if (result != CLResultCode.Success)
            {
                string message = $"OpenClCompiler: BuildProgram failed ({result}, {(int)result}). {diagnostics}";
                logger.LogError(message);
                throw new InvalidOperationException(message);
            }
            if (buildLog != "<empty>" || statusFailed || optionsFailed || logFailed)
            {
                logger.LogWarning($"OpenClCompiler: BuildProgram succeeded with diagnostics. {diagnostics}");
            }
        }

        private CLResultCode ExecuteBuild(Func<CLResultCode> build, string context)
        {
            try
            {
                return build();
            }
            catch (Exception ex)
            {
                string message = $"[BUILD EXCEPTION] {context}; {ex.GetType().Name}: {ex.Message}";
                this.WriteDiagnostic(message);
                logger.LogError($"OpenClCompiler: {message}");
                throw;
            }
        }

        private static string ReadBuildInfo(Func<ProgramBuildInfo, (CLResultCode Code, byte[]? Bytes)> query, ProgramBuildInfo field, out bool failed)
        {
            failed = false;
            try
            {
                var (code, bytes) = query(field);
                if (code != CLResultCode.Success || bytes == null)
                {
                    failed = true;
                    return $"<clGetProgramBuildInfo({field}) unavailable: {code} ({(int)code}); data={(bytes == null ? "null" : "present")}>";
                }
                if (field == ProgramBuildInfo.Status)
                {
                    failed = bytes.Length < sizeof(int);
                    return failed ? $"<invalid build status length: {bytes.Length}>"
                        : BitConverter.ToInt32(bytes).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                string text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                return string.IsNullOrWhiteSpace(text) ? "<empty>" : text;
            }
            catch (Exception ex)
            {
                failed = true;
                return $"<clGetProgramBuildInfo({field}) threw {ex.GetType().Name}: {ex.Message}>";
            }
        }

        private void WriteDiagnostic(string message)
        {
            // Do not compete with the logger's asynchronous writer for the main log file.
            // This sidecar is flushed before entering native code, independent of shutdown handlers.
            if (logger.Settings.Silent || logger.LogFilePath == null)
            {
                return;
            }
            try
            {
                string path = Path.ChangeExtension(Path.GetFullPath(logger.LogFilePath), ".opencl-build.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    writer.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                if (!this._diagnosticPathAnnounced)
                {
                    this._diagnosticPathAnnounced = true;
                    logger.LogWarning($"OpenClCompiler: synchronous build diagnostics: '{path}'. A fatal native compiler exit prevents BuildProgram from returning a build log; an unmatched BUILD START identifies the interrupted build.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                logger.LogWarning($"OpenClCompiler: cannot persist build diagnostics: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
