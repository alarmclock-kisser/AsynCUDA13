# 📄 RollingFileMemoryLogger (RoFL) Documentation  

*The definitive guide to the **RollingFileMemoryLogger** – a lightweight, thread‑safe, in‑memory logger that can also write to a file, expose UI‑friendly `BindingList`s, and persist logs to a repository.*  
---  

## Table of Contents  

1. [Overview](#overview)  
2. [Key Features](#key-features)  
3. [Package Structure](#package-structure)  
4. [Configuration – `RollingFileMemoryLoggerOptions`](#configuration---rollingfilememoryloggeroptions)  
5. [Getting Started – Minimal Example](#getting-started--minimal-example)  
6. [Core API](#core-api)  
   - [Public Members](#public-members)  
   - [Events](#events)  
   - [Methods Overview](#methods-overview)  
7. [Logging Scenarios](#logging-scenarios)  
   - [Simple Text Logging](#simple-text-logging)  
   - [Exception Logging](#exception-logging)  
   - [Adding Comments](#adding-comments)  
   - [Asynchronous Logging](#asynchronous-logging)  
8. [UI Integration](#ui-integration)  
9. [Saving to Repository](#saving-to-repository)  
10. [Advanced Scenarios & Tips](#advanced-scenarios--tips)  
11. [Performance & Thread‑Safety Considerations](#performance--thread‑safety-considerations)  
12. [Testing & Mocking](#testing--mocking)  
13. [FAQ](#faq)  
14. [License](#license)  

---  

## Overview <a name="overview"></a>  

`RollingFileMemoryLogger` (often abbreviated **RoFL**) is a **generic‑ready**, **non‑intrusive** logger designed for .NET applications that need:  

* **In‑memory buffering** – a fast, lock‑free collection of log entries.  
* **UI‑friendly binding lists** – `BindingList<string>` that can be bound to WPF/WinForms/MAUI controls.  
* **File persistence** – optional writing to a timestamped text file (or multiple rotated files).  
* **Repository mode** – persisting a snapshot of the current logs to a directory (e.g., for debugging or audit trails).  
* **Thread‑safe, async‑friendly API** – all public members can be called from any thread, including UI threads.  

The logger is deliberately small (≈ 400 lines) but packed with helper methods, fluent configuration, and a clean separation of concerns via the **`IRollingFileMemoryLogger`** interface.  

---  

## Key Features <a name="key-features"></a>  

| Feature | What it does | Typical use‑case |
|--------|--------------|------------------|
| **In‑memory dictionary** (`LogEntries`) | Stores `DateTime → formatted log line` pairs. | Fast lookup, roll‑forward/roll‑back, UI refresh. |
| **Binding lists** (`LogEntriesBindingList`, `FilteredLogEntriesBindingList`) | `BindingList<string>` that raises change notifications. | Bind directly to `DataGrid`, `ListView`, or custom controls. |
| **Filtering** (`Settings.FilterPhrase`) | Simple `Contains` filter (case‑insensitive). | Show only entries that contain a keyword. |
| **Console echo** (`Settings.EchoToConsole`, `EchoToConsoleKeyPhrases`) | Writes to `stdout` when enabled. | Quick debugging on the console. |
| **File persistence** (`LogFilePath`, `_logChannel`) | Asynchronously writes to a file via a bounded channel. | Persistent logs without blocking the app. |
| **Repository mode** (`SaveToRepository`, `ConfigureSaveToRepository`) | Snapshot the current log set to a timestamped file in a dedicated folder. | Capture a “snapshot” before shutting down or on user request. |
| **Comment support** (`AddComment`, `AddCommentAsync`) | Adds a special marker (`<!!!>`) anchored to a timestamp. | Tag user‑initiated actions, diagnostics, or “ping”. |
| **Exception helpers** (`Log`, `LogAsync`, `GetInnerExceptionsRecursively`) | Auto‑formats stack traces, inner exceptions, and optional pre‑text. | Centralised error reporting. |
| **UI Synchronization Context** (`GetUiContext`, `SetUiContext`) | Allows UI updates from any thread while staying on the UI thread. | Safe updates from background workers. |
| **Background writer** (`StartBackgroundWriter`) | Consumes the channel and flushes to disk on a dedicated task. | Guarantees async I/O without blocking. |

---  

## Package Structure <a name="package-structure"></a>  

```text
src/
└─ AsynCUDA13/
   └─ Shared/
      ├─ Interfaces/
      │   └─ IRollingFileMemoryLogger.cs
      ├─ Options/
      │   └─ RollingFileMemoryLoggerOptions.cs
      ├─ Utils/
      │   └─ UniversalHelper.cs
      └─ RollingFileMemoryLogger.cs   <-- main implementation
```  

* **`IRollingFileMemoryLogger`** – the public contract.  
* **`RollingFileMemoryLoggerOptions`** – strongly‑typed configuration options (see below).  
* **`UniversalHelper`** – small string‑sanitising utilities used throughout.  
* **`RollingFileMemoryLogger`** – concrete class that implements the interface.  

All files are **netstandard2.0** compatible, making them usable from .NET 6/7/8 console apps, WinForms, WPF, MAUI, Azure Functions, etc.  

---  

## Configuration – `RollingFileMemoryLoggerOptions` <a name="configuration---rollingfilememoryloggeroptions"></a>  

Create an instance (or use the default constructor) and tweak the properties that suit your scenario.  

```csharp
using AsynCUDA13.Shared.Options;

var opts = new RollingFileMemoryLoggerOptions
{
    // Logging behaviour
    EchoToConsole      = true,                     // write to stdout
    EchoToConsoleKeyPhrases = new[] { "ERROR" },   // only echo if contains these phrases
    Silent               = false,                  // suppress console output when true
    LogTimestampFormat   = "HH:mm:ss.fff",           // e.g. "[12:34:56.789] "

    // Repository behaviour
    SaveToRepository    = true,                    // enable snapshot mode
    SaveToRepositoryCustomFilePath = null,         // optional custom path
    SaveToRepositoryFileExtension = ".txt",        // default extension
    MaxRepositoryLogFiles = 10,                    // keep only the newest 10 snapshots

    // Ring‑buffer behaviour (advanced)
    UseRingBuffer       = true,                    // true → drop oldest when MaxLogEntries reached
    MaxLogEntries       = 16384,                   // max entries kept in memory

    // Filtering
    FilterPhrase        = null,                    // e.g. "critical" → only log entries containing this phrase

    // File rotation / naming
    CreateLogFile       = true,                    // create a new file on init
    LogFileBaseName     = "app",                   // prefix for generated file names
    LogFileExtension    = "txt",                   // file extension
    FileTimestampFormat = "yyyy-MM-dd_HH-mm-ss",   // used when CreateLogFile = false
};
```  

> **Tip:** All options have sensible defaults; you only need to set the ones you care about.  

---  

## Getting Started – Minimal Example <a name="getting-started--minimal-example"></a>  

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Options;

// 1️⃣ Create and configure the logger
var loggerOptions = new RollingFileMemoryLoggerOptions
{
    EchoToConsole = true,
    LogTimestampFormat = "HH:mm:ss.fff",
    SaveToRepository = true,
    MaxRepositoryLogFiles = 5,
    FilterPhrase = "demo"
};

var logger = new RollingFileMemoryLogger(loggerOptions);

// 2️⃣ Hook UI context (only needed if you plan to bind to UI controls)
logger.SetUiContext(SynchronizationContext.Current);

// 3️⃣ Start the background writer (must be done before logging)
logger.StartBackgroundWriter(CancellationToken.None);

// 4️⃣ Log some messages
logger.LogInfo("Application started");
logger.Log($"[DEBUG] Current time: {DateTime.Now:O}");
logger.LogWarning("This is a warning");
logger.LogError("Something went wrong!", new InvalidOperationException("Invalid operation"));
logger.AddComment(comment: "User pressed 'Start'");

// 5️⃣ (Optional) Bind to a UI list
// Assuming you have a WPF ListBox named LogListBox:
LogListBox.ItemsSource = logger.LogEntriesBindingList;

// 6️⃣ On shutdown, persist the current snapshot
logger.SetOnShutdownAction(() => logger.SaveToRepository());
// When the app is exiting:
CancellationTokenSource cts = new CancellationTokenSource();
logger.SaveToRepositoryOnShutdown?.Invoke();   // will be called by the token registration
```  

> **Result:** All log lines appear in the console, are stored in `LogEntriesBindingList`, filtered by `"demo"` (if you set `FilterPhrase`), and a snapshot is saved to `Logs/` when the process ends.  

---  

## Core API <a name="core-api"></a>  

### Public Members  

| Member | Type | Description |
|--------|------|-------------|
| `Settings` | `RollingFileMemoryLoggerOptions` | Read‑only configuration object. |
| `LogEntries` | `ConcurrentDictionary<DateTime, string>` | In‑memory dictionary of *timestamp → formatted line*. |
| `LogEntriesBindingList` | `BindingList<string>` | UI‑friendly list of **all** log entries. |
| `FilteredLogEntriesBindingList` | `BindingList<string>` | UI‑friendly list of entries that match `Settings.FilterPhrase`. |
| `LogFilePath` | `string?` | Full path of the currently active log file (or `null` if not created). |
| `LogWritten` | `event Action<DateTime,string>?` | Fires for every logged line (timestamp + formatted text). |
| `SaveToRepositoryOnShutdown` | `Action?` | Action executed on shutdown (default: `SaveToRepository`). |
| `UiContext` | `SynchronizationContext?` | UI thread context for safe UI updates. |
| `LogChannel` | `Channel<string>` | Bounded channel used for async file writing. |
| `_logWriterTask` / `_logCts` | `Task` / `CancellationTokenSource` | Internals of the background writer. |

### Events  

```csharp
public event Action<DateTime, string>? LogWritten;
```  

* Fires **immediately after** a line is added to the in‑memory dictionary.  
* The first argument is the `DateTime` the entry was recorded at, the second is the **exactly formatted** line (including any timestamp prefix).  
* Consumers must be prepared for **cross‑thread** calls; always marshal to the UI context if you touch UI controls.  

### Methods Overview  

| Method | Summary |
|--------|---------|
| `InitializeLogger(...)` | Sets up options, creates the log directory, optionally creates the first log file, and stores the configuration. |
| `Log(string message)` | Formats the message (adds timestamp if configured) and enqueues it for async file writing. |
| `Log(Exception ex, …)` | Logs an exception, optionally with a pre‑text and inner‑exception recursion. |
| `LogAsync(string message, …)` | Async wrapper around `Log`. |
| `AddComment(...)` / `AddCommentAsync(...)` | Adds a special comment anchored to a captured timestamp. |
| `ClearLogs()` | Clears both dictionaries and binding lists. |
| `GetLogLines(bool? returnFilteredLog = false, bool reverseOrder = false)` | Returns the current log lines (filtered or not) as a list. |
| `SaveToRepository(string? differentFilePathOrDirectory = null, …)` | Persists the current log snapshot to a timestamped file. |
| `ConfigureSaveToRepository(...)` | Enables/disables repository mode and sets the custom file path. |
| `StartBackgroundWriter(CancellationToken cancellationToken)` | Starts the async writer that flushes the channel to disk. |
| `SetUiContext(SynchronizationContext? context)` | Stores the UI synchronization context for later UI updates. |
| `GetUiContext(bool copy = false)` | Returns (or copies) the stored UI context. |
| `GetAllLogFilePaths()` | Returns all `*.txt` / `*.log` files in the log directory, newest first. |
| `ResolveRepositoryDirectory(...)` / `ResolveRepositoryLogFilePath(...)` | Helper methods that compute absolute paths for repository files. |
| `GetInnerExceptionsRecursively(Exception ex, …)` | Recursively formats inner exceptions (default: `(Message: …) , (Message: …)`). |
| `ShouldEchoToConsole(string logEntry)` | Decides whether a given line should be echoed to the console. |
| `RaiseLogWritten(DateTime timestamp, string line)` | Internal helper that invokes `LogWritten`. |
| `EnsureMaxLogEntriesWithOffloadAndBuffering()` | Enforces `MaxLogEntries` by dropping the oldest entries or triggering repository save. |

All methods are **thread‑safe**; they either work on immutable data structures (`ConcurrentDictionary`) or protect mutable collections with `lock`/`Channel` primitives.  

---  

## Logging Scenarios <a name="logging-scenarios"></a>  

### Simple Text Logging <a name="simple-text-logging"></a>  

```csharp
logger.Log("Hello, world!");                 // → [12:34:56.789] Hello, world!
logger.LogInfo("Info level message");        // → [INFO] Info level message
logger.LogWarning("Watch out!");             // → [WARN] Watch out!
logger.LogError("Boom!", new Exception());  // → [ERROR] Exception: ...
```  

*The formatted line is automatically added to both `LogEntriesBindingList` and `FilteredLogEntriesBindingList` (if a filter matches).*  

### Exception Logging <a name="exception-logging"></a>  

```csharp
try
{
    DoSomethingRisky();
}
catch (Exception ex)
{
    logger.Log(ex, maxInnerEx: 3, appendStackTrace: true,
                preText: "Critical failure while processing request");
}
```  

*Result:* A multi‑line entry that contains the exception type, message, and optionally the full stack trace.  

### Adding Comments <a name="adding-comments"></a>  

```csharp
// Capture the exact moment the user clicks a button
logger.AddComment(capturedAt: DateTime.Now, 
                  elapsedSince: TimeSpan.Zero, 
                  comment: "User clicked \"Start\" button");

// Asynchronous version (e.g. from a UI event)
await logger.AddCommentAsync(comment: "Saved configuration");
```  

*Comments appear in the log with the `[COMMENT]` prefix and are also added to the binding lists.*  

### Asynchronous Logging <a name="asynchronous-logging"></a>  

```csharp
await logger.LogAsync("Persisting data...", configureAwait: false);
// or
await logger.LogAsync(ex, maxInnerEx: 2, appendStackTrace: true,
                      preText: "Failed to write file", configureAwait: true);
```  

*The `configureAwait` argument lets you control whether the continuation runs on the captured context (useful for UI updates).*  

---  

## UI Integration <a name="ui-integration"></a>  

1. **Capture the UI synchronization context** (usually `SynchronizationContext.Current` on the UI thread).  

   ```csharp
   logger.SetUiContext(SynchronizationContext.Current);
   ```  

2. **Expose the binding list** to your UI component.  

   ```csharp
   // WPF example
   myListBox.SetBinding(ListBox.ItemsSourceProperty, 
       new Binding { Source = logger.LogEntriesBindingList });
   ```  

3. **Filtering** – if you set `Settings.FilterPhrase`, only matching entries appear in `FilteredLogEntriesBindingList`. Bind that list to a separate UI element to show filtered logs.  

4. **Live updates** – because `LogWritten` fires for every new line, you can subscribe to it to update a **rich text box**, **log viewer**, or **real‑time chart**.  

   ```csharp
   logger.LogWritten += (ts, line) =>
   {
       // Marshal onto UI thread if needed
       logger.UiContext?.Post(_ => 
       {
           myLogViewer.AppendText($"{ts:O} {line}{Environment.NewLine}");
       }, null);
   };
   ```  

---  

## Saving to Repository <a name="saving-to-repository"></a>  

### When to use it  

* You want a **snapshot** of the current log set before shutting down.  
* You need an audit trail that can be inspected later (e.g., in CI/CD pipelines).  

### Typical workflow  

```csharp
// 1️⃣ Enable repository mode (once, usually at startup)
logger.ConfigureSaveToRepository(
    configureToggle: true,                     // enable
    subDirOrDifferentPath: null,               // use default "Logs" folder
    maxPreviousLogFiles: 7,                    // keep last 7 snapshots
    onShutdown: null);                         // optional custom shutdown action

// 2️⃣ When you want to persist now:
string savedPath = logger.SaveToRepository(); // returns the full path of the created file

// 3️⃣ On application exit, the registered shutdown action will also call SaveToRepository()
logger.SetOnShutdownAction(() => logger.SaveToRepository());
// Register it with a cancellation token if you have one:
logger.SetOnShutdownAction(() => logger.SaveToRepository(),
                           cancellationToken: appLifetime.ApplicationStopping);
```  

**Result:** A file named something like `app_2024-10-01_12-34-56.txt` is created under the configured repository folder (`<solution root>/Logs/` by default). The file contains a header, entry count, and the full list of log lines (filtered or not, depending on the call).  

---  

## Advanced Scenarios & Tips <a name="advanced-scenarios--tips"></a>  

| Scenario | Tip |
|----------|-----|
| **Rotate logs manually** | Set `CreateLogFile = false` and control the file name via `LogFilePath`. Use `SaveToRepository` to switch to a new file (the logger will automatically rename the current file and create a new one). |
| **Large‑scale production** | Increase `MaxLogEntries` (default 16384) and consider enabling `UseRingBuffer`. The ring‑buffer mode automatically drops the oldest entries when the limit is exceeded. |
| **Custom formatting** | Override `LogTimestampFormat` or `FileTimestampFormat` with any `DateTime.ToString` pattern. The logger validates the format and falls back to `HH:mm:ss.fff` if invalid. |
| **Multi‑project solutions** | Pass a custom `projectName` to `ResolveRepositoryDirectory` or `ResolveRepositoryLogFilePath` to store logs per‑project. |
| **Testing** | Mock `IRollingFileMemoryLogger` by injecting a stub that implements the interface. Because the logger uses only in‑memory collections, you can assert on `LogEntriesBindingList` after a series of `Log` calls. |
| **Performance** | The logger is **CPU‑light** (mostly string concatenation) and **I/O‑light** (single async write per log line). |
| **Avoiding UI deadlocks** | Always use `logger.UiContext?.Post(..., null)` when updating UI controls from a background thread. If you already have a `SynchronizationContext`, you can call `logger.SetUiContext(context)` once at app start. |
| **Disabling console output in production** | Set `EchoToConsole = false` and/or provide an empty `EchoToConsoleKeyPhrases` collection. The logger will then only write to the file (or repository). |

---  

## Performance & Thread‑Safety Considerations <a name="performance--thread‑safety-considerations"></a>  

* **In‑memory dictionary** (`ConcurrentDictionary<DateTime,string>`) provides O(1) add/remove and is safe for concurrent producers (the UI thread, background workers, etc.).  
* **BindingLists** raise change notifications on the thread they were modified on. The logger marshals to the stored `UiContext` (or falls back to the current thread) to avoid cross‑thread exceptions.  
* **Bounded channel** (`System.Threading.Channels.Channel<string>`) caps the number of pending writes (`MaxLogEntries`). When the channel is full, the oldest entry is dropped (`DropOldest`). This prevents unbounded memory growth.  
* **File I/O** occurs on a dedicated `Task` (`_logWriterTask`). The writer flushes after each write (`FlushAsync`) to guarantee that log entries are persisted even if the application crashes.  
* **Exception handling** inside the writer catches `OperationCanceledException` to exit gracefully on shutdown.  

Overall, the logger is **CPU‑light** (mostly string concatenation) and **I/O‑light** (single async write per log line).  

---  

## Testing & Mocking <a name="testing--mocking"></a>  

### Unit‑test example (xUnit)  

```csharp
public class RollingFileMemoryLoggerTests
{
    [Fact]
    public void Log_AddsEntry_And_RaisesLogWritten()
    {
        // Arrange
        var logger = new RollingFileMemoryLogger(new RollingFileMemoryLoggerOptions { Silent = true });
        var captured = new List<(DateTime Ts, string Line)>();
        logger.LogWritten += (ts, line) => captured.Add((ts, line));

        // Act
        logger.Log("Test message");

        // Assert
        Assert.Single(captured);
        Assert.Contains("Test message", captured[0].Line);
        Assert.NotEmpty(logger.LogEntriesBindingList);
    }
}
```  

*Because the logger is self‑contained, you can instantiate it with default options, call `Log`, and inspect the public collections directly.*  

### Mocking the interface  

```csharp
public class FakeLogger : IRollingFileMemoryLogger
{
    public RollingFileMemoryLoggerOptions Settings { get; }
    public string? LogFilePath { get; }
    public Action? SaveToRepositoryOnShutdown { get; set; }
    public event Action<DateTime, string>? LogWritten;
    // Implement the rest as no‑ops or simple in‑memory collections.
}
```  

*Use the fake in integration tests to verify that `SaveToRepository` is called, or that `LogWritten` fires the expected number of times.*  

---  

## FAQ <a name="faq"></a>  

| Question | Answer |
|----------|--------|
| **Do I need to call `Dispose()`?** | No. The logger does not hold unmanaged resources that require explicit disposal. The background writer task is cancelled automatically when the `CancellationToken` passed to `StartBackgroundWriter` is triggered (e.g., during app shutdown). |
| **Can I have multiple loggers simultaneously?** | Yes. Each instance maintains its own channel, dictionaries, and UI context. Just be sure to start the background writer for each logger you intend to persist to file. |
| **What if I need structured logging (JSON, etc.)?** | The logger is intentionally *text‑only*. You can format the message before passing it to `Log(string message)`. For JSON, create a helper method that builds the JSON string and calls `logger.Log(jsonString)`. |
| **Is the logger compatible with .NET 6 minimal APIs?** | Absolutely. You can register `IRollingFileMemoryLogger` as a singleton in the DI container and inject it into controllers, background services, or Razor components. |
| **How does the filter work?** | `Settings.FilterPhrase` is matched against the **final formatted line** (including timestamp). The match is case‑insensitive and uses `String.Contains`. If `null` or empty, no filtering occurs. |
| **Can I change the filter at runtime?** | Yes. Set `logger.Settings.FilterPhrase = newPhrase;` and subsequent log entries will be filtered accordingly. Existing entries remain in both lists. |
| **What happens if the background writer throws?** | The writer catches `OperationCanceledException` for graceful shutdown. Any other exception is logged to the console (via `Console.WriteLine`) and the task stops. The logger continues to function; new entries will simply be dropped if the channel cannot be written to. |

---  

## License <a name="license"></a>  

`RollingFileMemoryLogger` is released under the **MIT License** – see the `LICENSE` file in the repository for details.  

---  

*Happy logging! 🎉*  

---  

*Document generated on 2025‑11‑03 by **MKi** (based on the source located in `src/AsynCUDA13/Shared/RollingFileMemoryLogger.cs`).*