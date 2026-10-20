using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Serialization;

namespace AsynCUDA13.Tests.Shared;

[TestClass]
public sealed class TypedDataParserTests : TestBase
{
    [TestMethod]
    [DataRow("Single")]
    [DataRow("System.Single")]
    [DataRow("System.Single, System.Private.CoreLib")]
    [DataRow("sInGlE")]
    public async Task ParseTypedAsync_WithFloatPayload_PreservesArrayTypeAndEveryValue(string elementType)
    {
        // Arrange
        float[] values = Enumerable.Range(-10, 20).Select(value => value * 0.5f).ToArray();
        var payload = (await DataSerializer.SerializeAsync(values)).ShouldBeOfType<SimdPayload1D>();

        // Act
        var result = await DataParser.ParseTypedAsync(payload, elementType, this.Logger);

        // Assert
        var actual = result.ShouldBeOfType<float[]>();
        actual.ShouldBe(values);
        actual.Length.ShouldBe(20);
    }

    [TestMethod]
    [DataRow("Single")]
    [DataRow("System.Single")]
    [DataRow("System.Single, System.Private.CoreLib")]
    public async Task ParseTypedAsync_WithChunks_PreservesJaggedArrayAndEmptyRows(string elementType)
    {
        // Arrange
        float[][] values = [[1f, 2.5f], [], [-3.5f]];
        var payload = (await DataSerializer.SerializeAsync(values)).ShouldBeOfType<SimdPayload2D>();

        // Act
        var result = await DataParser.ParseTypedAsync(payload, elementType, this.Logger);

        // Assert
        var actual = result.ShouldBeOfType<float[][]>();
        actual.Length.ShouldBe(values.Length);
        actual.Zip(values).ShouldAllBe(pair => pair.First.SequenceEqual(pair.Second));
    }

    [TestMethod]
    [DataRow("Int32")]
    [DataRow("Byte")]
    [DataRow("Double")]
    [DataRow("Int64")]
    public async Task ParseTypedAsync_WithOtherUnmanagedTypes_PreservesElementType(string elementType)
    {
        // Arrange, Act & Assert
        switch (elementType)
        {
            case "Int32": await this.AssertTypedRoundTripAsync<int>([-1, 0, int.MaxValue], elementType); break;
            case "Byte": await this.AssertTypedRoundTripAsync<byte>([0, 1, 255], elementType); break;
            case "Double": await this.AssertTypedRoundTripAsync<double>([-Math.PI, 0, Math.PI], elementType); break;
            case "Int64": await this.AssertTypedRoundTripAsync<long>([long.MinValue, 0, long.MaxValue], elementType); break;
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("NoSuchType")]
    [DataRow("Object")]
    [DataRow("System.String")]
    [DataRow("System.Nullable`1[System.Single]")]
    [DataRow("System.Collections.Generic.KeyValuePair`2[System.Int32,System.String]")]
    public async Task ParseTypedAsync_WithUnsupportedType_ReturnsNullAndLogsReason(string? elementType)
    {
        // Arrange
        var payload = new SimdPayload1D { ElementType = "Single", Data = "AACAPw==" };
        this.Logger.ClearLogs();

        // Act
        var result = await DataParser.ParseTypedAsync(payload, elementType, this.Logger);

        // Assert
        result.ShouldBeNull();
        this.Logger.GetLogLines(returnFilteredLog: null).ShouldContain(line => line.Contains("[ERROR]", StringComparison.Ordinal)
            && line.Contains("element type", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ParseTypedAsync_WithManagedFields_RejectsValueTypeBeforeDecoding()
    {
        // Arrange
        var payload = new SimdPayload1D { ElementType = "Single", Data = "AACAPw==" };

        // Act
        var result = await DataParser.ParseTypedAsync(payload, typeof(ManagedValue).AssemblyQualifiedName, this.Logger);

        // Assert
        result.ShouldBeNull();
        this.Logger.GetLogLines(returnFilteredLog: null).ShouldContain(line => line.Contains("contains managed references", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ParseTypedAsync_WithNullPayload_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(() => DataParser.ParseTypedAsync(null!, "Single", this.Logger));

        // Assert
        exception.ParamName.ShouldBe("payload");
    }

    private async Task AssertTypedRoundTripAsync<T>(T[] values, string elementType) where T : unmanaged
    {
        // Arrange
        var payload = (await DataSerializer.SerializeAsync(values)).ShouldBeOfType<SimdPayload1D>();

        // Act
        var result = await DataParser.ParseTypedAsync(payload, elementType, this.Logger);

        // Assert
        result.ShouldBeOfType<T[]>().ShouldBe(values);
    }

    public struct ManagedValue
    {
        public string? Text;
    }
}
