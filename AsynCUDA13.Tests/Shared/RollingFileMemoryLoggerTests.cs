using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Options;
using Shouldly;

namespace AsynCUDA13.Tests.Shared;

[TestClass]
public sealed class RollingFileMemoryLoggerTests : TestBase
{
    [TestMethod]
    public void Log_RaisesEventAndStoresFormattedLine()
    {
        // Arrange
        var logger = CreateLogger();
        DateTime? writtenAt = null;
        string? writtenLine = null;
        logger.LogWritten += (timestamp, line) => (writtenAt, writtenLine) = (timestamp, line);
        logger.ClearLogs();

        // Act
        logger.Log("hello");

        // Assert
        writtenAt.ShouldNotBeNull();
        writtenLine.ShouldContain("hello");
        logger.GetLogLines().ShouldHaveSingleItem().ShouldContain("hello");
    }

    [TestMethod]
    [DataRow("info", "[INFO]")]
    [DataRow("success", "[SUCCESS]")]
    [DataRow("warning", "[WARN]")]
    [DataRow("error", "[ERROR]")]
    public void SeverityMethods_AddExpectedMarker(string severity, string marker)
    {
        // Arrange
        var logger = CreateLogger();
        logger.ClearLogs();

        // Act
        switch (severity)
        {
            case "info": logger.LogInfo("message"); break;
            case "success": logger.LogSuccess("message"); break;
            case "warning": logger.LogWarning("message"); break;
            case "error": logger.LogError("message"); break;
        }

        // Assert
        logger.GetLogLines().ShouldHaveSingleItem().ShouldContain(marker);
    }

    [TestMethod]
    public void FilterPhrase_ReturnsMatchingAndNonMatchingViews()
    {
        // Arrange
        var logger = CreateLogger("needle");
        logger.ClearLogs();
        logger.Log("contains needle");
        logger.Log("other");

        // Act
        var filtered = logger.GetLogLines(returnFilteredLog: true);
        var regular = logger.GetLogLines(returnFilteredLog: false);
        var all = logger.GetLogLines(returnFilteredLog: null);

        // Assert
        filtered.ShouldHaveSingleItem().ShouldContain("needle");
        regular.ShouldHaveSingleItem().ShouldContain("other");
        all.Count.ShouldBe(2);
    }

    [TestMethod]
    public void ClearLogs_RemovesEveryRecordedLine()
    {
        // Arrange
        var logger = CreateLogger();
        logger.Log("one");
        logger.Log("two");

        // Act
        logger.ClearLogs();

        // Assert
        logger.GetLogLines(returnFilteredLog: null).ShouldBeEmpty();
    }

    [TestMethod]
    public void LogException_IncludesContextAndExceptionMessage()
    {
        // Arrange
        var logger = CreateLogger();
        logger.ClearLogs();
        var exception = new InvalidOperationException("broken");

        // Act
        logger.Log(exception, preText: "operation failed");

        // Assert
        var lines = logger.GetLogLines(returnFilteredLog: null);
        lines.ShouldContain(line => line.Contains("operation failed", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Contains("broken", StringComparison.Ordinal));
    }

    private static RollingFileMemoryLogger CreateLogger(string? filterPhrase = null)
        => new(new RollingFileMemoryLoggerOptions
        {
            Silent = true,
            CreateLogFile = false,
            FilterPhrase = filterPhrase,
            LogTimestampFormat = null
        });
}
