using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AsynCUDA13.Shared
{
    /// <summary>
    /// Checks the configured Windows environment paths for a CUDA runtime.
    /// </summary>
    public static class CudaAvailabilityTester
    {
        public static Boolean IsCudaAvailable() => GetCudaRuntimeDirectories().Any();

        public static IReadOnlyList<string> GetCudaRuntimeDirectories()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string? pathValue;
                try
                {
                    pathValue = Environment.GetEnvironmentVariable("Path", target);
                }
                catch (System.Security.SecurityException)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    continue;
                }

                foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!entry.Contains("CUDA", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(entry))
                    {
                        continue;
                    }

                    try
                    {
                        if (Directory.EnumerateFiles(entry, "cudart64_*.dll", SearchOption.AllDirectories).Any())
                        {
                            result.Add(Path.GetFullPath(entry));
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return result.ToArray();
        }

    }
}
