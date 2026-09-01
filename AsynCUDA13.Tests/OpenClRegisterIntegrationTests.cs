using AsynCUDA13.OpenClBackend;
using Shouldly;

namespace AsynCUDA13.Tests;

[TestClass]
public sealed class OpenClRegisterIntegrationTests : TestBase
{
    private OpenClService? service;
    private OpenClRegister? register;

    [TestInitialize]
    public void Initialize()
    {
        this.service = HardwareTestGuard.CreateOpenClService(this.Logger);
        this.register = this.service.Register.ShouldBeOfType<OpenClRegister>();
    }

    [TestCleanup]
    public void Cleanup() => this.service?.Dispose();

    [DataTestMethod]
    [DataRow("float", 16)]
    [DataRow("int", 31)]
    [DataRow("double", 7)]
    public void PushPull_PrimitiveValuesPreserveTypeLengthAndContent(string typeName, int length)
    {
        // Arrange
        var values = CreateValues(typeName, length);

        // Act & Assert
        switch (values)
        {
            case float[] floats:
                AssertRoundTrip(floats);
                break;
            case int[] integers:
                AssertRoundTrip(integers);
                break;
            case double[] doubles:
                AssertRoundTrip(doubles);
                break;
        }
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-1024)]
    public void AllocateSingle_WithInvalidLength_ReturnsNullAndLogsLength(int length)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var memory = this.register!.AllocateSingle<float>(new IntPtr(length));

        // Assert
        memory.ShouldBeNull();
        this.register.AllocationCount.ShouldBe(0);
        this.Logger.GetLogLines().ShouldContain(line => line.Contains($"invalid length {length}", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(0, 4)]
    [DataRow(4, 0)]
    [DataRow(-1, 4)]
    public void AllocateGroup_WithInvalidLength_ReturnsNullWithoutLeaking(int first, int second)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var memory = this.register!.AllocateGroup<float>([new IntPtr(first), new IntPtr(second)]);

        // Assert
        memory.ShouldBeNull();
        this.register.AllocationCount.ShouldBe(0);
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("AllocateGroup: invalid length", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(42)]
    [DataRow(-42)]
    public void PullData_WithUnknownPointer_ReturnsEmptyAndLogsHandle(int pointer)
    {
        // Arrange
        this.Logger.ClearLogs();

        // Act
        var values = this.register!.PullData<float>(new IntPtr(pointer));

        // Assert
        values.ShouldBeEmpty();
        this.Logger.GetLogLines().ShouldContain(line => line.Contains("no allocation found for handle", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AsyncChunks_RoundTripAndFreeAllocation()
    {
        // Arrange
        float[][] chunks = [[1f, 2f], [3f], [4f, 5f, 6f]];

        // Act
        var memory = await this.register!.PushChunksAsync(chunks);
        var restored = await this.register.PullChunksAsync<float>(memory!.IndexPointer, keepBuffer: false);

        // Assert
        restored.SelectMany(chunk => chunk).ShouldBe(chunks.SelectMany(chunk => chunk));
        this.register.AllocationCount.ShouldBe(0);
    }

    [TestMethod]
    public void FreeMemory_WithUnknownIdentity_ReturnsZero()
    {
        // Arrange & Act
        var byId = this.register!.FreeMemory(Guid.NewGuid());
        var byPointer = this.register.FreeMemory(new IntPtr(987654));

        // Assert
        byId.ShouldBe(0);
        byPointer.ShouldBe(0);
    }

    private void AssertRoundTrip<T>(T[] values) where T : unmanaged
    {
        var memory = this.register!.PushData(values);
        memory.ShouldNotBeNull();
        memory.ElementType.ShouldBe(typeof(T));
        memory.TotalLength.ShouldBe(values.LongLength);
        this.register.PullData<T>(memory.IndexPointer, keepBuffer: false).ShouldBe(values);
        this.register.AllocationCount.ShouldBe(0);
    }

    private static Array CreateValues(string typeName, int length) => typeName switch
    {
        "float" => Enumerable.Range(0, length).Select(i => i * 0.5f).ToArray(),
        "int" => Enumerable.Range(-length, length).ToArray(),
        "double" => Enumerable.Range(0, length).Select(i => i / 3d).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(typeName))
    };
}
