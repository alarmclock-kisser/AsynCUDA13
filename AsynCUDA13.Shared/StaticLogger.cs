using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace AsynCUDA13.Shared
{
    public static class StaticLogger
    {
        public static readonly ConcurrentDictionary<DateTime, string> LogEntries = new();
        public static readonly BindingList<string> LogEntriesBindingList = [];
        public static readonly BindingList<string> NativeRuntimeLogEntriesBindingList = [];

        /// <summary>
        /// Raised whenever a new line has been recorded. The first argument is the timestamp the entry was
        /// recorded at; the second argument is the fully formatted line (including the timestamp prefix).
        /// Subscribers must tolerate being invoked from arbitrary threads.
        /// </summary>
        public static event Action<DateTime, string>? LogWritten;

        /// <summary>
        /// Gets or sets a value indicating whether log lines are echoed to the console. When enabled, only
        /// success, error and warning lines are printed (per the project's CLI logging guideline).
        /// </summary>
        public static bool EchoToConsole { get; set; } = true;

        public static string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        public static string? LogFilePath { get; private set; } = null;

        // UI synchronization context (set from the UI at startup)
        private static SynchronizationContext? UiContext;

        public static void SetUiContext(SynchronizationContext context)
        {
            UiContext = context;
            Log("[Logger] StaticLogger UI context set");
        }

        public static void InitializeLogFiles(string? logDirectory = null, bool createLogFile = false, int maxPreviousLogFiles = 3)
        {
            if (!string.IsNullOrEmpty(logDirectory))
            {
                LogDirectory = logDirectory;
            }

            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                if (maxPreviousLogFiles == 0)
                {
                    // Clear all previous logs if exaclty 0 is specified
                    Directory.Delete(LogDirectory, true);
                    Directory.CreateDirectory(LogDirectory);
                }
                else if (maxPreviousLogFiles >= 1)
                {
                    var existingLogs = Directory.GetFiles(LogDirectory, "log_*.txt")
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(fi => fi.CreationTime)
                        .ToList();
                    // Keep only the most recent 'maxPreviousLogFiles' logs
                    foreach (var oldLog in existingLogs.Skip(maxPreviousLogFiles))
                    {
                        try
                        {
                            oldLog.Delete();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error deleting old log file '{oldLog.FullName}': {ex.Message}");
                        }
                    }
                }

                if (createLogFile)
                {
                    LogFilePath = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.Create(LogFilePath).Dispose();
                    Log($"Log file created at {LogFilePath}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error with log files initialization: {ex.Message}");
            }
        }


        public static void Log(string message)
        {
            DateTime timestamp = DateTime.Now;
            string logEntry = $"[{timestamp:HH:mm:ss.fff}] {message}";
            LogEntries[timestamp] = logEntry;

            if (!logEntry.Contains("[Native", StringComparison.OrdinalIgnoreCase))
            {
                if (UiContext != null)
                {
                    UiContext.Post(_ => LogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    // Fallback: add on current thread
                    lock (LogEntriesBindingList)
                    {
                        LogEntriesBindingList.Add(logEntry);
                    }
                }
            }
            else
            {
                if (UiContext != null)
                {
                    UiContext.Post(_ => NativeRuntimeLogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    // Fallback: add on current thread
                    lock (NativeRuntimeLogEntriesBindingList)
                    {
                        NativeRuntimeLogEntriesBindingList.Add(logEntry);
                    }
                }
            }

            RaiseLogWritten(timestamp, logEntry);

            if (EchoToConsole && ShouldEchoToConsole(logEntry))
            {
                Console.WriteLine(logEntry);
            }

            if (LogFilePath != null)
            {
                try
                {
                    File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Determines whether a formatted line should be echoed to the console. Per the project's CLI
        /// logging guideline, only success, error and warning lines are printed.
        /// </summary>
        private static bool ShouldEchoToConsole(string logEntry)
        {
            return logEntry.Contains("[SUCCESS]", StringComparison.OrdinalIgnoreCase)
                || logEntry.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
                || logEntry.Contains("[WARN", StringComparison.OrdinalIgnoreCase)
                || logEntry.Contains("Exception:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Safely raises <see cref="LogWritten"/>, swallowing any subscriber exception so logging never fails.
        /// </summary>
        private static void RaiseLogWritten(DateTime timestamp, string line)
        {
            try
            {
                LogWritten?.Invoke(timestamp, line);
            }
            catch
            {
            }
        }

        public static void Log(Exception ex, string? preText = null)
        {
            if (!string.IsNullOrEmpty(preText))
            {
                Log($"{preText}\nException: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }
            else
            {
                Log($"Exception: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Logs a contextual message together with an exception.
        /// </summary>
        /// <param name="message">The contextual message.</param>
        /// <param name="ex">The exception to append.</param>
        public static void Log(string message, Exception ex)
        {
            Log($"{message}\nException: {ex.Message}\nStack Trace: {ex.StackTrace}");
        }

        /// <summary>Logs an informational message.</summary>
        /// <param name="message">The message to log.</param>
        public static void LogInfo(string message) => Log($"[INFO] {message}");

        /// <summary>Logs a success message (echoed to the console).</summary>
        /// <param name="message">The message to log.</param>
        public static void LogSuccess(string message) => Log($"[SUCCESS] {message}");

        /// <summary>Logs a warning message (echoed to the console).</summary>
        /// <param name="message">The message to log.</param>
        public static void LogWarning(string message) => Log($"[WARN] {message}");

        /// <summary>Logs an error message (echoed to the console).</summary>
        /// <param name="message">The message to log.</param>
        public static void LogError(string message) => Log($"[ERROR] {message}");

        public static async Task LogAsync(string message, bool configureAwait = false)
        {
            await Task.Run(() => Log(message)).ConfigureAwait(configureAwait);
        }

        public static async Task LogAsync(Exception ex, string? preText = null, bool configureAwait = false)
        {
            await Task.Run(() => Log(ex, preText)).ConfigureAwait(configureAwait);
        }

        /// <summary>
        /// Records a user/debugging comment anchored to the timestamp captured when the user initiated it
        /// (for example when a "comment now" button was pressed), so it lands in the log at the right moment.
        /// </summary>
        /// <param name="capturedAt">The timestamp captured at the moment the comment was initiated.</param>
        /// <param name="comment">The free-form comment text.</param>
        public static void AddComment(DateTime capturedAt, string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return;
            }

            string logEntry = $"[{capturedAt:HH:mm:ss.fff}] [COMMENT] {comment}";
            LogEntries[capturedAt] = logEntry;

            if (UiContext != null)
            {
                UiContext.Post(_ => LogEntriesBindingList.Add(logEntry), null);
            }
            else
            {
                lock (LogEntriesBindingList)
                {
                    LogEntriesBindingList.Add(logEntry);
                }
            }

            RaiseLogWritten(capturedAt, logEntry);

            if (LogFilePath != null)
            {
                try
                {
                    File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Returns a chronological snapshot of all recorded log lines (oldest first).
        /// </summary>
        /// <returns>The formatted log lines in timestamp order.</returns>
        public static IReadOnlyList<string> GetLogLines()
        {
            return LogEntries
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value)
                .ToList();
        }

        /// <summary>
        /// Writes all recorded log lines to a timestamped TXT file under the repository's
        /// <c>AsynCUDA12.Shared\Logs</c> folder and prunes the directory so only the newest
        /// <see cref="MaxRepositoryLogFiles"/> files remain.
        /// </summary>
        /// <returns>The full path of the written file.</returns>
        public static string SaveToRepository()
        {
            string directory = ResolveRepositoryLogDirectory();
            Directory.CreateDirectory(directory);

            string fileName = $"AggregatedLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            string path = Path.Combine(directory, fileName);

            IReadOnlyList<string> snapshot = GetLogLines();

            var sb = new StringBuilder();
            sb.AppendLine("==============================================================");
            sb.AppendLine("Aggregated Log Export");
            sb.Append("Timestamp : ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("Entries   : ").AppendLine(snapshot.Count.ToString());
            sb.AppendLine("==============================================================");
            sb.AppendLine();
            foreach (string line in snapshot)
            {
                sb.AppendLine(line);
            }

            File.WriteAllText(path, sb.ToString());
            PruneOldRepositoryLogs(directory);

            Log($"[SUCCESS] Log saved to {fileName} ({snapshot.Count} entries)");

            return path;
        }

        /// <summary>
        /// The maximum number of saved log files to retain in the repository log directory.
        /// </summary>
        public const int MaxRepositoryLogFiles = 16;

        /// <summary>
        /// Deletes the oldest saved log files so at most <see cref="MaxRepositoryLogFiles"/> remain.
        /// </summary>
        private static void PruneOldRepositoryLogs(string directory)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(directory)
                    .GetFiles("AggregatedLog_*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToArray();

                foreach (FileInfo file in files.Skip(MaxRepositoryLogFiles))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Walks up from the app binaries to the repository root (the folder containing AsynCUDA13.sln)
        /// and returns the <c>AsynCUDA13.Shared\Logs</c> directory. Falls back to a directory next to the
        /// binaries when the solution cannot be located.
        /// </summary>
        private static string ResolveRepositoryLogDirectory()
        {
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "AsynCUDA";
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, $"{assemblyName}.sln")))
                {
                    return Path.Combine(dir.FullName, $"{assemblyName}.Shared", "Logs");
                }

                dir = dir.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "Logs");
        }



        public static void ClearLogs()
        {
            LogEntries.Clear();
            if (UiContext != null)
            {
                UiContext.Post(_ => LogEntriesBindingList.Clear(), null);
            }
            else
            {
                lock (LogEntriesBindingList)
                {
                    LogEntriesBindingList.Clear();
                }
            }
        }



    }
}