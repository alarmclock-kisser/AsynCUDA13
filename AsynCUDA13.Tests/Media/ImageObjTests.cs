using AsynCUDA13.Media;
using Shouldly;

namespace AsynCUDA13.Tests.Media;

[TestClass]
public sealed class ImageObjTests : TestBase
{
    [TestMethod]
    [DataRow(1, 1, "#00000000")]
    [DataRow(8, 4, "#FF0000FF")]
    [DataRow(3, 7, "#00FF00FF")]
    public void SizeConstructor_AssignsExpectedShape(int width, int height, string color)
    {
        // Arrange & Act
        using var image = new ImageObj(width, height, color);

        // Assert
        image.ShouldSatisfyAllConditions(
            value => value.Width.ShouldBe(width),
            value => value.Height.ShouldBe(height),
            value => value.Channels.ShouldBe(4),
            value => value.Bitdepth.ShouldBe(32),
            value => value.Img.ShouldNotBeNull(),
            value => value.OnHost.ShouldBeTrue(),
            value => value.OnDevice.ShouldBeFalse());
    }

    [TestMethod]
    [DataRow("bmp")]
    [DataRow("png")]
    [DataRow("jpg")]
    [DataRow("gif")]
    public async Task AsBase64ImageAsync_ForSupportedFormat_ReturnsDecodableData(string format)
    {
        // Arrange
        using var image = new ImageObj(2, 2, "#336699FF");

        // Act
        var base64 = await image.AsBase64ImageAsync(format);
        var bytes = Convert.FromBase64String(base64);

        // Assert
        base64.ShouldNotBeNullOrWhiteSpace();
        bytes.ShouldNotBeEmpty();
        bytes.ShouldContain(value => value != 0);
    }

    [TestMethod]
    public async Task GetBytesAsync_WithKeepImage_ReturnsEveryRgbaByte()
    {
        // Arrange
        using var image = new ImageObj(3, 2, "#01020304");

        // Act
        var bytes = (await image.GetBytesAsync(keepImage: true)).ToArray();

        // Assert
        bytes.Length.ShouldBe(3 * 2 * 4);
        bytes.Chunk(4).ShouldAllBe(pixel => pixel.SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        image.Img.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task GetBytesAsync_WithoutKeepImage_ReleasesHostImage()
    {
        // Arrange
        using var image = new ImageObj(2, 2, "#FFFFFFFF");

        // Act
        var bytes = await image.GetBytesAsync();

        // Assert
        bytes.Count().ShouldBe(16);
        image.Img.ShouldBeNull();
        image.OnHost.ShouldBeFalse();
    }

    [TestMethod]
    [DataRow(0, 0, 1, 1)]
    [DataRow(-5, -9, 1, 1)]
    [DataRow(40_000, 50_000, 32_768, 32_768)]
    public void GetSharpSize_ClampsEachDimension(int height, int width, int expectedHeight, int expectedWidth)
    {
        // Arrange & Act
        var size = ImageCollection.GetSharpSize(height, width);

        // Assert
        size.Height.ShouldBe(expectedHeight);
        size.Width.ShouldBe(expectedWidth);
    }

    [TestMethod]
    [DataRow("#112233", new[] { 17, 34, 51 })]
    [DataRow("112233", new[] { 17, 34, 51 })]
    [DataRow("#AA112233", new[] { 17, 34, 51, 170 })]
    [DataRow("", new[] { 0, 0, 0 })]
    [DataRow("invalid", new[] { 0, 0, 0 })]
    public void GetRgbFromHexColor_ReturnsExpectedChannels(string color, int[] expected)
    {
        // Arrange & Act
        var channels = ImageCollection.GetRgbFromHexColor(color);

        // Assert
        channels.ShouldBe(expected);
        channels.ShouldAllBe(channel => channel >= 0 && channel <= 255);
    }

    [TestMethod]
    public void Dispose_ReleasesImageAndResetsMetadata()
    {
        // Arrange
        var image = new ImageObj(4, 4) { Pointer = 123, Name = "temporary" };
        var id = image.Id;
        var createdAt = image.CreatedAt;

        // Act
        image.Dispose();

        // Assert
        image.Img.ShouldBeNull();
        image.Pointer.ShouldBe(0);
        image.Id.ShouldBe(id);
        image.CreatedAt.ShouldBe(createdAt);
    }
}
