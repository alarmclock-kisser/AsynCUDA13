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
        /// <summary>
        /// A thread-safe dictionary that stores log entries with their corresponding timestamps. The key is the timestamp when the log entry was recorded, and the value is the fully formatted log line (including the timestamp prefix).
        /// </summary>
        public static readonly ConcurrentDictionary<DateTime, string> LogEntries = new();

        /// <summary>
        /// A thread-safe binding list that provides a chronological view of log entries for UI components. This list is updated whenever a new log entry is recorded, and it can be used to display log entries in a user interface. The list is synchronized with the UI context to ensure thread safety when updating the UI.
        /// </summary>
        public static readonly BindingList<string> LogEntriesBindingList = [];

        /// <summary>
        /// A thread-safe binding list that provides a filtered view of log entries based on a specified filter phrase. This list is updated whenever a new log entry is recorded, and it can be used to display filtered log entries in a user interface. The list is synchronized with the UI context to ensure thread safety when updating the UI.
        /// </summary>
        public static readonly BindingList<string> FilteredLogEntriesBindingList = [];

        /// <summary>
        /// Raised whenever a new line has been recorded. The first argument is the timestamp the entry was
        /// recorded at; the second argument is the fully formatted line (including the timestamp prefix).
        /// Subscribers must tolerate being invoked from arbitrary threads.
        /// </summary>
        public static event Action<DateTime, string>? LogWritten;

        /// <summary>
        /// Gets or sets a value indicating whether log lines are echoed to the console. True echoes every log, false echoes none, and null echoes only lines containing the phrases in <see cref="EchoToConsoleKeyPhrases"/>.
        /// </summary>
        public static Boolean? EchoToConsole { get; set; } = null;

        /// <summary>
        /// Gets or sets the key phrases that determine which log lines are echoed to the console when <see cref="EchoToConsole"/> is null. Only log lines containing any of these phrases will be echoed to the console.
        /// </summary>
        public static string[] EchoToConsoleKeyPhrases { get; set; } = new[] { "[SUCCESS]", "[ERROR]", "[WARN", "Exception:" };

        /// <summary>
        /// The directory where log files are stored.
        /// </summary>
        public static string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        /// <summary>
        /// Gets the full path of the current log file. If no log file has been created, this property will be null.
        /// </summary>
        public static string? LogFilePath { get; private set; } = null;

        /// <summary>
        /// Gets or sets a value indicating whether the logger should operate in silent mode. When set to true, log entries will not be echoed to the console or written to a log file, but they will still be recorded in the internal log entries dictionary and binding lists. This can be useful for scenarios where logging is needed for internal tracking but should not produce output to the console or files.
        /// </summary>
        public static Boolean Silent { get; set; } = false;

        /// <summary>
        /// The phrase used to filter log entries into separate BindingList.
        /// </summary>
        public static string? FilterPhrase { get; set; } = null;

        /// <summary>
        /// The opening bracket used when formatting inner exception messages in the log. This can be customized to change how inner exceptions are displayed in the log output.
        /// </summary>
        public static string InnerExceptionOpeningBracket { get; set; } = "(";

        /// <summary>
        /// The closing bracket used when formatting inner exception messages in the log. This can be customized to change how inner exceptions are displayed in the log output.
        /// </summary>
        public static string InnerExceptionClosingBracket { get; set; } = ")";

        /// <summary>
        /// The separator used when formatting inner exception messages in the log. This can be customized to change how inner exceptions are displayed in the log output.
        /// </summary>
        public static string InnerExceptionSeparator { get; set; } = " ";

        /// <summary>
        /// UI synchronization context (set from the UI at startup)
        /// </summary>
        private static SynchronizationContext? UiContext;

        /// <summary>
        /// Sets the UI synchronization context for updating the BindingList from the UI thread. This method should be called from the UI thread during application startup to ensure that log entries are added to the BindingList in a thread-safe manner.
        /// </summary>
        /// <param name="context">The synchronization context of the UI thread.</param>
        public static void SetUiContext(SynchronizationContext? context)
        {
            context ??= SynchronizationContext.Current;
            UiContext = context;
            Log("[Logger] StaticLogger UI context set");
        }

        /// <summary>
        /// Initializes the log files in the specified directory. If the directory does not exist, it will be created. If <paramref name="createLogFile"/> is true, a new log file will be created with a timestamped name. The method also manages the number of previous log files to retain based on <paramref name="maxPreviousLogFiles"/>. If set to 0, all previous logs will be cleared; if set to 1 or more, only the most recent specified number of logs will be kept.
        /// </summary>
        /// <param name="logDirectory">The directory where log files are stored.</param>
        /// <param name="createLogFile">Whether to create a new log file.</param>
        /// <param name="maxPreviousLogFiles">The maximum number of previous log files to retain.</param>
        public static void InitializeLogFiles(string? logDirectory = null, Boolean createLogFile = false, Int32 maxPreviousLogFiles = 3)
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

        /// <summary>
        /// Logs a message with a timestamp. The message is added to the internal log entries dictionary, and if it matches the filter phrase (if any), it is also added to the filtered log entries binding list. The method raises the LogWritten event and optionally echoes the message to the console and writes it to a log file if configured.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Log(string message)
        {
            DateTime timestamp = DateTime.Now;
            string logEntry = $"[{timestamp:HH:mm:ss.fff}] {message}";
            LogEntries[timestamp] = logEntry;
            if (Silent)
            {
                return;
            }

            if (string.IsNullOrEmpty(FilterPhrase) || !logEntry.Contains(FilterPhrase, StringComparison.OrdinalIgnoreCase))
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
                    UiContext.Post(_ => FilteredLogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    // Fallback: add on current thread
                    lock (FilteredLogEntriesBindingList)
                    {
                        FilteredLogEntriesBindingList.Add(logEntry);
                    }
                }
            }

            RaiseLogWritten(timestamp, logEntry);

            if (EchoToConsole == true || ShouldEchoToConsole(logEntry))
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
        private static Boolean ShouldEchoToConsole(string logEntry)
        {
            if (Silent)
            {
                return false;
            }

            if (EchoToConsole == true)
            {
                return true;
            }
            else if (EchoToConsole == false)
            {
                return false;
            }
            else
            {
                return EchoToConsoleKeyPhrases.Any(phrase => logEntry.Contains(phrase, StringComparison.OrdinalIgnoreCase));
            }
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

        /// <summary>
        /// Logs an exception with an optional pre-text message. The exception's message and stack trace are included in the log entry. If a pre-text message is provided, it is logged before the exception details.
        /// </summary>
        /// <param name="ex">The exception to log.</param>
        /// <param name="preText">An optional pre-text message to include before the exception details.</param>
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

        /// <summary>Logs an exception with an optional pre-text message (echoed to the console).</summary>
        /// <param name="ex">The exception to log.</param>
        /// <param name="configureAwait">Whether to configure await.</param>
        public static async Task LogAsync(string message, Boolean configureAwait = false)
        {
            await Task.Run(() => Log(message)).ConfigureAwait(configureAwait);
        }

        /// <summary>Logs an exception with an optional pre-text message (echoed to the console).</summary>
        /// <param name="ex">The exception to log.</param>
        /// <param name="preText">An optional pre-text message to include before the exception details.</param>
        /// <param name="configureAwait">Whether to configure await.</param>
        public static async Task LogAsync(Exception ex, string? preText = null, Boolean configureAwait = false)
        {
            await Task.Run(() => Log(ex, preText)).ConfigureAwait(configureAwait);
        }

        /// <summary>
        /// Records a user/debugging comment anchored to the timestamp captured when the user initiated it
        /// (for example when a "comment now" button was pressed), so it lands in the log at the right moment.
        /// </summary>
        /// <param name="capturedAt">The timestamp captured at the moment the comment was initiated.</param>
        /// <param name="comment">The free-form comment text.</param>
        public static void AddComment(DateTime? capturedAt = null, string comment = "<!!!>")
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return;
            }

            capturedAt ??= DateTime.Now;
            string logEntry = $"[{capturedAt:HH:mm:ss.fff}] [COMMENT] {comment}";
            LogEntries[capturedAt.Value] = logEntry;

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

            RaiseLogWritten(capturedAt.Value, logEntry);

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
        /// <c>AsynCUDA13.Shared\Logs</c> folder and prunes the directory so only the newest
        /// <see cref="MaxRepositoryLogFiles"/> files remain.
        /// </summary>
        /// <returns>The full path of the written file.</returns>
        public static string SaveToRepository(string? differentFilePathOrDirectory = null)
        {
            string directory;
            if (Directory.Exists(differentFilePathOrDirectory))
            {
                directory = differentFilePathOrDirectory;
            }
            else
            {
                directory = ResolveRepositoryLogDirectory();
                Directory.CreateDirectory(directory);
            }

            string fileName;
            if (!string.IsNullOrEmpty(differentFilePathOrDirectory) && !Directory.Exists(differentFilePathOrDirectory))
            {
                fileName = Path.GetFileName(differentFilePathOrDirectory);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"AggregatedLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                }
            }
            else
            {
                fileName = $"AggregatedLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            }
            string path = Path.Combine(directory, fileName);

            IReadOnlyList<string> snapshot = GetLogLines();

            var sb = new stringBuilder();
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
        /// Returns the full paths of all log files in the log directory, ordered by creation time (newest first). This method searches for files with the extensions ".txt" and ".log" in the specified log directory and returns their paths as an enumerable collection. The returned list can be used to access or manage existing log files.
        /// </summary>
        /// <returns>An enumerable collection of full paths to log files.</returns>
        public static IEnumerable<string> GetAllLogFilePaths()
        {
            return Directory.GetFiles(LogDirectory, "*.txt").Concat(Directory.GetFiles(LogDirectory, "*.log"))
                .OrderByDescending(f => f)
                .ToList();
        }

        /// <summary>
        /// Returns the full path of a previous log file based on the specified index. The index is zero-based, where 0 corresponds to the most recent log file, 1 corresponds to the second most recent log file, and so on. If the specified index is out of range (i.e., there are fewer log files than the index), this method returns null.
        /// </summary>
        /// <param name="backIndex">The zero-based index of the log file to retrieve, where 0 is the most recent log file.</param>
        /// <returns>The full path of the previous log file, or null if the index is out of range.</returns>
        public static string? GetPreviousLogFilePath(Int32 backIndex)
        {
            return GetAllLogFilePaths().Skip(backIndex).FirstOrDefault();
        }

        /// <summary>
        /// The maximum number of saved log files to retain in the repository log directory.
        /// </summary>
        public const Int32 MaxRepositoryLogFiles = 16;

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


        /// <summary>
        /// Clears all recorded log entries from the internal dictionary and the binding lists. This method is thread-safe and ensures that the UI context is used to update the binding lists if available. After calling this method, both <see cref="LogEntriesBindingList"/> and <see cref="FilteredLogEntriesBindingList"/> will be empty.
        /// </summary>
        public static void ClearLogs()
        {
            LogEntries.Clear();
            if (UiContext != null)
            {
                UiContext.Post(_ => LogEntriesBindingList.Clear(), null);
                UiContext.Post(_ => FilteredLogEntriesBindingList.Clear(), null);
            }
            else
            {
                lock (LogEntriesBindingList)
                {
                    LogEntriesBindingList.Clear();
                }
                lock (FilteredLogEntriesBindingList)
                {
                    FilteredLogEntriesBindingList.Clear();
                }
            }
        }


        /// <summary>
        /// Returns a string representation of all inner exceptions of the provided exception, including their messages and stack traces, recursively. This method is useful for logging or displaying detailed information about nested exceptions.
        /// </summary>
        /// <param name="ex">The exception to process.</param>
        /// <param name="openingBracket">The opening bracket to use for inner exception messages.</param>
        /// <param name="closingBracket">The closing bracket to use for inner exception messages.</param>
        /// <param name="separator">The separator to use between inner exception messages.</param>
        /// <returns>A string containing the details of the exception and all its inner exceptions.</returns>
        public static string GetAllInnerExceptionsRecursively(Exception ex, string openingBracket = "(", string closingBracket = ")", string separator = " ")
        {
            if (ex == null)
            {
                return string.Empty;
            }

            stringBuilder sb = new stringBuilder();
            sb.AppendLine($"Exception: {ex.GetType().FullName}");
            string message = $"Message: {ex.Message}";

            Exception? inner = ex.InnerException;
            Int32 count = 0;
            while (inner != null)
            {
                message = message + $"{separator}{openingBracket}{inner.Message}";
                inner = inner.InnerException;
                count++;
            }
            message = message + string.Concat(Enumerable.Repeat(closingBracket, count));

            sb.AppendLine(message);
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            return sb.ToString();
        }

        /// <summary>
        /// Returns a string representation of all inner exceptions of the provided exception, including their messages and stack traces, recursively. This method is useful for logging or displaying detailed information about nested exceptions. It uses default formatting with parentheses and spaces to separate inner exception messages.
        /// </summary>
        /// <param name="ex">The exception to process.</param>
        /// <returns>A string containing the details of the exception and all its inner exceptions.</returns>
        public static string GetAllInnerExceptionsRecursively(Exception ex)
        {
            return GetAllInnerExceptionsRecursively(ex, InnerExceptionOpeningBracket, InnerExceptionClosingBracket, InnerExceptionSeparator);
        }


    }
}