using AsynCUDA13.OpenClBackend;
using OpenTK.Compute.OpenCL;
using Shouldly;

namespace AsynCUDA13.Tests;

[TestClass]
public sealed class OpenClMemTests
{
    [DataTestMethod]
    [DataRow("float", 4, 16)]
    [DataRow("double", 3, 24)]
    [DataRow("int", 5, 20)]
    public void Constructor_ComputesDescriptorMetadata(string typeName, int length, long expectedBytes)
    {
        // Arrange
        var type = ResolveType(typeName);
        var buffer = new CLBuffer(new IntPtr(123));

        // Act
        var memory = new OpenClMem(buffer, length, type);

        // Assert
        memory.Count.ShouldBe(1);
        memory.IndexLength.ShouldBe(length);
        memory.TotalLength.ShouldBe(length);
        memory.TotalSize.ShouldBe(expectedBytes);
        memory.IndexPointer.ShouldBe(new IntPtr(123));
        memory.ElementType.ShouldBe(type);
    }

    [TestMethod]
    public void GroupConstructor_WithMismatchedLengths_ThrowsDescriptiveException()
    {
        // Arrange
        CLBuffer[] buffers = [new(new IntPtr(1)), new(new IntPtr(2))];

        // Act
        var exception = Should.Throw<ArgumentException>(() => new OpenClMem(buffers, [4], typeof(float)));

        // Assert
        exception.ParamName.ShouldBe("lengths");
        exception.Message.ShouldContain("number of buffers and lengths must match");
    }

    [TestMethod]
    public void Dispose_IsIdempotentAndPreservesIdentity()
    {
        // Arrange
        var memory = new OpenClMem([], [], typeof(float));
        var id = memory.Id;
        var createdAt = memory.CreatedAt;

        // Act
        var first = memory.Dispose();
        var second = memory.Dispose();

        // Assert
        first.ShouldBe(0);
        second.ShouldBe(0);
        memory.IsDisposed.ShouldBeTrue();
        memory.Id.ShouldBe(id);
        memory.CreatedAt.ShouldBe(createdAt);
        memory.Buffers.ShouldBeEmpty();
    }

    private static Type ResolveType(string typeName) => typeName switch
    {
        "float" => typeof(float),
        "double" => typeof(double),
        "int" => typeof(int),
        _ => throw new ArgumentOutOfRangeException(nameof(typeName))
    };
}
