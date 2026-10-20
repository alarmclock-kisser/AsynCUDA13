using System.Text.RegularExpressions;
using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Options;

namespace AsynCUDA13.Tests;

[TestClass]
[TestCategory("OpenCL")]
[DoNotParallelize]
public sealed class OpenClRadialCoordinateIntegrationTests
{
    private static OpenClService? service;

    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        var logger = new RollingFileMemoryLogger(new RollingFileMemoryLoggerOptions { Silent = true });
        service = HardwareTestGuard.CreateOpenClService(logger);
        foreach (string variant in new[] { "IP", "OOP" })
        {
            string source = service.Compiler.GetKernelCode($"radial_kaleidoscope_image_{variant}").ShouldNotBeNull();
            string probe = CreateProbe(source, variant);
            service.Compiler.CompileKernel(probe).ShouldContain($"radial_coordinate_probe_{variant}");
        }
    }

    [ClassCleanup]
    public static void Cleanup() => service?.Dispose();

    [TestMethod]
    [DataRow("IP", 1)]
    [DataRow("IP", 7)]
    [DataRow("IP", 1024)]
    [DataRow("OOP", 1)]
    [DataRow("OOP", 7)]
    [DataRow("OOP", 1024)]
    public void RadialCoordinates_MatchFloorThenClampForBothAxes(string variant, int limit)
    {
        // Arrange
        float[] coordinates = [float.MinValue, -2147483648f, -5.9f, -1.5f, -float.Epsilon, -0.0f,
            0f, float.Epsilon, 0.49f, 0.99f, 1f, 1.99f, limit - 1f, limit - 0.5f,
            limit, limit + 0.5f, 2147483648f, float.MaxValue, float.NegativeInfinity, float.PositiveInfinity];
        int[] expected = coordinates.Select(value => (int)Math.Clamp(Math.Floor((double)value), 0d, limit - 1d)).ToArray();
        var input = service!.PushData(coordinates).ShouldNotBeNull();
        var outputX = service.PushData(new int[coordinates.Length]).ShouldNotBeNull();
        var outputY = service.PushData(new int[coordinates.Length]).ShouldNotBeNull();

        // Act
        var response = service.Launcher.Execute($"radial_coordinate_probe_{variant}", input.IndexPointer,
            outputX.IndexPointer, outputY.IndexPointer, coordinates.Length, limit);
        int[] actualX = service.PullData<int>(outputX.IndexPointer, keepBuffer: false);
        int[] actualY = service.PullData<int>(outputY.IndexPointer, keepBuffer: false);
        service.Register.FreeMemory(input.IndexPointer);

        // Assert
        response.ShouldNotBeNull().Success.ShouldBeTrue();
        actualX.ShouldBe(expected);
        actualY.ShouldBe(expected);
        actualX.Concat(actualY).ShouldAllBe(value => value >= 0 && value < limit);
        service.Register.AllocationCount.ShouldBe(0);
    }

    private static string CreateProbe(string source, string variant)
    {
        // Reuse the production expressions, not a managed copy of the replacement algorithm.
        string x = ExtractExpression(source, "si");
        string y = ExtractExpression(source, "sj");
        return $$"""
            __kernel void radial_coordinate_probe_{{variant}}(__global const float* coordinates,
                __global int* outputX, __global int* outputY, int length, int limit)
            {
                int index = get_global_id(0);
                if (index >= length) return;
                float sx = coordinates[index];
                float sy = coordinates[index];
                int si = {{x}};
                int sj = {{y}};
                outputX[index] = clamp(si, 0, limit - 1);
                outputY[index] = clamp(sj, 0, limit - 1);
            }
            """;
    }

    private static string ExtractExpression(string source, string variable)
    {
        Match match = Regex.Match(source, $@"int {variable}\s*=\s*(?<expression>[^;]+);");
        match.Success.ShouldBeTrue($"Coordinate expression '{variable}' must be present in the production kernel.");
        return match.Groups["expression"].Value;
    }
}
