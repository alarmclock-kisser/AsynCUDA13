using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Options
{
    /// <summary>
    /// Konfigurationsoptionen für RollingFileMemoryLogger.
    /// </summary>
    public class RollingFileMemoryLoggerOptions
    {
        /// <summary>
        /// The maximum number of log entries to retain. Unlimited when null, which can be dangerous in long-running applications. When the limit is reached, entries are removed (if UseRingBuffer is true) or the log file is written and entries are cleared (if UseRingBuffer is false).
        /// </summary>
        public int? MaxLogEntries { get; set; } = 65536;

        /// <summary>
        /// Gets or sets a value indicating whether to use a ring buffer. When true, oldest entries are removed to make room for new ones when the limit is reached. When false, the log file is written and entries are cleared when full. When null, ring buffer logic is used but the log file is written every limit interval.
        /// </summary>
        public bool? UseRingBuffer { get; set; } = null;

        /// <summary>
        /// The directory where log files are stored.
        /// </summary>
        public string LogDirectory { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        /// <summary>
        /// Whether to create a new log file upon initialization.
        /// </summary>
        public bool CreateLogFile { get; set; } = false;

        /// <summary>
        /// The maximum number of previous log files to retain.
        /// </summary>
        public int MaxLogFiles { get; set; } = 32;

        /// <summary>
        /// The maximum number of saved log files to retain in the repository log directory.
        /// </summary>
        public int MaxRepositoryLogFiles { get; set; } = 8;

        /// <summary>
        /// Gets or sets a value indicating whether the logger should operate in silent mode. When set to true, log entries will not be echoed to the console or written to a log file, but they will still be recorded in the internal log entries dictionary and binding lists. This can be useful for scenarios where logging is needed for internal tracking but should not produce output to the console or files.
        /// </summary>
        public bool Silent { get; set; } = false;

        /// <summary>
        /// Gets or sets the format string used for timestamps in log entries. If null, no timestamps will be added to the log entry strings.
        /// </summary>
        public string? LogTimestampFormat { get; set; } = "HH:mm:ss.fff";

        /// <summary>
        /// Gets or sets the format string used for timestamps in log file names. This format is applied when creating new log files to ensure unique and timestamped file names. The default format is "yyyy-MM-dd_HH-mm-ss", which results in file names like "Log_2024-06-15_14-30-45.txt".
        /// </summary>
        public string FileTimestampFormat { get; set; } = "yyyy-MM-dd_HH-mm-ss";

        /// <summary>
        /// Gets or sets the base name used for log files. The default value is "Log", but it can be changed to any valid file name to suit specific logging requirements. This property affects the naming of log files created by the logger.
        /// </summary>
        public string LogFileBaseName { get; set; } = "dotnet10-Application_Log";

        /// <summary>
        /// Gets or sets the file extension used for log files. The default value is ".txt", but it can be changed to any valid file extension (e.g., ".log") to suit specific logging requirements. This property affects the naming of log files created by the logger. Must not contain the trailing dot (.) character, as it will be automatically added when creating log files.
        /// </summary>
        public string LogFileExtension { get; set; } = ".txt";

        /// <summary>
        /// The phrase used to filter log entries into separate BindingList.
        /// </summary>
        public string? FilterPhrase { get; set; } = null;

        /// <summary>
        /// Gets or sets a value indicating whether log lines are echoed to the console. True echoes every log, false echoes none, and null echoes only lines containing the phrases in EchoToConsoleKeyPhrases.
        /// </summary>
        public bool? EchoToConsole { get; set; } = null;

        /// <summary>
        /// Gets or sets the key phrases that determine which log lines are echoed to the console when EchoToConsole is null. Only log lines containing any of these phrases will be echoed to the console.
        /// </summary>
        public string[] EchoToConsoleKeyPhrases { get; set; } = ["[SUCCESS]", "[ERROR]", "[WARN", "Exception:"];

        /// <summary>
        /// Whether saving to the repository is configured and enabled.
        /// </summary>
        public bool SaveToRepository { get; set; } = false;

        /// <summary>
        /// Custom file path or directory for saving logs to the repository.
        /// </summary>
        public string? SaveToRepositoryCustomFilePath { get; set; } = null;

        /// <summary>
        /// Gets or sets the exception print settings that control how inner exceptions are logged. This property allows customization of the depth, formatting, and inclusion of stack traces for inner exceptions when logging exceptions.
        /// </summary>
        public ExceptionPrintOptions ExceptionPrintSettings { get; set; } = new ExceptionPrintOptions();


        /// <summary>
        /// Initializes a new instance of the <see cref="RollingFileMemoryLoggerOptions"/> class with default settings. If a custom timestamp format is provided, it verifies the format string and logs a warning if the format is invalid, reverting to the default format.
        /// </summary>
        public RollingFileMemoryLoggerOptions()
        {
            if (!string.IsNullOrEmpty(this.LogTimestampFormat))
            {
                string original = this.LogTimestampFormat;
                this.LogTimestampFormat = this.LogTimestampFormat.VerifyFormatString(out string? err);
                if (!string.IsNullOrEmpty(err))
                {
                    try
                    {
                        RollingFileMemoryLogger.Instance.Log($"[WARN] Invalid TimestampFormat '{original}' in StaticLoggerSettings. Reverting to default format 'HH:mm:ss.fff'. Error: {err}");
                    }
                    catch { }
                }
            }
        }
    }
}
