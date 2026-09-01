using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.RuntimeDtos;
using Shouldly;

namespace AsynCUDA13.Tests.Shared;

[TestClass]
public sealed class DtoContractTests : TestBase
{
    [TestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("0", false)]
    [DataRow("null", false)]
    [DataRow("123", true)]
    public void ImageInfo_OnGpu_RecognizesPointerState(string? pointer, bool expected)
    {
        // Arrange
        var info = new ImageInfo { Pointer = pointer };

        // Act & Assert
        info.OnGpu.ShouldBe(expected);
    }

    [TestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("0", false)]
    [DataRow("null", false)]
    [DataRow("123", true)]
    public void AudioInfo_OnGpu_RecognizesPointerState(string? pointer, bool expected)
    {
        // Arrange
        var info = new AudioInfo { Pointer = pointer };

        // Act & Assert
        info.OnGpu.ShouldBe(expected);
    }

    [TestMethod]
    public void MediaInfo_IdMatch_RequiresMatchingIdentityAndOptionalGpuState()
    {
        // Arrange
        var id = Guid.NewGuid();
        var image = new ImageInfo { Id = id, Pointer = "17" };
        var audio = new AudioInfo { Id = id, Pointer = null };

        // Act & Assert
        image.IdMatch(id).ShouldBeTrue();
        image.IdMatch(id.ToString().ToUpperInvariant(), requireOnGpu: true).ShouldBeTrue();
        audio.IdMatch(id).ShouldBeTrue();
        audio.IdMatch(id, requireOnGpu: true).ShouldBeFalse();
        image.IdMatch(Guid.NewGuid()).ShouldBeFalse();
    }

    [TestMethod]
    [DataRow("CUDA", 0, "RTX", "cuda:0 'RTX'")]
    [DataRow("OpenCL", null, "CPU", "opencl:-1 'CPU'")]
    public void RuntimeDeviceInfo_DeviceEntry_FormatsEveryRuntime(string runtime, int? id, string name, string expected)
    {
        // Arrange
        var info = new RuntimeDeviceInfo { RuntimeType = runtime, DeviceId = id, DeviceName = name };

        // Act & Assert
        info.DeviceEntry.ShouldBe(expected);
    }

    [TestMethod]
    public void RuntimeMemInfo_ComputesShapeAndByteSize()
    {
        // Arrange
        var info = new RuntimeMemInfo
        {
            ElementType = typeof(float).AssemblyQualifiedName!,
            Lengths = ["3", "5"],
            Pointers = ["11", "12"]
        };

        // Act & Assert
        info.ShouldSatisfyAllConditions(
            value => value.IsValid.ShouldBeTrue(),
            value => value.Count.ShouldBe(2),
            value => value.TotalLength.ShouldBe("8"),
            value => value.ElementSize.ShouldBe(sizeof(float)),
            value => value.TotalSize.ShouldBe("32"),
            value => value.IndexPointer.ShouldBe("11"));
    }

    [TestMethod]
    public void RuntimeMemInfo_AssetReferenceId_UpdatesArrayView()
    {
        // Arrange
        var info = new RuntimeMemInfo();
        var id = Guid.NewGuid();

        // Act
        info.AssetReferenceId = id;

        // Assert
        info.AssetReferenceIds.ShouldBe([id]);
        info.AssetReferenceId.ShouldBe(id);

        // Act
        info.AssetReferenceId = null;

        // Assert
        info.AssetReferenceIds.ShouldBeEmpty();
        info.AssetReferenceId.ShouldBeNull();
    }

    [TestMethod]
    public void RuntimeKernelInfo_DescribesPointerAndScalarArguments()
    {
        // Arrange
        var info = new RuntimeKernelInfo
        {
            ArgumentNames = ["data", "length", "scale", "enabled"],
            ArgumentTypes = ["float*", "int", "float", "bool"]
        };

        // Act & Assert
        info.ArgumentsCount.ShouldBe(4);
        info.PointerArgumentsCount.ShouldBe(1);
        info.PointerArgumentTypes.ShouldContainKey(0);
        info.IsPointerArgument("data").ShouldBe("float*");
        info.IsPointerArgument("data", returnPointerType: false).ShouldBe("float");
        info.IsIntegerArgument("length").ShouldBe(true);
        info.IsDecimalArgument("scale").ShouldBe(true);
        info.IsBooleanArgument("enabled").ShouldBe(true);
        info.GetStepSize("length").ShouldBe("1");
        info.GetStepSize("scale").ShouldNotBeNullOrWhiteSpace();
    }
}
