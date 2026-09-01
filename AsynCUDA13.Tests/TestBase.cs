using System.IO;
using System.Linq;
using System.Xml.Linq;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsynCUDA13.Tests
{
    /// <summary>
    /// Basisklasse für alle Tests. Sammelt automatisch Testergebnisse
    /// für den TestRunReportWriter.
    /// </summary>
    public abstract class TestBase
    {
        protected readonly IRollingFileMemoryLogger Logger = new RollingFileMemoryLogger(
            new RollingFileMemoryLoggerOptions { Silent = true });

        private TestContext? _testContext;
        private Exception? _lastException;

        /// <summary>
        /// Gets the test context.
        /// </summary>
        public TestContext TestContext
        {
            get { return this._testContext ?? throw new InvalidOperationException("TestContext has not been initialized."); }
            set { this._testContext = value; }
        }

        protected static T Require<T>(T? value, string? message = null) where T : class
        {
            Assert.IsNotNull(value, message);
            return value;
        }

        /// <summary>
        /// Kann von abgeleiteten Tests aufgerufen werden, um eine Exception explizit zu melden.
        /// </summary>
        protected void SetLastException(Exception ex) => this._lastException = ex;

        /// <summary>
        /// Wird nach jedem Test aufgerufen und meldet das Ergebnis an TestRunReportWriter.
        /// </summary>
        [TestCleanup]
        public void ReportTestResult()
        {
            if (this._testContext == null)
            {
                return;
            }

            var testName = this._testContext.TestName;
            var className = this._testContext.FullyQualifiedTestClassName
                ?? this.GetType().FullName
                ?? "Unknown";

            // Nur fehlgeschlagene Tests melden
            if (this._testContext.CurrentTestOutcome == UnitTestOutcome.Failed)
            {
                // Versuche, die Fehlermeldung direkt aus dem Test-Result zu extrahieren
                var errorMessage = this.GetErrorMessageFromTestResult();
                var stackTrace = this.GetStackTraceFromTestResult();

                // Fallback auf gespeicherte Exception
                if (string.IsNullOrEmpty(errorMessage) && this._lastException != null)
                {
                    errorMessage = this._lastException.Message;
                    stackTrace = this._lastException.StackTrace ?? string.Empty;
                }

                // Letzte Reserve
                if (string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = $"Test failed (outcome: {this._testContext.CurrentTestOutcome})";
                }

                TestRunReportWriter.RecordResult(
                    testName,
                    className,
                    errorMessage,
                    stackTrace);
            }
        }

        private string GetErrorMessageFromTestResult()
        {
            // Versuche, die Fehlermeldung aus der TRX-Datei zu extrahieren
            var trxFiles = this.FindTrxFilesForCurrentTest();
            if (trxFiles.Any())
            {
                var latestTrx = trxFiles.OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();
                if (latestTrx != null)
                {
                    try
                    {
                        var trxContent = File.ReadAllText(latestTrx);
                        var doc = XDocument.Parse(trxContent);
                        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                        var testEntry = doc.Root?.Descendants(ns + "UnitTestResult")
                            .FirstOrDefault(e => e.Attribute("testName")?.Value == this._testContext?.TestName);

                        if (testEntry != null)
                        {
                            var output = testEntry.Element(ns + "Output");
                            var errorInfo = output?.Element(ns + "ErrorInfo");
                            var message = errorInfo?.Element(ns + "Message")?.Value
                                ?? output?.Element(ns + "ErrorMessage")?.Value
                                ?? string.Empty;

                            if (!string.IsNullOrEmpty(message))
                            {
                                return message;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback wenn TRX-Datei nicht gelesen werden kann
                    }
                }
            }
            return string.Empty;
        }

        private string GetStackTraceFromTestResult()
        {
            // Versuche, den Stacktrace aus der TRX-Datei zu extrahieren
            var trxFiles = this.FindTrxFilesForCurrentTest();
            if (trxFiles.Any())
            {
                var latestTrx = trxFiles.OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();
                if (latestTrx != null)
                {
                    try
                    {
                        var trxContent = File.ReadAllText(latestTrx);
                        var doc = XDocument.Parse(trxContent);
                        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                        var testEntry = doc.Root?.Descendants(ns + "UnitTestResult")
                            .FirstOrDefault(e => e.Attribute("testName")?.Value == this._testContext?.TestName);

                        if (testEntry != null)
                        {
                            var output = testEntry.Element(ns + "Output");
                            var errorInfo = output?.Element(ns + "ErrorInfo");
                            var stackTrace = errorInfo?.Element(ns + "StackTrace")?.Value
                                ?? output?.Element(ns + "StackTrace")?.Value
                                ?? string.Empty;

                            if (!string.IsNullOrEmpty(stackTrace))
                            {
                                return stackTrace;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback wenn TRX-Datei nicht gelesen werden kann
                    }
                }
            }
            return string.Empty;
        }

        private List<string> FindTrxFilesForCurrentTest()
        {
            var trxFiles = new List<string>();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

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

            trxFiles.AddRange(Directory.GetFiles(baseDir, "*.trx", SearchOption.AllDirectories));
            return trxFiles;
        }
    }
}