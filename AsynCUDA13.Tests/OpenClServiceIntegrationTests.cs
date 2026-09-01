using AsynCUDA13.OpenClBackend;
using Shouldly;

namespace AsynCUDA13.Tests;

[TestClass]
public sealed class OpenClServiceIntegrationTests : TestBase
{
    private OpenClService? service;

    [TestInitialize]
    public void Initialize() => this.service = HardwareTestGuard.CreateOpenClService(this.Logger);

    [TestCleanup]
    public void Cleanup() => this.service?.Dispose();

    [TestMethod]
    public void InitializedService_ExposesSelectedDeviceAndRuntimeComponents()
    {
        // Arrange & Act
        var runtime = this.service!;

        // Assert
        runtime.RuntimeType.ShouldBe("OpenCL");
        runtime.Online.ShouldBeTrue();
        runtime.SelectedDeviceId.ShouldBe(0);
        runtime.SelectedDeviceName.ShouldNotBeNullOrWhiteSpace();
        runtime.SelectedDeviceProperties.ShouldNotBeEmpty();
        runtime.Register.ShouldNotBeNull();
        runtime.Compiler.ShouldNotBeNull();
        runtime.Launcher.ShouldNotBeNull();
        runtime.Fourier.ShouldNotBeNull();
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void Initialize_WithOutOfRangeIndex_ReturnsFalseAndLogsError(int deviceIndex)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var initialized = this.service!.Initialize(deviceIndex);

        // Assert
        initialized.ShouldBeFalse();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("out of range", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("definitely-not-an-opencl-device")]
    public void Initialize_WithInvalidName_ReturnsFalseAndLogsReason(string deviceName)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var initialized = this.service!.Initialize(deviceName);

        // Assert
        initialized.ShouldBeFalse();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("Initialize:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Dispose_MakesServiceOfflineAndClearsRegistry()
    {
        // Arrange
        this.service!.PushData(new[] { 1f, 2f }).ShouldNotBeNull();

        // Act
        this.service.Dispose();

        // Assert
        this.service.Online.ShouldBeFalse();
        this.service.SelectedDeviceId.ShouldBe(-1);
        this.service.TotalAllocations.ShouldBe(0);
    }
}
