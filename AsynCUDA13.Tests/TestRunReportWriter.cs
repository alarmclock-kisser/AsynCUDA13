using System.Text;
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
        // Statischer Puffer für gesammelte Testergebnisse
        private static readonly List<TestResultEntry> _collectedResults = new();
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
                if (_reportWritten) return;
                _reportWritten = true;

                TestResultEntry[] failedTests;

                // TRX-Datei suchen
                var trxFiles = FindTrxFiles();

                if (trxFiles.Count > 0)
                {
                    // Neueste TRX-Datei verwenden
                    var latestTrx = trxFiles
                        .Select(f => new { Path = f, Time = new FileInfo(f).LastWriteTime })
                        .OrderByDescending(x => x.Time)
                        .First().Path;

                    var trxContent = File.ReadAllText(latestTrx);
                    var doc = XDocument.Parse(trxContent);

                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    var testEntries = doc.Root?.Descendants(ns + "UnitTestResult") ?? Enumerable.Empty<XElement>();

                    failedTests = testEntries
                        .Where(e => e.Attribute("outcome")?.Value == "Failed")
                        .Select(e =>
                        {
                            var testName = e.Attribute("testName")?.Value ?? "Unknown";
                            // fullName ist kein Attribut — extrahiere Klasse aus testName oder aus ErrorInfo
                            var fullyQualifiedName = e.Attribute("testId")?.Value ?? "Unknown";
                            var output = e.Element(ns + "Output");
                            var errorInfo = output?.Element(ns + "ErrorInfo");
                            var message = errorInfo?.Element(ns + "Message")?.Value
                                ?? output?.Element(ns + "ErrorMessage")?.Value
                                ?? string.Empty;
                            var stackTrace = errorInfo?.Element(ns + "StackTrace")?.Value
                                ?? output?.Element(ns + "StackTrace")?.Value
                                ?? string.Empty;
                            // Extrahiere Klassenname aus der Message (z.B. "AsynCUDA13.Tests.Api.CudaMemoryControllerTests.PushAsync...")
                            var className = "Unknown";
                            if (message.Contains("AsynCUDA13.Tests"))
                            {
                                var idx = message.IndexOf("AsynCUDA13.Tests");
                                var dotIdx = message.IndexOf('.', idx + 16);
                                if (dotIdx > 0)
                                {
                                    className = message.Substring(idx, dotIdx - idx);
                                }
                            }
                            return new TestResultEntry
                            {
                                TestName = testName,
                                FullyQualifiedName = className,
                                ErrorMessage = message,
                                StackTrace = stackTrace,
                            };
                        })
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

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                    sb.AppendLine($"Fehler: {result.ErrorMessage}");

                if (!string.IsNullOrEmpty(result.StackTrace))
                    sb.AppendLine($"Stacktrace: {result.StackTrace}");

                sb.AppendLine(new string('-', 40));
            }

            File.WriteAllText(filePath, sb.ToString());
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
            var testEntries = doc.Root?.Descendants(ns + "UnitTestResult") ?? Enumerable.Empty<XElement>();

            return testEntries
                .Where(e => e.Attribute("outcome")?.Value == "Failed")
                .Select(e => new TestResultEntry
                {
                    TestName = e.Attribute("testName")?.Value ?? "Unknown",
                    FullyQualifiedName = e.Attribute("fullName")?.Value ?? "Unknown",
                    ErrorMessage = e.Element(ns + "Output")?.Element(ns + "ErrorMessage")?.Value ?? string.Empty,
                    StackTrace = e.Element(ns + "Output")?.Element(ns + "StackTrace")?.Value ?? string.Empty,
                })
                .ToArray();
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
            if (_initialized) return;

            lock (_lockObj)
            {
                if (_initialized) return;
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
    }
}