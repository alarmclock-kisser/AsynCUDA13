using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Shared.Interfaces;
using Shouldly;

namespace AsynCUDA13.Tests;

[TestClass]
public sealed class OpenClCompilerIntegrationTests : TestBase
{
    private OpenClService? service;
    private IRuntimeCompiler? compiler;

    [TestInitialize]
    public void Initialize()
    {
        this.service = HardwareTestGuard.CreateOpenClService(this.Logger);
        this.compiler = this.service.Compiler;
    }

    [TestCleanup]
    public void Cleanup() => this.service?.Dispose();

    [DataTestMethod]
    [DataRow("__kernel void AddOne(__global float* data, int length) { int i = get_global_id(0); if (i < length) data[i] += 1.0f; }", "AddOne", 2)]
    [DataRow("__kernel void Scale(__global float* data, float factor, int length) { int i = get_global_id(0); if (i < length) data[i] *= factor; }", "Scale", 3)]
    [DataRow("__kernel void Copy(__global int* input, __global int* output, int length) { int i = get_global_id(0); if (i < length) output[i] = input[i]; }", "Copy", 3)]
    public void CompileKernel_WithValidSource_RegistersKernelAndArguments(string source, string kernelName, int argumentCount)
    {
        // Arrange & Act
        var compiledNames = this.compiler!.CompileKernel(source);
        var arguments = this.compiler.GetArguments(source);

        // Assert
        compiledNames.ShouldContain(kernelName);
        this.compiler.HasKernel(kernelName).ShouldBeTrue();
        arguments.Count.ShouldBe(argumentCount);
        arguments.Values.ShouldContain(type => type.IsPointer);
        arguments.Values.ShouldContain(typeof(int));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void CompileKernel_WithMissingSource_ThrowsArgumentException(string? source)
    {
        // Arrange & Act
        var exception = Should.Throw<ArgumentException>(() => this.compiler!.CompileKernel(source!));

        // Assert
        exception.ParamName.ShouldBe("kernelCode");
        exception.Message.ShouldContain("cannot be null or whitespace");
    }

    [DataTestMethod]
    [DataRow("void MissingQualifier(float* data, int length) {}", "does not contain '__kernel'")]
    [DataRow("__kernel int MissingVoid(__global int* data, int length) { return 0; }", "does not contain 'void '")]
    [DataRow("__kernel void MissingLength(__global float* data) { data[0] = 1; }", "does not contain 'int ', 'long ' or 'size_t '")]
    [DataRow("__kernel void Broken(__global float* data, int length) {", "unbalanced brackets { }")]
    public void PrecompileKernel_WithInvalidContract_ReturnsNullAndLogsReason(string source, string expectedMessage)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var kernelName = this.compiler!.PrecompileKernel(source);

        // Assert
        kernelName.ShouldBeNull();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("__kernel void SyntaxError(__global float* data, int length) { data[0] = ; }")]
    [DataRow("__kernel void UnknownType(__global definitely_not_a_type* data, int length) { }")]
    public void CompileKernel_WithInvalidOpenClSource_ThrowsBuildExceptionWithLog(string source)
    {
        // Arrange & Act
        var exception = Should.Throw<InvalidOperationException>(() => this.compiler!.CompileKernel(source));

        // Assert
        exception.Message.ShouldContain("BuildProgram failed");
        exception.Message.ShouldContain("Build log:");
    }

    [TestMethod]
    public void CompileKernel_WithMultipleEntryPoints_RegistersEveryKernel()
    {
        // Arrange
        const string source = "__kernel void First(__global float* data, int length) { int i = get_global_id(0); if (i < length) data[i] += 1; } __kernel void Second(__global float* data, int length) { int i = get_global_id(0); if (i < length) data[i] *= 2; }";

        // Act
        var names = this.compiler!.CompileKernel(source);

        // Assert
        names.ShouldContain("First");
        names.ShouldContain("Second");
        this.compiler.HasKernel("First").ShouldBeTrue();
        this.compiler.HasKernel("Second").ShouldBeTrue();
    }
}
