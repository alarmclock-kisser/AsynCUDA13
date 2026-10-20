using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Options;

namespace AsynCUDA13.Tests;

[TestClass]
[TestCategory("OpenCL")]
[DoNotParallelize]
public sealed class OpenClCadRoundingIntegrationTests
{
    private static OpenClService? service;

    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        var logger = new RollingFileMemoryLogger(new RollingFileMemoryLoggerOptions { Silent = true });
        service = HardwareTestGuard.CreateOpenClService(logger);
        string source = service.Compiler.GetKernelCode("cad_render_scene").ShouldNotBeNull();
        int start = source.IndexOf("inline float cad_round_grid(", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        int end = source.IndexOf("\ninline ", start + 1, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        // Execute the actual production helper, not a managed reimplementation.
        string probe = source[start..end] + "\n" + """
            __kernel void cad_round_grid_probe(__global float* values, int length)
            {
                int index = get_global_id(0);
                if (index < length) values[index] = cad_round_grid(values[index]);
            }
            """;
        service.Compiler.CompileKernel(probe).ShouldContain("cad_round_grid_probe");
        service.Compiler.HasKernel("cad_render_scene").ShouldBeTrue();
    }

    [ClassCleanup]
    public static void Cleanup() => service?.Dispose();

    [TestMethod]
    [DataRow("halfway")]
    [DataRow("halfway-neighbors")]
    [DataRow("integral-boundary")]
    [DataRow("special")]
    [DataRow("grid")]
    [DataRow("sampled-bits")]
    public void CadRoundGrid_MatchesAwayFromZeroIncludingSignedZero(string dataset)
    {
        // Arrange
        float[] input = CreateValues(dataset);
        float[] expected = input.Select(value => MathF.Round(value, MidpointRounding.AwayFromZero)).ToArray();
        var memory = service!.PushData(input).ShouldNotBeNull();

        // Act
        var response = service.Launcher.Execute("cad_round_grid_probe", memory.IndexPointer, input.Length);
        float[] actual = service.PullData<float>(memory.IndexPointer, keepBuffer: false);

        // Assert
        response.ShouldNotBeNull().Success.ShouldBeTrue();
        actual.Length.ShouldBe(expected.Length);
        expected.Select((value, index) => new { Index = index, Expected = value, Actual = actual[index] })
            .ShouldAllBe(pair => float.IsNaN(pair.Expected) ? float.IsNaN(pair.Actual)
                : BitConverter.SingleToInt32Bits(pair.Actual) == BitConverter.SingleToInt32Bits(pair.Expected));
        service.Register.AllocationCount.ShouldBe(0);
    }

    private static float[] CreateValues(string dataset) => dataset switch
    {
        "halfway" => [-10.5f, -2.5f, -1.5f, -0.5f, 0.5f, 1.5f, 2.5f, 10.5f],
        "halfway-neighbors" => new[] { -2.5f, -1.5f, -0.5f, 0.5f, 1.5f, 2.5f }
            .SelectMany(value => new[] { MathF.BitDecrement(value), value, MathF.BitIncrement(value) }).ToArray(),
        "integral-boundary" => [8388607f, 8388607.5f, 8388608f, 8388609f, 16777216f,
            -8388607f, -8388607.5f, -8388608f, -8388609f, -16777216f,
            2147483520f, 2147483648f, -2147483648f, float.MaxValue, float.MinValue],
        "special" => [0f, BitConverter.Int32BitsToSingle(int.MinValue), float.Epsilon, -float.Epsilon,
            BitConverter.Int32BitsToSingle(0x00800000), -BitConverter.Int32BitsToSingle(0x00800000),
            float.NaN, float.PositiveInfinity, float.NegativeInfinity],
        "grid" => [-80f, -10.1f, -5.8f, -5f, -4.2f, -0.01f, 0.01f, 4.2f, 5f, 5.8f, 10.1f, 80f],
        "sampled-bits" => CreateBitSamples(),
        _ => throw new ArgumentOutOfRangeException(nameof(dataset), dataset, "Unknown rounding dataset.")
    };

    private static float[] CreateBitSamples()
    {
        var random = new Random(0xCAD);
        return Enumerable.Range(0, 4096)
            .Select(_ => BitConverter.UInt32BitsToSingle((uint)random.NextInt64(1L << 32))).ToArray();
    }
}
