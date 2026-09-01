using AsynCUDA13.Shared.Options;

namespace AsynCUDA13.Shared.Interfaces
{
    /// <summary>
    /// A logger class that provides thread-safe logging functionality for applications. It allows logging messages, exceptions, and user comments with timestamps, and supports filtering log entries based on a specified phrase. The logger can write log entries to a file, echo them to the console, and maintain a binding list for UI components. It also provides methods for saving logs to a repository and managing log files.
    /// </summary>
    public interface IRollingFileMemoryLogger
    {
        /// <summary>
        /// Global settings for the StaticLogger.
        /// </summary>
        public RollingFileMemoryLoggerOptions Settings { get; }

        /// <summary>
        /// Gets the full path of the current log file. If no log file has been created, this property will be null.
        /// </summary>
        public string? LogFilePath { get; }

        /// <summary>
        /// Action to perform on shutdown to save logs to the repository.
        /// </summary>
        public Action? SaveToRepositoryOnShutdown { get; }

        /// <summary>
        /// Raised whenever a new line has been recorded. The first argument is the timestamp the entry was
        /// recorded at; the second argument is the fully formatted line (including the timestamp prefix).
        /// Subscribers must tolerate being invoked from arbitrary threads.
        /// </summary>
        public event Action<DateTime, string>? LogWritten;

