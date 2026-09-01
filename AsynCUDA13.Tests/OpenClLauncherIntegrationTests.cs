using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Shared.Interfaces;
using Shouldly;

namespace AsynCUDA13.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OpenClLauncherIntegrationTests : TestBase
{
    private OpenClService? service;
    private IRuntimeCompiler? compiler;
    private IRuntimeLauncher? launcher;

    [TestInitialize]
    public void Initialize()
    {
        this.service = HardwareTestGuard.CreateOpenClService(this.Logger);
        this.compiler = this.service.Compiler;
        this.launcher = this.service.Launcher;
    }

    [TestCleanup]
    public void Cleanup() => this.service?.Dispose();

    [DataTestMethod]
    [DataRow("Add", 3f, 1f, 4f)]
    [DataRow("Multiply", 3f, 2f, 6f)]
    [DataRow("Subtract", 3f, 5f, -2f)]
    public void Execute_WithScalarKernel_TransformsEveryElement(string operation, float input, float operand, float expected)
    {
        // Arrange
        string kernelName = $"Apply{operation}";
        string expression = operation switch
        {
            "Add" => "+",
            "Multiply" => "*",
            "Subtract" => "-",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        string source = $"__kernel void {kernelName}(__global float* data, float operand, int length) {{ int i = get_global_id(0); if (i < length) data[i] = data[i] {expression} operand; }}";
        this.compiler!.CompileKernel(source);
        var values = Enumerable.Repeat(input, 32).ToArray();
        var memory = this.service!.PushData(values)!;

        // Act
        var response = this.launcher!.Execute(kernelName, memory.IndexPointer, operand, values.Length);
        var result = this.service.PullData<float>(memory.IndexPointer);

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.ElapsedMs.ShouldBeGreaterThanOrEqualTo(0);
        result.ShouldAllBe(value => MathF.Abs(value - expected) < 0.0001f);
    }

    [DataTestMethod]
    [DataRow("MissingKernel")]
    [DataRow("")]
    [DataRow("   ")]
    public async Task ExecuteAsync_WithUnknownKernel_ReturnsFailureAndLogsName(string kernelName)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var response = await this.launcher!.ExecuteAsync(kernelName, 1L, 1L, Array.Empty<object>());

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeFalse();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("kernel not found", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(-100L)]
    public void Execute_WithInvalidDerivedWorkSize_ReturnsFailureAndLogsWorkSize(long globalWorkSize)
    {
        // Arrange
        const string source = "__kernel void NoBuffers(int value) { }";
        this.compiler!.CompileKernel(source);
        this.Logger.ClearLogs();

        // Act
        var response = this.launcher!.Execute("NoBuffers", globalWorkSize, 0, 1);

        // Assert
        response.Success.ShouldBeFalse();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("invalid global work size", StringComparison.OrdinalIgnoreCase));
    }

}
