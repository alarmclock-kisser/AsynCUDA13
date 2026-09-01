using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.RuntimeDtos;
using AsynCUDA13.Shared.Serialization;
using Shouldly;

namespace AsynCUDA13.Tests.Shared;

[TestClass]
public sealed class SerializationTests : TestBase
{
    [TestMethod]
    [DataRow("byte")]
    [DataRow("int")]
    [DataRow("long")]
    [DataRow("float")]
    [DataRow("double")]
    public async Task OneDimensionalRoundTrip_PreservesUnmanagedValues(string type)
    {
        // Arrange, Act & Assert
        switch (type)
        {
            case "byte":
                await AssertRoundTripAsync<byte>([1, 2, 255]);
                break;
            case "int":
                await AssertRoundTripAsync<int>([-1, 0, 42, int.MaxValue]);
                break;
            case "long":
                await AssertRoundTripAsync<long>([-1, 0, long.MaxValue]);
                break;
            case "float":
                await AssertRoundTripAsync<float>([-1.5f, 0f, 42.25f]);
                break;
            case "double":
                await AssertRoundTripAsync<double>([-1.5, 0, Math.PI]);
                break;
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TwoDimensionalRoundTrip_PreservesChunksAndAsyncFlag(bool asyncCall)
    {
        // Arrange
        int[][] source = [[1, 2, 3], [], [4, 5]];

        // Act
        var payload = await DataSerializer.SerializeAsync(source, asyncCall) as SimdPayload2D;
        var parsed = await DataParser.ParseAsync<int>(payload!);

        // Assert
        payload.ShouldNotBeNull();
        payload.AsyncCall.ShouldBe(asyncCall);
        payload.DataChunks.Count().ShouldBe(source.Length);
        parsed.ShouldNotBeNull();
        parsed.Length.ShouldBe(source.Length);
        parsed.Zip(source).ShouldAllBe(pair => pair.First.SequenceEqual(pair.Second));
    }

    [TestMethod]
    public async Task NonGenericOneDimensionalParser_SupportsValueTypes()
    {
        // Arrange
        int[] source = [1, 2, 3];
        var payload = (SimdPayload1D) (await DataSerializer.SerializeAsync(source))!;

        // Act
        var parsed = await DataParser.ParseAsync(payload, typeof(int).AssemblyQualifiedName!, this.Logger);

        // Assert
        parsed.ShouldNotBeNull();
        parsed.ShouldBe(source.Cast<object>());
    }

    [TestMethod]
    public async Task NonGenericTwoDimensionalParser_SupportsValueTypes()
    {
        // Arrange
        int[][] source = [[1, 2], [3, 4]];
        var payload = (SimdPayload2D) (await DataSerializer.SerializeAsync(source))!;

        // Act
        var parsed = await DataParser.ParseAsync(payload, typeof(int).AssemblyQualifiedName!, this.Logger);

        // Assert
        parsed.ShouldNotBeNull();
        parsed.Length.ShouldBe(2);
        parsed[0].ShouldBe(source[0].Cast<object>());
        parsed[1].ShouldBe(source[1].Cast<object>());
    }

    [TestMethod]
    [DataRow(54, 24, 6)]
    [DataRow(17, 13, 1)]
    [DataRow(0, 5, 5)]
    [DataRow(5, 0, 5)]
    public void GreatCommonDivisor_ReturnsExpectedValue(int left, int right, int expected)
    {
        // Arrange & Act
        var result = DataSerializer.GreatCommonDivisor(left, right);

        // Assert
        result.ShouldBe(expected);
    }

    [TestMethod]
    [DataRow("extern \"C\" __global__ void Add(float* x) { }", "Add")]
    [DataRow("__kernel void fft(__global float* x) { }", "fft")]
    [DataRow("not a kernel", null)]
    public void ExtractKernelName_ReturnsExpectedName(string code, string? expected)
    {
        // Arrange & Act
        var name = DataParser.ExtractKernelName(code);

        // Assert
        name.ShouldBe(expected);
    }

    [TestMethod]
    [DataRow(true, "1", "2")]
    [DataRow(false, "1", 2)]
    public void AreAllArgumentsString_RecognizesHomogeneousArguments(bool expected, object first, object second)
    {
        // Arrange
        object[] arguments = [first, second];

        // Act
        var result = DataParser.AreAllArgumentsString(arguments);

        // Assert
        result.ShouldBe(expected);
    }

    [TestMethod]
    public void ParseArgumentValues_ConvertsDeclaredScalarTypes()
    {
        // Arrange
        string[] values = ["42", "1.5", "true", "text"];
        Type[] types = [typeof(int), typeof(float), typeof(bool), typeof(string)];

        // Act
        var parsed = DataParser.ParseArgumentValues(values, types);

        // Assert
        parsed.ShouldBe(new object[] { 42, 1.5f, true, "text" });
    }

    private static async Task AssertRoundTripAsync<T>(T[] source) where T : unmanaged
    {
        var payload = await DataSerializer.SerializeAsync(source) as SimdPayload1D;
        var parsed = await DataParser.ParseAsync<T>(payload!);

        payload.ShouldNotBeNull();
        payload.ElementType.ShouldBe(typeof(T).Name);
        parsed.ShouldBe(source);
    }
}
