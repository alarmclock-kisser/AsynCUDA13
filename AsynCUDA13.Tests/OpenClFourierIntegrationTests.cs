using System.Numerics;
using AsynCUDA13.OpenClBackend;
using Shouldly;

namespace AsynCUDA13.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OpenClFourierIntegrationTests : TestBase
{
    private OpenClService? service;
    private OpenClFourier? fourier;

    [TestInitialize]
    public void Initialize()
    {
        this.service = HardwareTestGuard.CreateOpenClService(this.Logger);
        this.fourier = this.service.Fourier.ShouldBeOfType<OpenClFourier>();
    }

    [TestCleanup]
    public void Cleanup() => this.service?.Dispose();

    [DataTestMethod]
    [DataRow(0, 2, "null or empty")]
    [DataRow(3, 3, "power of two")]
    [DataRow(3, 2, "multiple")]
    public void FftChunked_WithInvalidShape_ReturnsNullAndLogsReason(int length, int chunkSize, string expectedMessage)
    {
        // Arrange
        var input = new float[length];
        this.Logger.ClearLogs();

        // Act
        var result = this.fourier!.FftChunked(input, chunkSize);

        // Assert
        result.ShouldBeNull();
        this.service!.TotalAllocations.ShouldBe(0);
        this.Logger.GetLogLines().ShouldContain(line => line.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-8)]
    public void NormalizeIfftResult_WithInvalidDivisor_ReturnsNull(int divisor)
    {
        // Arrange & Act
        var result = OpenClFourier.NormalizeIfftResult([1f, 2f], divisor);

        // Assert
        result.ShouldBeNull();
    }

    [DataTestMethod]
    [DataRow(2, 1f, 0.5f)]
    [DataRow(4, 8f, 2f)]
    public void NormalizeIfftResult_WithValidDivisorScalesValues(int divisor, float input, float expected)
    {
        // Arrange & Act
        var result = OpenClFourier.NormalizeIfftResult([input], divisor);

        // Assert
        result.ShouldBe([expected]);
    }

    [TestMethod]
    public void Ifft_WithEmptyInput_ReturnsNullAndLogsError()
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var result = this.fourier!.Ifft(Array.Empty<Vector2>());

        // Assert
        result.ShouldBeNull();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("Ifft: data is null or empty", StringComparison.Ordinal));
    }
}
