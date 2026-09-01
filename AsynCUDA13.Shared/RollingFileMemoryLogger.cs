using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Options;
using AsynCUDA13.Shared.Utils;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AsynCUDA13.Shared
{
    public partial class RollingFileMemoryLogger : IRollingFileMemoryLogger
    {
        public RollingFileMemoryLoggerOptions Settings { get; private set; } = new();


        public static readonly RollingFileMemoryLogger Instance = new();

        public static bool ApplyInstanceSettingsGlobally { get; private set; } = true;

        public static bool ApplyInstanceOnShutdownGlobally { get; private set; } = true;



        public RollingFileMemoryLogger(RollingFileMemoryLoggerOptions? settings = null, bool? applyGlobalSettings = null, Action? onShutdown = null, CancellationToken? exitCancellationToken = null, SynchronizationContext? synchronizationContext = null, bool setGlobally = false)
        {
            this.Settings = settings ?? new RollingFileMemoryLoggerOptions();

            if (applyGlobalSettings == true)
            {
                if (ApplyInstanceSettingsGlobally)
                {
                    this.Settings = Instance.Settings;
                }
                if (ApplyInstanceOnShutdownGlobally)
                {
                    this.SetOnShutdownAction(Instance.SaveToRepositoryOnShutdown, Instance._logCts is not null ? Instance._logCts.Token : CancellationToken.None, false);
                }
            }

            this._logChannel = System.Threading.Channels.Channel.CreateBounded<string>(new System.Threading.Channels.BoundedChannelOptions(this.Settings.MaxLogEntries ?? 16384)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
            });

            this.InitializeLogger(this.Settings, onShutdown, exitCancellationToken, synchronizationContext, setGlobally);
        }



        public readonly ConcurrentDictionary<DateTime, string> LogEntries = [];


        private readonly System.Threading.Channels.Channel<string> _logChannel;

        private Task? _logWriterTask;
        private CancellationTokenSource? _logCts;

        public void StartBackgroundWriter(CancellationToken cancellationToken)
        {
            if (this._logWriterTask != null) return;
            this._logCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Link channel completion to the cancellation token
            this._logCts.Token.Register(() => this._logChannel.Writer.TryComplete());

            this._logWriterTask = Task.Run(async () =>
            {
                StreamWriter? streamWriter = null;
                string? currentFilePath = null;

                try
                {
                    await foreach (var line in this._logChannel.Reader.ReadAllAsync(this._logCts.Token))
                    {
                        if (this.LogFilePath != null)
                        {
                            // Create or switch writer if file path changes (e.g., ring buffer rolling)
                            if (streamWriter == null || currentFilePath != this.LogFilePath)
                            {
                                streamWriter?.Dispose();
                                currentFilePath = this.LogFilePath;
                                streamWriter = new StreamWriter(new FileStream(currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true), Encoding.UTF8);
                            }

                            await streamWriter.WriteLineAsync(line.AsMemory(), this._logCts.Token);
                            await streamWriter.FlushAsync(); // Flush immediately for realtime logging
                        }
                    }
                }
                catch (OperationCanceledException) when (this._logCts.Token.IsCancellationRequested)
                {
                    // Clean exit on cancellation
                }
                finally
                {
                    streamWriter?.Dispose();
                }
            }, this._logCts.Token);
        }

        private int _logEntriesRingBufferCounter = 0;

        public readonly BindingList<string> LogEntriesBindingList = [];

        public readonly BindingList<string> FilteredLogEntriesBindingList = [];


        public string? LogFilePath { get; private set; } = null;


        public event Action<DateTime, string>? LogWritten;

        private SynchronizationContext? UiContext;

        public Action? SaveToRepositoryOnShutdown { get; private set; } = null;


        public SynchronizationContext? GetUiContext(bool copy = false) => copy ? this.UiContext?.CreateCopy() : this.UiContext;

        public void SetUiContext(SynchronizationContext? context)
        {
            context ??= SynchronizationContext.Current;
            this.UiContext = context;
            string projectName = this.GetType().Namespace?.Split('.').FirstOrDefault() ?? "---";
            this.Log($"[Logger] RollingFileMemoryLogger UI context set for project <{projectName}>");
        }

        public void SetOnShutdownAction(Action? onShutdown, CancellationToken cancellationToken = default, bool setGlobally = false)
        {
            this.SaveToRepositoryOnShutdown = onShutdown;
            if (setGlobally)
            {
                Instance.SetOnShutdownAction(onShutdown, cancellationToken, false);
            }

            if (cancellationToken != default)
            {
                cancellationToken.Register(() => this.SaveToRepositoryOnShutdown?.Invoke());
                if (setGlobally)
                {
                    cancellationToken.Register(() => Instance.SaveToRepositoryOnShutdown?.Invoke());
                }
            }
        }


        public void InitializeLogger(RollingFileMemoryLoggerOptions? options = null, Action? onShutdown = null, CancellationToken? exitCancellationToken = null, SynchronizationContext? synchronizationContext = null, bool setGlobally = false)
        {
            options ??= new();
            string projectName = this.GetType().Namespace?.Split('.').FirstOrDefault() ?? "---";

            options.LogDirectory = Path.GetFullPath(string.IsNullOrEmpty(options.LogDirectory) ? Path.Combine(Assembly.GetAssembly(typeof(RollingFileMemoryLogger))?.Location ?? AppContext.BaseDirectory, "Logs") : options.LogDirectory);
            if (!string.IsNullOrEmpty(options.LogDirectory))
            {
                this.Settings.LogDirectory = options.LogDirectory;
                if (setGlobally)
                {
                    Instance.Settings.LogDirectory = options.LogDirectory;
                }
            }

            onShutdown ??= () => { this.SaveToRepository(); };
            this.SaveToRepositoryOnShutdown = onShutdown;
            if (setGlobally)
            {
                Instance.SaveToRepositoryOnShutdown = onShutdown;
                ApplyInstanceOnShutdownGlobally = true;
                Instance.Log($"[Logger] RollingFileMemoryLogger global shutdown action applied for project <{projectName}>");
            }

            if (exitCancellationToken.HasValue && exitCancellationToken.Value != default)
            {
                exitCancellationToken.Value.Register(() => this.SaveToRepositoryOnShutdown?.Invoke());
                if (setGlobally)
                {
                    exitCancellationToken.Value.Register(() => Instance.SaveToRepositoryOnShutdown?.Invoke());
                    ApplyInstanceOnShutdownGlobally = true;
                    Instance.Log($"[Logger] RollingFileMemoryLogger global shutdown CancellationToken registered for project <{projectName}>");
                }
            }

            if (synchronizationContext != null)
            {
                this.SetUiContext(synchronizationContext);
                if (setGlobally)
                {
                    Instance.SetUiContext(synchronizationContext);
                }
            }

            try
            {
                if (!Directory.Exists(this.Settings.LogDirectory))
                {
                    Directory.CreateDirectory(this.Settings.LogDirectory);
                }

                if (options.MaxLogFiles == 0)
                {
                    // Clear all previous logs if exactly 0 is specified
                    Directory.Delete(this.Settings.LogDirectory, true);
                    Directory.CreateDirectory(this.Settings.LogDirectory);
                }
                else if (options.MaxLogFiles >= 1)
                {
                    var existingLogs = Directory.GetFiles(this.Settings.LogDirectory, "log_*.txt")
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(fi => fi.CreationTime)
                        .ToList();
                    // Keep only the most recent 'MaxLogFiles' logs
                    foreach (var oldLog in existingLogs.Skip(options.MaxLogFiles))
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

                    if (setGlobally)
                    {
                        Instance.PruneOldRepositoryLogs(this.Settings.LogDirectory);
                    }
                }

                if (options.CreateLogFile)
                {
                    this.LogFilePath = Path.Combine(this.Settings.LogDirectory, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.Create(this.LogFilePath).Dispose();
                    this.Log($"Log file created at {this.LogFilePath}");
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error with log files initialization: {ex.Message}");
            }
            finally
            {
                this.Log($"[Logger] RollingFileMemoryLogger initialized for project <{projectName}>");
                this.Settings = options;
                if (setGlobally)
                {
                    Instance.Settings = options;
                    ApplyInstanceSettingsGlobally = true;
                    Instance.Log($"[Logger] RollingFileMemoryLogger global settings applied for project <{projectName}>");
                }
            }
        }


        public void Log(string message)
        {
            DateTime timestamp = DateTime.Now;
            string logEntry = string.IsNullOrEmpty(this.Settings.LogTimestampFormat) ? message : $"[{timestamp.ToString(this.Settings.LogTimestampFormat)}] {message}";

            this.EnsureMaxLogEntriesWithOffloadAndBuffering();

            this.LogEntries[timestamp] = logEntry;
            if (this.Settings.Silent)
            {
                return;
            }

            if (string.IsNullOrEmpty(this.Settings.FilterPhrase) || !logEntry.Contains(this.Settings.FilterPhrase, StringComparison.OrdinalIgnoreCase))
            {
                if (this.UiContext != null)
                {
                    this.UiContext.Post(_ => this.LogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    // Fallback: add on current thread
                    lock (this.LogEntriesBindingList)
                    {
                        this.LogEntriesBindingList.Add(logEntry);
                    }
                }
            }
            else
            {
                if (this.UiContext != null)
                {
                    this.UiContext.Post(_ => this.FilteredLogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    // Fallback: add on current thread
                    lock (this.FilteredLogEntriesBindingList)
                    {
                        this.FilteredLogEntriesBindingList.Add(logEntry);
                    }
                }
            }

            this.RaiseLogWritten(timestamp, logEntry);

            if (this.Settings.EchoToConsole == true || this.ShouldEchoToConsole(logEntry))
            {
                Console.WriteLine(logEntry);
            }

            if (this.LogFilePath != null)
            {
                try
                {
                    // Non-blocking write to the log channel
                    this._logChannel.Writer.TryWrite(logEntry);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to log file: {ex.Message}");
                }
            }
        }

        public void Log(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null)
        {
            this.Log($"{(string.IsNullOrEmpty(preText) ? "" : preText + "\n")}Exception: {this.GetInnerExceptionsRecursively(ex, maxInnerEx)}{(appendStackTrace ? "\nStack Trace: " + ex.StackTrace : "")}");
        }

        public void Log(string message, Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true)
        {
            this.Log($"{message} Exception: {this.GetInnerExceptionsRecursively(ex, maxInnerEx)}{(appendStackTrace ? "\nStack Trace: " + ex.StackTrace : "")}");
        }

        public void LogInfo(string message) => this.Log($"[INFO] {message}");

        public void LogSuccess(string message) => this.Log($"[SUCCESS] {message}");

        public void LogWarning(string message) => this.Log($"[WARN] {message}");

        public void LogError(string message) => this.Log($"[ERROR] {message}");

        public async Task LogAsync(string message, bool? configureAwait = null)
        {
            if (configureAwait.HasValue)
            {
                await Task.Run(() => this.Log(message)).ConfigureAwait(configureAwait.Value);

            }
            else
            {
                await Task.Run(() => this.Log(message));
            }
        }

        public async Task LogAsync(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null, bool? configureAwait = null)
        {
            if (configureAwait.HasValue)
            {
                await Task.Run(() => this.Log(ex, maxInnerEx, appendStackTrace, preText)).ConfigureAwait(configureAwait.Value);
            }
            else
            {
                await Task.Run(() => this.Log(ex, maxInnerEx, appendStackTrace, preText));
            }
        }

        public void AddComment(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>")
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return;
            }

            elapsedSince ??= TimeSpan.Zero;
            capturedAt ??= DateTime.Now.Subtract(elapsedSince.Value);

            string logEntry = string.IsNullOrEmpty(this.Settings.LogTimestampFormat) ? $"[COMMENT] {comment}" : $"[{capturedAt.Value.ToString(this.Settings.LogTimestampFormat.VerifyFormatString(out var _))}] [COMMENT] {comment}";

            this.LogEntries[capturedAt.Value] = logEntry;

            if (this.UiContext != null)
            {
                this.UiContext.Post(_ => this.LogEntriesBindingList.Add(logEntry), null);
            }
            else
            {
                lock (this.LogEntriesBindingList)
                {
                    this.LogEntriesBindingList.Add(logEntry);
                }
            }

            this.RaiseLogWritten(capturedAt.Value, logEntry);

            if (this.LogFilePath != null)
            {
                try
                {
                    File.AppendAllText(this.LogFilePath, logEntry + Environment.NewLine);
                }
                catch
                {
                }
            }
        }

        public async Task AddCommentAsync(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>", bool? configureAwait = null)
        {
            if (configureAwait.HasValue)
            {
                await Task.Run(() => this.AddComment(capturedAt, elapsedSince, comment)).ConfigureAwait(configureAwait.Value);
            }
            else
            {
                await Task.Run(() => this.AddComment(capturedAt, elapsedSince, comment));
            }
        }


        public IReadOnlyList<string> GetLogLines(bool? returnFilteredLog = false, bool reverseOrder = false)
        {
            var result = returnFilteredLog switch
            {
                true => this.FilteredLogEntriesBindingList,
                false => this.LogEntriesBindingList,
                null => this.LogEntries.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value),
            };

            return reverseOrder ? result.Reverse().ToList() : result.ToList();
        }


        public string[] GetAllLogFilePaths()
        {
            return Directory.GetFiles(this.Settings.LogDirectory, "*.txt").Concat(Directory.GetFiles(this.Settings.LogDirectory, "*.log"))
                .OrderByDescending(f => f)
                .ToArray();
        }

        public string? GetPreviousLogFilePath(int backIndex = 0)
        {
            return this.GetAllLogFilePaths().Select(l => new FileInfo(l)).OrderByDescending(f => f.CreationTime) is IEnumerable<FileInfo> fileInfos ? fileInfos.Count() > backIndex ? fileInfos.ElementAt(backIndex).FullName : null : null;
        }


        public string SaveToRepository(string? differentFilePathOrDirectory = null, bool? returnFilteredLog = null, bool reverseOrder = false, bool forceSave = false)
        {
            if (!this.Settings.SaveToRepository && !forceSave)
            {
                return string.Empty;
            }

            string path = this.ResolveRepositoryLogFilePath(differentFilePathOrDirectory ?? this.Settings.SaveToRepositoryCustomFilePath);
            string fileName = Path.GetFileName(path) ?? (string.IsNullOrEmpty(this.Settings.LogFileBaseName) ? "dotnet-Application_Log_" : this.Settings.LogFileBaseName);
            string directory = Path.GetDirectoryName(path) ?? this.ResolveRepositoryDirectory();

            IReadOnlyList<string> snapshot = this.GetLogLines(returnFilteredLog, reverseOrder);

            var sb = new StringBuilder();
            sb.AppendLine("==============================================================");
            sb.AppendLine(UniversalHelper.SanitizeString(fileName, null, "".ToCharArray(), " ", true, true) + " Log");

            string timestamp = DateTime.Now.ToString(this.Settings.FileTimestampFormat);

            nint logCount = new(snapshot.LongCount());
            sb.AppendLine($"Saved at : {timestamp}");
            sb.Append("Entries   : ").AppendLine(logCount.ToString("N0"));
            sb.AppendLine("==============================================================");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, snapshot);

            this.PruneOldRepositoryLogs(directory);
            File.WriteAllText(path, sb.ToString());

            this.Log($"[SUCCESS] Log saved to {Path.GetFileName(path)} ({logCount:N0} entries)");

            return path;
        }

        public string ConfigureSaveToRepository(bool? configureToggle = null, string? subDirOrDifferentPath = null, int maxPreviousLogFiles = 8, Action? onShutdown = null)
        {
            this.Settings.SaveToRepository = configureToggle == null ? !this.Settings.SaveToRepository : configureToggle.Value;
            this.SaveToRepositoryOnShutdown = onShutdown;
            this.Settings.SaveToRepositoryCustomFilePath = string.IsNullOrEmpty(subDirOrDifferentPath) ? null : this.ResolveRepositoryLogFilePath(subDirOrDifferentPath);

            this.Settings.MaxRepositoryLogFiles = Math.Clamp(maxPreviousLogFiles, 0, int.MaxValue);
            this.PruneOldRepositoryLogs();

            try
            {
                if (this.Settings.SaveToRepository)
                {
                    this.SaveToRepository(subDirOrDifferentPath);
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error configuring save to repository: {ex.Message}");
            }

            return this.Settings.SaveToRepositoryCustomFilePath ?? this.ResolveRepositoryDirectory();
        }


        public string[]? GetNamespacesForProject(string? projectName = null, bool includeSubNamespaces = true, bool ignoreCase = true)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                projectName = typeof(RollingFileMemoryLogger).Namespace;
                if (string.IsNullOrEmpty(projectName))
                {
                    return null;
                }
            }

            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetTypes()
                .Where(t => t.IsClass && t.Namespace != null && (includeSubNamespaces ? t.Namespace.StartsWith(projectName) : t.Namespace.Equals(projectName, (ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
                .Select(t => t.Namespace)
                .Distinct()
                .OfType<string>()
                .ToArray();
        }

        public string ResolveRepositoryDirectory(string? projectName = null, string subPath = "Logs", bool ensureDirectoryExists = false)
        {
            // Auto-namespace with this class if null, otherwise use the provided project name (string.Empty for root path)
            if (projectName is null)
            {
                projectName = typeof(RollingFileMemoryLogger).Namespace;
            }
            else if (!string.IsNullOrEmpty(projectName))
            {
                projectName = this.GetNamespacesForProject(projectName, false)?.FirstOrDefault();
            }

            string path = Path.GetFullPath(AppContext.BaseDirectory);
            DirectoryInfo? dir = new(path);
            string? assemblyName = Assembly.GetExecutingAssembly().GetName() is AssemblyName assembly ? assembly.Name ?? assembly.FullName : null;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, $"{assemblyName}.sln")) || File.Exists(Path.Combine(dir.FullName, $"{assemblyName}.slnx")))
                {
                    path = Directory.GetFiles(dir.FullName, $"{assemblyName}.sln").FirstOrDefault() ?? Directory.GetFiles(dir.FullName, $"{assemblyName}.slnx").FirstOrDefault() ?? string.Empty;
                    if (!string.IsNullOrEmpty(projectName))
                    {
                        var options = new EnumerationOptions
                        {
                            MatchCasing = MatchCasing.CaseInsensitive,
                            RecurseSubdirectories = true
                        };
                        path = Directory.EnumerateFiles(dir.FullName, $"{projectName}.csproj", options).FirstOrDefault() ?? string.Empty;
                    }
                }

                dir = dir.Parent;
            }

            string fullPath = Path.GetFullPath(path);
            if (!string.IsNullOrEmpty(fullPath) && !Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return fullPath;
        }

        public string ResolveRepositoryLogFilePath(string? differentFilePathOrDirectory = null)
        {
            string repoRootDir = this.ResolveRepositoryDirectory();
            string directory;
            if (Directory.Exists(differentFilePathOrDirectory))
            {
                directory = differentFilePathOrDirectory;
            }
            else
            {
                directory = this.ResolveRepositoryDirectory();
                Directory.CreateDirectory(directory);
            }

            string fileName;
            if (!string.IsNullOrEmpty(differentFilePathOrDirectory) && !Directory.Exists(differentFilePathOrDirectory))
            {
                fileName = Path.GetFileName(differentFilePathOrDirectory);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = this.Settings.LogFileBaseName;
                }
            }
            else
            {
                fileName = this.Settings.LogFileBaseName;
            }

            try
            {
                fileName = $"{fileName}{DateTime.Now.ToString(this.Settings.FileTimestampFormat)}";
            }
            catch
            {
                fileName = $"{fileName}{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            }

            return Path.GetFullPath(Path.Combine(directory, fileName + "." + this.Settings.LogFileExtension.Trim('.')));
        }


        public string GetInnerExceptionsRecursively(Exception ex, int? maxDepth = null, bool appendStackTrace = true, string openingBracket = "(", string closingBracket = ")", string separator = " ", bool asSingleLine = false)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            if (maxDepth <= 0)
            {
                return ex.Message;
            }

            StringBuilder sb = new();
            sb.AppendLine($"Exception: {ex.GetType().FullName}");
            string message = $"Message: {ex.Message}";

            Exception? inner = ex.InnerException;
            int count = 0;
            while (inner != null)
            {
                message += $"{separator}{openingBracket}{inner.Message}";
                inner = inner.InnerException;
                count++;
                if (maxDepth.HasValue && count >= maxDepth.Value)
                {
                    break;
                }
            }
            message += string.Concat(Enumerable.Repeat(closingBracket, count));

            sb.AppendLine(message);
            if (appendStackTrace)
            {
                sb.AppendLine($"StackTrace: {ex.StackTrace}");
            }
            return sb.Replace(Environment.NewLine, asSingleLine ? " " : Environment.NewLine).ToString();
        }

        public string GetInnerExceptionsRecursively(Exception ex)
        {
            return this.GetInnerExceptionsRecursively(ex, this.Settings.ExceptionPrintSettings.InnerExceptionMaxDepth, this.Settings.ExceptionPrintSettings.InnerExceptionAppendStackTrace, this.Settings.ExceptionPrintSettings.InnerExceptionOpeningBracket, this.Settings.ExceptionPrintSettings.InnerExceptionClosingBracket, this.Settings.ExceptionPrintSettings.InnerExceptionSeparator, this.Settings.ExceptionPrintSettings.InnerExceptionAsSingleLine);
        }



        public void ClearLogs()
        {
            this.LogEntries.Clear();
            if (this.UiContext != null)
            {
                this.UiContext.Post(_ => this.LogEntriesBindingList.Clear(), null);
                this.UiContext.Post(_ => this.FilteredLogEntriesBindingList.Clear(), null);
            }
            else
            {
                lock (this.LogEntriesBindingList)
                {
                    this.LogEntriesBindingList.Clear();
                }
                lock (this.FilteredLogEntriesBindingList)
                {
                    this.FilteredLogEntriesBindingList.Clear();
                }
            }
        }

        private void PruneOldRepositoryLogs(string? directory = null)
        {
            directory ??= this.Settings.SaveToRepositoryCustomFilePath is not null ? Path.GetDirectoryName(this.Settings.SaveToRepositoryCustomFilePath) ?? this.ResolveRepositoryDirectory() : this.ResolveRepositoryDirectory();

            try
            {
                FileInfo[] files = new DirectoryInfo(directory)
                    .GetFiles("AggregatedLog_*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToArray();

                foreach (FileInfo file in files.Skip(this.Settings.MaxRepositoryLogFiles))
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


        private bool ShouldEchoToConsole(string logEntry)
        {
            if (this.Settings.Silent)
            {
                return false;
            }

            if (this.Settings.EchoToConsole == true)
            {
                return true;
            }
            else
            {
                return this.Settings.EchoToConsole == false
                    ? false
                    : this.Settings.EchoToConsoleKeyPhrases.Any(phrase => logEntry.Contains(phrase, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void RaiseLogWritten(DateTime timestamp, string line)
        {
            try
            {
                LogWritten?.Invoke(timestamp, line);
                this._logEntriesRingBufferCounter++;
            }
            catch
            {
            }
        }

        private void EnsureMaxLogEntriesWithOffloadAndBuffering()
        {
            int maxVal = this.Settings.MaxLogEntries ?? int.MaxValue;
            if (maxVal <= this.LogEntries.Count)
            {
                bool? error = null;
                switch (this.Settings.UseRingBuffer)
                {
                    case true:
                        {
                            while (maxVal <= this.LogEntries.Count)
                            {
                                var oldestKeyOpt = this.LogEntries.Keys.OrderBy(k => k).FirstOrDefault();
                                if (oldestKeyOpt != default)
                                {
                                    error = !this.LogEntries.TryRemove(oldestKeyOpt, out _);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            break;
                        }
                    case false:
                        {
                            this.SaveToRepository();
                            this.LogEntries.Clear();
                            this.LogEntriesBindingList.Clear();
                            this.FilteredLogEntriesBindingList.Clear();
                            int? fileIndex = int.TryParse(Path.GetFileNameWithoutExtension(this.LogFilePath)?.Split('_').Last().Trim(), out var index) ? index : null;
                            this.LogFilePath = Path.GetFullPath(this.LogFilePath?.TrimEnd('_') + "_" + (fileIndex.HasValue ? (fileIndex.Value + 1).ToString() : "0"));
                            break;
                        }
                    case null:
                        {
                            while (maxVal <= this.LogEntries.Count)
                            {
                                var oldestEntry = this.LogEntries.Keys.OrderBy(k => k).FirstOrDefault();
                                if (oldestEntry != default)
                                {
                                    error = !this.LogEntries.TryRemove(oldestEntry, out _);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (this._logEntriesRingBufferCounter >= maxVal)
                            {
                                this.SaveToRepository();
                                this._logEntriesRingBufferCounter = 0;
                                int? fileIndexNull = int.TryParse(Path.GetFileNameWithoutExtension(this.LogFilePath)?.Split('_').Last().Trim(), out var indexNull) ? indexNull : null;
                                this.LogFilePath = Path.GetFullPath(this.LogFilePath?.TrimEnd('_') + "_" + (fileIndexNull.HasValue ? (fileIndexNull.Value + 1).ToString() : "0"));
                            }
                            break;
                        }
                }
                if (error == true)
                {
                    this.LogWarning($"[WARN] Failed to remove the oldest log entry while ensuring max log entries. Current count: {this.LogEntries.Count}, Max allowed: {maxVal}");
                }
            }
        }

    }
}