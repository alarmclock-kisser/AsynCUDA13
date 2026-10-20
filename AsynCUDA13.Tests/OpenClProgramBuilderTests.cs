using System.Text;
using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Options;
using Moq;
using OpenTK.Compute.OpenCL;

namespace AsynCUDA13.Tests;

[TestClass]
public sealed class OpenClProgramBuilderTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("\0")]
    [DataRow(" \r\n\0")]
    public void Build_WithSuccessfulEmptyLog_QueriesEveryDiagnosticWithoutWarnings(string buildLog)
    {
        // Arrange
        var logger = CreateLogger();
        var queries = new List<ProgramBuildInfo>();
        var builder = new OpenClProgramBuilder(logger);

        // Act
        builder.Build("valid.cl", "CPU; DriverVersion=test", "", () => CLResultCode.Success, field =>
        {
            queries.Add(field);
            return GetInfo(field, buildLog);
        });

        // Assert
        queries.ShouldBe([ProgramBuildInfo.Status, ProgramBuildInfo.Options, ProgramBuildInfo.Log]);
        logger.GetLogLines(returnFilteredLog: null).ShouldNotContain(line => line.Contains("[WARN]", StringComparison.Ordinal) || line.Contains("[ERROR]", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("warning: unused variable\0")]
    [DataRow("warning: Größe prüfen\nsecond line\0\0")]
    public void Build_WithSuccessfulNonEmptyLog_LogsCompilerDiagnostics(string buildLog)
    {
        // Arrange
        var logger = CreateLogger();
        var builder = new OpenClProgramBuilder(logger);

        // Act
        builder.Build("warnings.cl", "CPU; DriverVersion=test", "-cl-opt-disable", () => CLResultCode.Success,
            field => GetInfo(field, buildLog));

        // Assert
        var lines = logger.GetLogLines(returnFilteredLog: null);
        lines.ShouldContain(line => line.Contains("[WARN]", StringComparison.Ordinal)
            && line.Contains(buildLog.TrimEnd('\0'), StringComparison.Ordinal)
            && line.Contains("warnings.cl", StringComparison.Ordinal)
            && line.Contains("Requested options='-cl-opt-disable'", StringComparison.Ordinal));
        lines.ShouldNotContain(line => line.Contains("[ERROR]", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(-11, "error: invalid expression\nsource.cl:4:7\0")]
    [DataRow(-43, "invalid build options\0")]
    [DataRow(-3, "compiler unavailable\0")]
    public void Build_WithDriverFailure_LogsAndThrowsCompleteDiagnostics(int result, string buildLog)
    {
        // Arrange
        var logger = CreateLogger();
        var builder = new OpenClProgramBuilder(logger);

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => builder.Build("broken.cl", "CPU; DriverVersion=test", "",
            () => (CLResultCode)result, field => GetInfo(field, buildLog, -2)));

        // Assert
        exception.Message.ShouldContain("BuildProgram failed");
        exception.Message.ShouldContain(result.ToString());
        exception.Message.ShouldContain("broken.cl");
        exception.Message.ShouldContain("CPU; DriverVersion=test");
        exception.Message.ShouldContain("Requested options=''");
        exception.Message.ShouldContain("Driver options='driver-options'");
        exception.Message.ShouldContain("Build status=-2");
        exception.Message.ShouldContain($"Build log:{Environment.NewLine}{buildLog.TrimEnd('\0')}");
        logger.GetLogLines(returnFilteredLog: null).ShouldContain(line => line.Contains("[ERROR]", StringComparison.Ordinal) && line.Contains(exception.Message, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("\0", "<empty>")]
    [DataRow(null, "data=null")]
    public void Build_WithFailureAndEmptyOrMissingLog_ReportsTheDistinction(string? buildLog, string expectedMessage)
    {
        // Arrange
        var builder = new OpenClProgramBuilder(CreateLogger());

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => builder.Build("broken.cl", "CPU", "", () => (CLResultCode)(-11),
            field => GetInfo(field, buildLog)));

        // Assert
        exception.Message.ShouldContain(expectedMessage);
        exception.Message.ShouldContain("BuildProgram failed");
    }

    [TestMethod]
    [DataRow(ProgramBuildInfo.Status)]
    [DataRow(ProgramBuildInfo.Options)]
    [DataRow(ProgramBuildInfo.Log)]
    public void Build_WithSuccessfulBuildButFailedInfoQuery_LogsWarning(ProgramBuildInfo failedField)
    {
        // Arrange
        var logger = CreateLogger();
        var builder = new OpenClProgramBuilder(logger);

        // Act
        builder.Build("valid.cl", "CPU", "", () => CLResultCode.Success,
            field => field == failedField ? ((CLResultCode)(-30), null) : GetInfo(field, "\0"));

        // Assert
        logger.GetLogLines(returnFilteredLog: null).ShouldContain(line => line.Contains("[WARN]", StringComparison.Ordinal)
            && line.Contains($"clGetProgramBuildInfo({failedField}) unavailable", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(ProgramBuildInfo.Log)]
    [DataRow(ProgramBuildInfo.Status)]
    [DataRow(ProgramBuildInfo.Options)]
    public void Build_WithInfoQueryError_PreservesDriverAndQueryFailure(ProgramBuildInfo failedField)
    {
        // Arrange
        var builder = new OpenClProgramBuilder(CreateLogger());

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => builder.Build("broken.cl", "CPU", "", () => (CLResultCode)(-11),
            field => field == failedField ? ((CLResultCode)(-30), null) : GetInfo(field, "original error\0")));

        // Assert
        exception.Message.ShouldContain("BuildProgram failed");
        exception.Message.ShouldContain($"clGetProgramBuildInfo({failedField}) unavailable");
        exception.Message.ShouldContain("-30");
        if (failedField != ProgramBuildInfo.Log)
        {
            exception.Message.ShouldContain("original error");
        }
    }

    [TestMethod]
    [DataRow(ProgramBuildInfo.Log)]
    [DataRow(ProgramBuildInfo.Status)]
    [DataRow(ProgramBuildInfo.Options)]
    public void Build_WithThrowingInfoQuery_DoesNotHideOriginalBuildFailure(ProgramBuildInfo failedField)
    {
        // Arrange
        var builder = new OpenClProgramBuilder(CreateLogger());

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => builder.Build("broken.cl", "CPU", "", () => (CLResultCode)(-11),
            field => field == failedField ? throw new NotSupportedException("query unavailable") : GetInfo(field, "error\0")));

        // Assert
        exception.Message.ShouldContain("BuildProgram failed");
        exception.Message.ShouldContain($"clGetProgramBuildInfo({failedField}) threw NotSupportedException: query unavailable");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Build_WithFileLogging_FlushesStartBeforeBuildAndResultAfterReturn(bool fail)
    {
        // Arrange
        string directory = Path.Combine(Path.GetTempPath(), $"OpenClBuildTests-{Guid.NewGuid():N}");
        string mainLog = Path.Combine(directory, "application.log");
        string diagnosticPath = Path.ChangeExtension(mainLog, ".opencl-build.log");
        var logger = CreateFileLogger(mainLog);
        var builder = new OpenClProgramBuilder(logger.Object);
        try
        {
            // Act
            Action act = () => builder.Build("cad_render_scene.cl", "QEMU CPU; DriverVersion=test", "", () =>
            {
                string beforeBuild = File.ReadAllText(diagnosticPath);
                beforeBuild.ShouldContain("[BUILD START]");
                beforeBuild.ShouldContain("cad_render_scene.cl");
                beforeBuild.ShouldNotContain("[BUILD END]");
                return fail ? (CLResultCode)(-11) : CLResultCode.Success;
            }, field => GetInfo(field, "diagnostic: Größe\0"));
            if (fail) Should.Throw<InvalidOperationException>(act); else act();

            // Assert
            string persisted = File.ReadAllText(diagnosticPath);
            persisted.ShouldContain("[BUILD END]");
            persisted.ShouldContain("QEMU CPU; DriverVersion=test");
            persisted.ShouldContain("Requested options=''");
            persisted.ShouldContain("diagnostic: Größe");
            File.Exists(mainLog).ShouldBeFalse(); // No asynchronous logger writer was needed.
            logger.Verify(log => log.LogWarning(It.Is<string>(message => message.Contains("synchronous build diagnostics"))), Times.Once);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Build_WithUnwritableDiagnostics_DoesNotChangeBuildOutcome(bool fail)
    {
        // Arrange
        string directory = Path.Combine(Path.GetTempPath(), $"OpenClBuildTests-{Guid.NewGuid():N}");
        string mainLog = Path.Combine(directory, "application.log");
        Directory.CreateDirectory(Path.ChangeExtension(mainLog, ".opencl-build.log")); // A directory cannot be appended as a file.
        var logger = CreateFileLogger(mainLog);
        var builder = new OpenClProgramBuilder(logger.Object);
        try
        {
            // Act
            Action act = () => builder.Build("test.cl", "CPU", "", () => fail ? (CLResultCode)(-11) : CLResultCode.Success,
                field => GetInfo(field, "\0"));
            if (fail) Should.Throw<InvalidOperationException>(act).Message.ShouldContain("BuildProgram failed"); else act();

            // Assert
            logger.Verify(log => log.LogWarning(It.Is<string>(message => message.Contains("cannot persist build diagnostics"))), Times.AtLeastOnce);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Build_WithSilentLogger_DoesNotCreateDiagnosticFile()
    {
        // Arrange
        string mainLog = Path.Combine(Path.GetTempPath(), $"OpenClBuildTests-{Guid.NewGuid():N}.log");
        var logger = CreateFileLogger(mainLog, silent: true);
        var builder = new OpenClProgramBuilder(logger.Object);

        // Act
        builder.Build("test.cl", "CPU", "", () => CLResultCode.Success, field => GetInfo(field, "\0"));

        // Assert
        File.Exists(Path.ChangeExtension(mainLog, ".opencl-build.log")).ShouldBeFalse();
    }

    [TestMethod]
    public void Build_WithManagedBuildException_LogsContextAndRethrowsOriginalException()
    {
        // Arrange
        var logger = CreateLogger();
        var builder = new OpenClProgramBuilder(logger);
        var expected = new InvalidOperationException("binding failure");
        bool queried = false;

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => builder.Build("test.cl", "CPU", "", () => throw expected, field =>
        {
            queried = true;
            return GetInfo(field, "\0");
        }));

        // Assert
        exception.ShouldBeSameAs(expected);
        queried.ShouldBeFalse();
        logger.GetLogLines(returnFilteredLog: null).ShouldContain(line => line.Contains("[ERROR]", StringComparison.Ordinal)
            && line.Contains("[BUILD EXCEPTION]", StringComparison.Ordinal) && line.Contains("binding failure", StringComparison.Ordinal));
    }

    private static RollingFileMemoryLogger CreateLogger()
        => new(new RollingFileMemoryLoggerOptions { Silent = true, CreateLogFile = false, LogTimestampFormat = null });

    private static Mock<IRollingFileMemoryLogger> CreateFileLogger(string path, bool silent = false)
    {
        var logger = new Mock<IRollingFileMemoryLogger>();
        logger.SetupGet(log => log.Settings).Returns(new RollingFileMemoryLoggerOptions { Silent = silent });
        logger.SetupGet(log => log.LogFilePath).Returns(path);
        return logger;
    }

    private static (CLResultCode Code, byte[]? Bytes) GetInfo(ProgramBuildInfo field, string? buildLog, int status = 0)
        => (CLResultCode.Success, field switch
        {
            ProgramBuildInfo.Status => BitConverter.GetBytes(status),
            ProgramBuildInfo.Options => Encoding.UTF8.GetBytes("driver-options\0"),
            _ => buildLog == null ? null : Encoding.UTF8.GetBytes(buildLog)
        });
}