        /// <summary>
        /// Records a user/debugging comment anchored to the timestamp captured when the user initiated it
        /// (for example when a "comment now" button was pressed), so it lands in the log at the right moment.
        /// </summary>
        /// <param name="capturedAt">The timestamp captured at the moment the comment was initiated.</param>
        /// <param name="elapsedSince">The elapsed time since the comment was captured.</param>
        /// <param name="comment">The free-form comment text.</param>
        public void AddComment(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>");

        /// <summary>
        /// Asynchronously records a user/debugging comment anchored to the timestamp captured when the user initiated it (for example when a "comment now" button was pressed), so it lands in the log at the right moment.
        /// </summary>
        /// <param name="capturedAt">The timestamp captured at the moment the comment was initiated.</param>
        /// <param name="elapsedSince">The elapsed time since the comment was captured.</param>
        /// <param name="comment">The free-form comment text.</param>
        /// <param name="configureAwait">Whether to configure await.</param>
        public Task AddCommentAsync(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>", bool? configureAwait = null);

        /// <summary>
        /// Clears all recorded log entries from the internal dictionary and the binding lists. This method is thread-safe and ensures that the UI context is used to update the binding lists if available.
        /// </summary>
        public void ClearLogs();

        /// <summary>
        /// Configures the logger to save all recorded log lines to a timestamped TXT file under the repository's Logs folder.
        /// </summary>
        /// <param name="configureToggle">If true, enables saving to the repository; if false, disables it; if null, toggles the current state.</param>
        /// <param name="subDirOrDifferentPath">Custom subdirectory or different path for the log file.</param>
        /// <param name="maxPreviousLogFiles">Maximum number of previous log files to retain.</param>
        /// <param name="onShutdown">Action to perform on shutdown.</param>
        public string ConfigureSaveToRepository(bool? configureToggle = null, string? subDirOrDifferentPath = null, int maxPreviousLogFiles = 8, Action? onShutdown = null);

        /// <summary>
        /// Returns the full paths of all log files in the log directory, ordered by creation time (newest first). This method searches for files with the extensions ".txt" and ".log" in the specified log directory and returns their paths.
        /// </summary>
        public string[] GetAllLogFilePaths();

        /// <summary>
        /// Returns a string representation of all inner exceptions of the provided exception, including their messages and stack traces, recursively. This method is useful for logging or displaying detailed information about nested exceptions. It uses default formatting with parentheses and spaces to separate inner exception messages.
        /// </summary>
        /// <param name="ex">The exception to process.</param>
        public string GetInnerExceptionsRecursively(Exception ex);

        /// <summary>
        /// Returns a string representation of all inner exceptions of the provided exception, including their messages and stack traces, recursively. This method is useful for logging or displaying detailed information about nested exceptions.
        /// </summary>
        /// <param name="ex">The exception to process.</param>
        /// <param name="maxDepth">Maximum depth of inner exceptions to include.</param>
        /// <param name="appendStackTrace">Whether to append the stack trace.</param>
        /// <param name="openingBracket">The opening bracket to use for inner exception messages.</param>
        /// <param name="closingBracket">The closing bracket to use for inner exception messages.</param>
        /// <param name="separator">The separator to use between inner exception messages.</param>
        /// <param name="asSingleLine">Whether to return the result as a single line.</param>
        public string GetInnerExceptionsRecursively(Exception ex, int? maxDepth = null, bool appendStackTrace = true, string openingBracket = "(", string closingBracket = ")", string separator = " ", bool asSingleLine = false);

        /// <summary>
        /// Returns a read-only list of log lines, optionally filtered based on the FilterPhrase. If returnFilteredLog is true, only the filtered log entries are returned; if false, only the main log list is returned; if null, all log entries are returned in chronological order.
        /// </summary>
        /// <param name="returnFilteredLog">If true, returns ONLY the filtered log entries, if null, returns all log entries chronologically merged, if false, returns only the main log list.</param>
        /// <param name="reverseOrder">If true, returns the log entries in reverse chronological order.</param>
        public IReadOnlyList<string> GetLogLines(bool? returnFilteredLog = false, bool reverseOrder = false);

        /// <summary>
        /// Returns an array of namespace strings for the specified project name. If the project name is null, the namespace of the logger class is used.
        /// </summary>
        /// <param name="projectName">The name of the project to retrieve namespaces for. If null, the namespace of the logger class is used.</param>
        /// <param name="includeSubNamespaces">If true, includes sub-namespaces of the specified project namespace.</param>
        /// <param name="ignoreCase">If true, performs a case-insensitive comparison of namespaces.</param>
        public string[]? GetNamespacesForProject(string? projectName = null, bool includeSubNamespaces = true, bool ignoreCase = true);

        /// <summary>
        /// Returns the full path of a previous log file based on the specified index. The index is zero-based, where 0 corresponds to the most recent log file, 1 corresponds to the second most recent log file, and so on. If the specified index is out of range, this method returns null.
        /// </summary>
        /// <param name="backIndex">The zero-based index of the log file to retrieve, where 0 is the most recent log file.</param>
        public string? GetPreviousLogFilePath(int backIndex = 0);

        /// <summary>
        /// Gets the UI synchronization context for updating the BindingList from the UI thread. If copy is true, a copy of the synchronization context is returned; otherwise, the original context is returned.
        /// </summary>
        /// <param name="copy">Whether to return a copy of the synchronization context.</param>
        public SynchronizationContext? GetUiContext(bool copy = false);

        /// <summary>
        /// Initializes the log files. If a settings object is provided, it will be used to configure the logger. Otherwise, the global Settings will be used.
        /// </summary>
        /// <param name="options">Optional settings to override the global configuration.</param>
        /// <param name="onShutdown">Optional action to perform on shutdown (e.g. save logs to repository).</param>
        /// <param name="exitCancellationToken">Cancellation token for application shutdown.</param>
        /// <param name="synchronizationContext">UI synchronization context for thread-safe UI updates.</param>
        /// <param name="setGlobally">If true, applies the settings globally to the singleton instance.</param>
        public void InitializeLogger(RollingFileMemoryLoggerOptions? options = null, Action? onShutdown = null, CancellationToken? exitCancellationToken = null, SynchronizationContext? synchronizationContext = null, bool setGlobally = false);

        /// <summary>
        /// Logs an exception with an optional pre-text message. The exception's message and stack trace are included in the log entry. If a pre-text message is provided, it is logged before the exception details.
        /// </summary>
        /// <param name="ex">The exception to log.</param>
        /// <param name="maxInnerEx">Maximum number of inner exceptions to include.</param>
        /// <param name="appendStackTrace">Whether to append the stack trace.</param>
        /// <param name="preText">An optional pre-text message to include before the exception details.</param>
        public void Log(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null);

        /// <summary>
        /// Logs a message with a timestamp. The message is added to the internal log entries dictionary, and if it matches the filter phrase (if any), it is also added to the filtered log entries binding list. The method raises the LogWritten event and optionally echoes the message to the console and writes it to a log file if configured.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Log(string message);

        /// <summary>
        /// Logs a contextual message together with an exception.
        /// </summary>
        /// <param name="message">The contextual message.</param>
        /// <param name="ex">The exception to append.</param>
        /// <param name="maxInnerEx">Maximum number of inner exceptions to include.</param>
        /// <param name="appendStackTrace">Whether to append the stack trace.</param>
        public void Log(string message, Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true);

        /// <summary>
        /// Logs an exception with an optional pre-text message (echoed to the console).
        /// </summary>
        /// <param name="ex">The exception to log.</param>
        /// <param name="maxInnerEx">Maximum number of inner exceptions to include.</param>
        /// <param name="appendStackTrace">Whether to append the stack trace.</param>
        /// <param name="preText">An optional pre-text message to include before the exception details.</param>
        /// <param name="configureAwait">Whether to configure await.</param>
        public Task LogAsync(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null, bool? configureAwait = null);

        /// <summary>
        /// Logs a message asynchronously (echoed to the console).
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="configureAwait">Whether to configure await.</param>
        public Task LogAsync(string message, bool? configureAwait = null);

        /// <summary>
        /// Logs an error message (echoed to the console).
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogError(string message);

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogInfo(string message);

        /// <summary>
        /// Logs a success message (echoed to the console).
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogSuccess(string message);

        /// <summary>
        /// Logs a warning message (echoed to the console).
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogWarning(string message);

        /// <summary>
        /// Resolves the full path of the repository or project directory based on the provided project name and optional subdirectory. If the project name is null, the namespace of the logger class is used.
        /// </summary>
        /// <param name="projectName">Project name within the repository, string.Empty ("") for root path, null for this namespaces project.</param>
        /// <param name="subPath">Optional relative subdirectory within the project or repository directory.</param>
        /// <param name="ensureDirectoryExists">If true, ensures that the resolved directory exists by creating it if necessary.</param>
        public string ResolveRepositoryDirectory(string? projectName = null, string subPath = "Logs", bool ensureDirectoryExists = false);

        /// <summary>
        /// Resolves the full path of a log file in the repository's log directory, optionally using a custom file path or directory. If a custom file path is provided and it points to an existing directory, the log file will be created in that directory.
        /// </summary>
        /// <param name="differentFilePathOrDirectory">Custom file path or directory for the log file.</param>
        public string ResolveRepositoryLogFilePath(string? differentFilePathOrDirectory = null);

        /// <summary>
        /// Saves the current log entries to a timestamped TXT file in the repository's log directory.
        /// </summary>
        /// <param name="differentFilePathOrDirectory">An optional different file path or directory to save the log file to. If null, the default repository log directory is used.</param>
        /// <param name="returnFilteredLog">Whether to return the filtered log entries.</param>
        /// <param name="reverseOrder">Whether to reverse the order of the log entries.</param>
        /// <param name="forceSave">Whether to force saving the log file even if the setting to save to the repository is disabled.</param>
        public string SaveToRepository(string? differentFilePathOrDirectory = null, bool? returnFilteredLog = null, bool reverseOrder = false, bool forceSave = false);

        /// <summary>
        /// Sets the action to perform on shutdown to save logs to the repository and registers it on the provided cancellation token so it is invoked when the application is shutting down.
        /// </summary>
        /// <param name="onShutdown">The action to perform on shutdown.</param>
        /// <param name="cancellationToken">Cancellation token signalling application shutdown (e.g. app.Lifetime.ApplicationStopping).</param>
        /// <param name="setGlobally">If true, applies the shutdown action globally to the singleton instance.</param>
        public void SetOnShutdownAction(Action? onShutdown, CancellationToken cancellationToken = default, bool setGlobally = false);

        /// <summary>
        /// Sets the UI synchronization context for updating the BindingList from the UI thread. This method should be called from the UI thread during application startup to ensure that log entries are added to the BindingList in a thread-safe manner.
        /// </summary>
        /// <param name="context">The synchronization context of the UI thread.</param>
        public void SetUiContext(SynchronizationContext? context);

        /// <summary>
        /// Starts the background writer task that consumes log entries from the channel and writes them to the log file. This method should be called once during initialization to ensure that log entries are written to the file asynchronously without blocking the main thread.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for stopping the background writer.</param>
        public void StartBackgroundWriter(CancellationToken cancellationToken);
    }
}