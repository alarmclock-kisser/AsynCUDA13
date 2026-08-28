using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsynCUDA13.Tests
{
    /// <summary>
    /// Sammelt Testergebnisse und schreibt nach dem Testlauf eine .txt-Datei
    /// mit allen fehlgeschlagenen Tests in TestRunReports/.
    /// 
    /// Strategie: Statischer Puffer + AssemblyCleanup + Hintergrund-Thread Fallback.
    /// </summary>
    [TestClass]
    public class TestRunReportWriter
    {
        // Maximale Anzahl der FailedTests-Report-Dateien, die behalten werden
        private const int MaxReportFiles = 10;

        // Statischer Puffer für gesammelte Testergebnisse
        private static readonly List<TestResultEntry> _collectedResults = [];
        private static readonly object _lockObj = new();
        private static bool _reportWritten = false;
        private static volatile bool _initialized = false;
        private static Thread? _fallbackThread;

        /// <summary>
        /// Einfaches Datenmodell für ein Testergebnis.
        /// </summary>
        private sealed class TestResultEntry
        {
            public string TestName { get; set; } = string.Empty;
            public string FullyQualifiedName { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public string StackTrace { get; set; } = string.Empty;
        }

        /// <summary>
        /// Tests können diese statische Methode aufrufen, um Ergebnisse zu sammeln.
        /// </summary>
        public static void RecordResult(string testName, string fullyQualifiedName, string? errorMessage = null, string? stackTrace = null)
        {
            EnsureFallbackStarted();
            lock (_lockObj)
            {
                _collectedResults.Add(new TestResultEntry
                {
                    TestName = testName,
                    FullyQualifiedName = fullyQualifiedName,
                    ErrorMessage = errorMessage ?? string.Empty,
                    StackTrace = stackTrace ?? string.Empty,
                });
            }
        }

        /// <summary>
        /// HAUPTPFAD: Liest TRX-Datei und schreibt Report.
        /// Fallback: Nutzt den statischen Puffer wenn keine TRX gefunden wird.
        /// </summary>
        private static void WriteReportFromTrx()
        {
            lock (_lockObj)
            {
                if (_reportWritten)
                {
                    return;
                }

                _reportWritten = true;

                TestResultEntry[] failedTests;

                // TRX-Datei suchen
                var trxFiles = FindTrxFiles();

                if (trxFiles.Count > 0)
                {
                    // ALLE TRX-Dateien analysieren, um alle fehlgeschlagenen Tests zu sammeln
                    var allFailedTests = new List<TestResultEntry>();

                    // Sortiere TRX-Dateien nach Änderungsdatum (neueste zuerst) für bessere Berichterstattung
                    var sortedTrxFiles = trxFiles
                        .Select(f => new { Path = f, Time = new FileInfo(f).LastWriteTime })
                        .OrderByDescending(x => x.Time)
                        .ToList();

                    foreach (var trxFile in sortedTrxFiles)
                    {
                        try
                        {
                            var trxContent = File.ReadAllText(trxFile.Path);
                            var doc = XDocument.Parse(trxContent);

                            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                            var testEntries = doc.Root?.Descendants(ns + "UnitTestResult") ?? [];

                            // Baue eine Lookup-Tabelle für TestDefinitions (testId -> className)
                            var testDefinitions = doc.Root?.Descendants(ns + "UnitTest") ?? [];
                            var testClassLookup = new Dictionary<string, string>();

                            foreach (var unitTest in testDefinitions)
                            {
                                var testId = unitTest.Attribute("id")?.Value;
                                var testMethod = unitTest.Element(ns + "TestMethod");
                                var className = testMethod?.Attribute("className")?.Value;

                                if (!string.IsNullOrEmpty(testId) && !string.IsNullOrEmpty(className))
                                {
                                    testClassLookup[testId] = className;
                                }
                            }

                            var failedFromThisTrx = testEntries
                                .Where(e => e.Attribute("outcome")?.Value == "Failed")
                                .Select(e =>
                                {
                                    var testName = e.Attribute("testname")?.Value ?? e.Attribute("testName")?.Value ?? "Unknown";
                                    var testId = e.Attribute("testid")?.Value ?? e.Attribute("testId")?.Value ?? string.Empty;

                                    // Versuche, die Klasse aus den TestDefinitions zu finden
                                    var fullyQualifiedName = testClassLookup.TryGetValue(testId, out var className)
                                        ? className
                                        : ExtractTestClassFromStackTrace(e, ns);

                                    var output = e.Element(ns + "Output");
                                    var errorInfo = output?.Element(ns + "ErrorInfo");
                                    var stackTrace = errorInfo?.Element(ns + "StackTrace")?.Value
                                        ?? output?.Element(ns + "StackTrace")?.Value
                                        ?? string.Empty;

                                    // Versuche, die Fehlermeldung aus dem Message-Element zu extrahieren
                                    var message = errorInfo?.Element(ns + "Message")?.Value
                                        ?? output?.Element(ns + "ErrorMessage")?.Value
                                        ?? string.Empty;

                                    // Wenn die Fehlermeldung nicht ausreichend ist, extrahiere sie aus dem Stacktrace
                                    if (string.IsNullOrEmpty(message) || message.Contains("Test failed"))
                                    {
                                        message = ExtractErrorMessageFromStackTrace(stackTrace);
                                    }

                                    return new TestResultEntry
                                    {
                                        TestName = testName,
                                        FullyQualifiedName = fullyQualifiedName,
                                        ErrorMessage = message,
                                        StackTrace = stackTrace,
                                    };
                                })
                                .ToList();

                            allFailedTests.AddRange(failedFromThisTrx);
                        }
                        catch (Exception ex)
                        {
                            // TRX-Datei konnte nicht gelesen werden - überspringen
                            Console.WriteLine($"Warnung: TRX-Datei '{trxFile.Path}' konnte nicht analysiert werden: {ex.Message}");
                        }
                    }

                    // Entferne Duplikate basierend auf TestName und FullyQualifiedName
                    failedTests = allFailedTests
                        .GroupBy(t => new { t.TestName, t.FullyQualifiedName })
                        .Select(g => g.First())
                        .ToArray();
                }
                else
                {
                    // Fallback: Statischer Puffer
                    lock (_lockObj)
                    {
                        failedTests = _collectedResults
                            .Where(r => !string.IsNullOrEmpty(r.ErrorMessage))
                            .ToArray();
                    }
                }

                if (failedTests.Length == 0)
                {
                    WriteSuccessReport();
                }
                else
                {
                    WriteFailedReport(failedTests);
                }
            }
        }

        private static void WriteSuccessReport()
        {
            var reportsDir = GetReportsDir();
            Directory.CreateDirectory(reportsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"TestReport_{timestamp}.txt";
            var filePath = Path.Combine(reportsDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== Test Report ===");
            sb.AppendLine($"Datum: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("Alle Tests erfolgreich — keine Fehler gefunden.");
            sb.AppendLine(new string('=', 60));

            File.WriteAllText(filePath, sb.ToString());
        }

        /// <summary>
        /// Extrahiert eine aussagekräftige Fehlermeldung aus dem Stacktrace.
        /// </summary>
        private static string ExtractErrorMessageFromStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return string.Empty;
            }

            var lines = stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            // Sammle alle Zeilen, die zur Fehlermeldung gehören
            var messageLines = new List<string>();
            bool inMessage = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Starte die Fehlermeldung bei "Shouldly.ShouldAssertException:"
                if (trimmedLine.StartsWith("Shouldly.ShouldAssertException:", StringComparison.OrdinalIgnoreCase))
                {
                    inMessage = true;
                    var message = trimmedLine.Substring("Shouldly.ShouldAssertException:".Length).Trim();
                    messageLines.Add(message);
                }
                else if (inMessage)
                {
                    // Prüfe auf Fortsetzungen der Fehlermeldung
                    if (trimmedLine.StartsWith("at ") || trimmedLine.StartsWith("   at "))
                    {
                        // Ende der Fehlermeldung
                        break;
                    }
                    else
                    {
                        // Fortsetzung der Fehlermeldung (mehrzeilige Nachricht)
                        messageLines.Add(trimmedLine);
                    }
                }
            }

            if (messageLines.Count > 0)
            {
                return string.Join(" ", messageLines.Select(l => l.Trim()));
            }

            return string.Empty;
        }

        private static void WriteFailedReport(TestResultEntry[] failedTests)
        {
            var reportsDir = GetReportsDir();
            Directory.CreateDirectory(reportsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"FailedTests_{timestamp}.txt";
            var filePath = Path.Combine(reportsDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== Fehlgeschlagene Tests ===");
            sb.AppendLine($"Datum: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Anzahl: {failedTests.Length}");
            sb.AppendLine(new string('=', 60));

            foreach (var result in failedTests)
            {
                sb.AppendLine();
                sb.AppendLine($"Test: {result.TestName}");
                sb.AppendLine($"Klasse: {result.FullyQualifiedName}");

                // Erstelle eine aussagekräftige Fehlermeldung
                var displayError = CreateErrorMessage(result.ErrorMessage, result.StackTrace);
                sb.AppendLine($"Fehler: {displayError}");

                if (!string.IsNullOrEmpty(result.StackTrace))
                {
                    var assertInfo = ExtractAssertInfoFromStackTrace(result.StackTrace);
                    if (!string.IsNullOrEmpty(assertInfo))
                    {
                        sb.AppendLine($"Assert: {assertInfo}");
                    }
                    sb.AppendLine("Stacktrace:");
                    sb.AppendLine(result.StackTrace.Trim());
                }

                sb.AppendLine(new string('-', 40));
            }

            File.WriteAllText(filePath, sb.ToString());

            // Bereinige alte Reports
            CleanupOldReports(reportsDir);
        }

        /// <summary>
        /// Erstellt eine aussagekräftige Fehlermeldung aus ErrorMessage und Stacktrace.
        /// Versucht, die Assert-Information aus dem Stacktrace zu extrahieren.
        /// </summary>
        private static string CreateErrorMessage(string? errorMessage, string? stackTrace)
        {
            // Wenn bereits eine spezifische Fehlermeldung vorhanden ist, verwende sie
            if (!string.IsNullOrEmpty(errorMessage) && !errorMessage.Contains("Test failed"))
            {
                return errorMessage.Trim();
            }

            // Versuche, die Assert-Information aus dem Stacktrace zu extrahieren
            if (!string.IsNullOrEmpty(stackTrace))
            {
                var assertInfo = ExtractAssertInfoFromStackTrace(stackTrace);
                if (!string.IsNullOrEmpty(assertInfo))
                {
                    return assertInfo;
                }

                // Falls keine Shouldly-Affirmation gefunden, extrahiere die Test-Assertion
                var testAssertInfo = ExtractTestAssertionInfo(stackTrace);
                if (!string.IsNullOrEmpty(testAssertInfo))
                {
                    return testAssertInfo;
                }
            }

            // Fallback: Zeige die Fehlermeldung aus dem Stacktrace an
            if (!string.IsNullOrEmpty(errorMessage))
            {
                return errorMessage.Trim();
            }

            return "Test failed";
        }

        /// <summary>
        /// Extrahiert die Test-Assertion-Information aus dem Stacktrace.
        /// </summary>
        private static string? ExtractTestAssertionInfo(string stackTrace)
        {
            var lines = stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Suche nach der Testmethode im Stacktrace
                var atMatch = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"at\s+([\w\.]+)\s*\(",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (atMatch.Success)
                {
                    var className = atMatch.Groups[1].Value;

                    // Extrahiere Datei und Zeilennummer
                    var fileMatch = System.Text.RegularExpressions.Regex.Match(
                        line,
                        @"in\s+(.+?)\s*:\s*line\s+(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (fileMatch.Success)
                    {
                        var filePath = fileMatch.Groups[1].Value;
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        var lineNumber = fileMatch.Groups[2].Value;
                        return $"Assertion in {className} in {fileName}.cs Zeile {lineNumber}";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extrahiert die Assert-Information aus dem Stacktrace (z.B. "ShouldBe", "ShouldNotBeNull", etc.)
        /// </summary>
        private static string? ExtractAssertInfoFromStackTrace(string stackTrace)
        {
            var lines = stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            // Suche nach Shouldly-Affirmationen im Stacktrace
            foreach (var line in lines)
            {
                // Shouldly-Affirmationen extrahieren (z.B. "ShouldBe", "ShouldNotBeNull", "ShouldNotBeEmpty")
                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"(\w+)\s*\(\s*\)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var methodName = match.Groups[1].Value;
                    if (methodName.StartsWith("Should", StringComparison.OrdinalIgnoreCase))
                    {
                        // Versuche den Kontext zu extrahieren
                        var contextMatch = System.Text.RegularExpressions.Regex.Match(
                            line,
                            @"at\s+([\w\.]+)\s*\(",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        if (contextMatch.Success)
                        {
                            // Extrahiere Datei und Zeilennummer
                            var fileMatch = System.Text.RegularExpressions.Regex.Match(
                                line,
                                @"in\s+(.+?)\s*:\s*line\s+(\d+)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                            if (fileMatch.Success)
                            {
                                var filePath = fileMatch.Groups[1].Value;
                                var fileName = Path.GetFileNameWithoutExtension(filePath);
                                var lineNumber = fileMatch.Groups[2].Value;
                                return $"{methodName}() in {fileName}.cs Zeile {lineNumber}";
                            }
                            return $"{methodName}() in {contextMatch.Groups[1].Value}";
                        }
                        return $"{methodName}()";
                    }
                }
            }

            // Falls keine Shouldly-Affirmation gefunden, versuche die Testmethode zu extrahieren
            foreach (var line in lines)
            {
                var atMatch = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"at\s+([\w\.]+)\s*\(",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (atMatch.Success)
                {
                    var className = atMatch.Groups[1].Value;

                    // Extrahiere Datei und Zeilennummer
                    var fileMatch = System.Text.RegularExpressions.Regex.Match(
                        line,
                        @"in\s+(.+?)\s*:\s*line\s+(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (fileMatch.Success)
                    {
                        var filePath = fileMatch.Groups[1].Value;
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        var lineNumber = fileMatch.Groups[2].Value;
                        return $"Test-Assertion in {className} in {fileName}.cs Zeile {lineNumber}";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sucht TRX-Dateien in allen TestResults-Verzeichnissen.
        /// </summary>
        private static List<string> FindTrxFiles()
        {
            var trxFiles = new List<string>();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Alle Parent-Verzeichnisse nach TestResults durchsuchen
            var current = new DirectoryInfo(baseDir);
            while (current != null)
            {
                var testResultsDir = Path.Combine(current.FullName, "TestResults");
                if (Directory.Exists(testResultsDir))
                {
                    trxFiles.AddRange(Directory.GetFiles(testResultsDir, "*.trx", SearchOption.AllDirectories));
                }
                current = current.Parent;
            }

            // 2. Im bin-Ordner suchen (Fallback)
            trxFiles.AddRange(Directory.GetFiles(baseDir, "*.trx", SearchOption.AllDirectories));

            // 3. Solution-Root via .slnx/.sln finden und TestResults dort suchen
            var solutionRoot = FindSolutionRoot();
            if (!string.IsNullOrEmpty(solutionRoot))
            {
                var trxDir = Path.Combine(solutionRoot, "TestResults");
                if (Directory.Exists(trxDir))
                {
                    trxFiles.AddRange(Directory.GetFiles(trxDir, "*.trx", SearchOption.AllDirectories));
                }
            }

            return trxFiles.Distinct().ToList();
        }

        /// <summary>
        /// Ermittelt das Solution-Root-Verzeichnis.
        /// </summary>
        private static string? FindSolutionRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (Directory.GetFiles(current.FullName, "*.sln").Length > 0 ||
                    Directory.GetFiles(current.FullName, "*.slnx").Length > 0)
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            return null;
        }

        /// <summary>
        /// Liest eine TRX-Datei und extrahiert fehlgeschlagene Tests.
        /// </summary>
        private static TestResultEntry[] ParseTrxFile(string trxPath)
        {
            var trxContent = File.ReadAllText(trxPath);
            var doc = XDocument.Parse(trxContent);

            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var testEntries = doc.Root?.Descendants(ns + "UnitTestResult") ?? [];

            // Baue eine Lookup-Tabelle für TestDefinitions (testId -> className)
            var testDefinitions = doc.Root?.Descendants(ns + "UnitTest") ?? [];
            var testClassLookup = new Dictionary<string, string>();

            foreach (var unitTest in testDefinitions)
            {
                var testId = unitTest.Attribute("id")?.Value;
                var testMethod = unitTest.Element(ns + "TestMethod");
                var className = testMethod?.Attribute("className")?.Value;

                if (!string.IsNullOrEmpty(testId) && !string.IsNullOrEmpty(className))
                {
                    testClassLookup[testId] = className;
                }
            }

            return testEntries
                .Where(e => e.Attribute("outcome")?.Value == "Failed")
                .Select(e =>
                {
                    var testName = e.Attribute("testname")?.Value ?? e.Attribute("testName")?.Value ?? "Unknown";
                    var testId = e.Attribute("testid")?.Value ?? e.Attribute("testId")?.Value ?? string.Empty;

                    // Versuche, die Klasse aus den TestDefinitions zu finden
                    var fullyQualifiedName = testClassLookup.TryGetValue(testId, out var className)
                        ? className
                        : ExtractTestClassFromStackTrace(e, ns);

                    var output = e.Element(ns + "Output");
                    var errorInfo = output?.Element(ns + "ErrorInfo");
                    var stackTrace = errorInfo?.Element(ns + "StackTrace")?.Value
                        ?? output?.Element(ns + "StackTrace")?.Value
                        ?? string.Empty;

                    // Versuche, die Fehlermeldung aus dem Message-Element zu extrahieren
                    var message = errorInfo?.Element(ns + "Message")?.Value
                        ?? output?.Element(ns + "ErrorMessage")?.Value
                        ?? string.Empty;

                    // Wenn die Fehlermeldung nicht ausreichend ist, extrahiere sie aus dem Stacktrace
                    if (string.IsNullOrEmpty(message) || message.Contains("Test failed"))
                    {
                        message = ExtractErrorMessageFromStackTrace(stackTrace);
                    }

                    return new TestResultEntry
                    {
                        TestName = testName,
                        FullyQualifiedName = fullyQualifiedName,
                        ErrorMessage = message,
                        StackTrace = stackTrace,
                    };
                })
                .ToArray();
        }

        /// <summary>
        /// Extrahiert die Testklasse aus dem Stacktrace (Fallback-Methode).
        /// </summary>
        private static string ExtractTestClassFromStackTrace(XElement element, XNamespace ns)
        {
            var stackTrace = element.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "StackTrace")?.Value
                ?? element.Element(ns + "Output")?.Element(ns + "StackTrace")?.Value;

            if (string.IsNullOrEmpty(stackTrace))
            {
                return "Unknown";
            }

            // Suche nach der ersten "at" Zeile im Stacktrace, die die Testklasse enthält
            var lines = stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"at\s+([\w\.]+)\s*\(", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var className = match.Groups[1].Value;
                    // Filtere bekannte Systemklassen heraus
                    if (!className.StartsWith("System.") &&
                        !className.StartsWith("Microsoft.") &&
                        !className.StartsWith("ManagedCuda"))
                    {
                        return className;
                    }
                }
            }

            return "Unknown";
        }

        /// <summary>
        /// Gibt den Pfad zum TestRunReports-Verzeichnis zurück.
        /// </summary>
        private static string GetReportsDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Suche nach TestRunReports im Solution-Root oder im Projektordner
            var current = new DirectoryInfo(baseDir);
            while (current != null)
            {
                var reportsDir = Path.Combine(current.FullName, "AsynCUDA13.Tests", "TestRunReports");
                if (Directory.Exists(reportsDir) || current.Name == "AsynCUDA13.Tests")
                {
                    return Path.Combine(current.FullName, "TestRunReports");
                }
                current = current.Parent;
            }
            return Path.Combine(baseDir, "TestRunReports");
        }

        /// <summary>
        /// Startet den Hintergrund-Thread als Fallback.
        /// </summary>
        private static void EnsureFallbackStarted()
        {
            if (_initialized)
            {
                return;
            }

            lock (_lockObj)
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;

                _fallbackThread = new Thread(() =>
                {
                    // Fallback: Warte 30 Sekunden und schreibe Report falls AssemblyCleanup nicht gelaufen ist
                    Thread.Sleep(30000);
                    WriteReportFromTrx();
                })
                {
                    IsBackground = true,
                    Name = "TestRunReportFallback"
                };
                _fallbackThread.Start();
            }
        }

        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            EnsureFallbackStarted();
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            // Kurze Pause damit alle TestCleanup-Calls den Puffer gefüllt haben
            Thread.Sleep(2000);
            WriteReportFromTrx();
        }

        /// <summary>
        /// Bereinigt alte FailedTests-Report-Dateien, indem ältere Dateien gelöscht werden,
        /// wenn mehr als MaxReportFiles Dateien vorhanden sind.
        /// </summary>
        private static void CleanupOldReports(string reportsDir)
        {
            try
            {
                var reportFiles = new DirectoryInfo(reportsDir)
                    .GetFiles("FailedTests_*.txt")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(MaxReportFiles)
                    .ToList();

                foreach (var file in reportFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        // Ignoriere Fehler beim Löschen alter Dateien
                        System.Diagnostics.Debug.WriteLine($"Fehler beim Löschen alter Report-Datei {file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignoriere Fehler beim Bereinigen
                System.Diagnostics.Debug.WriteLine($"Fehler beim Bereinigen alter Reports: {ex.Message}");
            }
        }
    }
}