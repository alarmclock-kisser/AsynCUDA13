using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsynCUDA13.Tests
{
    /// <summary>
    /// Basisklasse für alle Tests. Sammelt automatisch Testergebnisse
    /// für den TestRunReportWriter.
    /// </summary>
    public abstract class TestBase
    {
        private TestContext? _testContext;
        private Exception? _lastException;

        /// <summary>
        /// Gets the test context.
        /// </summary>
        public TestContext TestContext
        {
            get { return _testContext ?? throw new InvalidOperationException("TestContext has not been initialized."); }
            set { _testContext = value; }
        }

        protected static T Require<T>(T? value, string? message = null) where T : class
        {
            Assert.IsNotNull(value, message);
            return value;
        }

        /// <summary>
        /// Kann von abgeleiteten Tests aufgerufen werden, um eine Exception explizit zu melden.
        /// </summary>
        protected void SetLastException(Exception ex) => _lastException = ex;

        /// <summary>
        /// Wird nach jedem Test aufgerufen und meldet das Ergebnis an TestRunReportWriter.
        /// </summary>
        [TestCleanup]
        public void ReportTestResult()
        {
            if (_testContext == null) return;

            var testName = _testContext.TestName;
            var className = _testContext.FullyQualifiedTestClassName
                ?? GetType().FullName
                ?? "Unknown";

            // Nur fehlgeschlagene Tests melden
            if (_testContext.CurrentTestOutcome == UnitTestOutcome.Failed)
            {
                var errorMessage = GetErrorMessage();
                var stackTrace = GetStackTrace();
                TestRunReportWriter.RecordResult(
                    testName,
                    className,
                    errorMessage,
                    stackTrace);
            }
        }

        private string GetErrorMessage()
        {
            // 1. Explizit gesetzte Exception (von abgeleiteten Tests)
            if (_lastException != null)
            {
                return _lastException.Message;
            }

            // 2. Fallback
            return $"Test failed (outcome: {_testContext.CurrentTestOutcome})";
        }

        private string GetStackTrace()
        {
            if (_lastException != null)
            {
                return _lastException.StackTrace ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
