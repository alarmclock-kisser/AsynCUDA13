using AsynCUDA13.Media;
using AsynCUDA13.Shared.MediaDtos;
using Shouldly;

namespace AsynCUDA13.Tests.Media;

[TestClass]
public sealed class MediaCollectionTests : TestBase
{
    [TestMethod]
    public void ImageCollection_AddAndIndexers_ReturnSameObject()
    {
        // Arrange
        using var collection = new ImageCollection();
        using var image = new ImageObj(2, 2) { Name = "IndexedImage" };

        // Act
        var added = collection.Add(image);

        // Assert
        added.ShouldBeTrue();
        collection.Images.Count.ShouldBe(1);
        collection[image.Id].ShouldBeSameAs(image);
        collection["indexedimage"].ShouldBeSameAs(image);
        collection[0].ShouldBeSameAs(image);
        collection.Objects.ShouldContain(item => item.Id == image.Id);
    }

    [TestMethod]
    public void ImageCollection_AddingSameInstanceTwice_IsRejected()
    {
        // Arrange
        using var collection = new ImageCollection();
        using var image = new ImageObj(1, 1);
        collection.Add(image).ShouldBeTrue();

        // Act
        var addedAgain = collection.Add(image);

        // Assert
        addedAgain.ShouldBeFalse();
        collection.Images.ShouldHaveSingleItem();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ImageCollection_CreateFromInfo_RespectsDataChoice(bool emptyData)
    {
        // Arrange
        using var collection = new ImageCollection();
        var info = new ImageInfo { Width = 3, Height = 2, Channels = 4, BitDepth = 32, Name = "created" };

        // Act
        using var image = collection.CreateFromInfo(info, tryAdd: false, emptyData: emptyData);

        // Assert
        image.ShouldNotBeNull();
        (image.Img is null).ShouldBe(emptyData);
        image.Width.ShouldBe(info.Width);
        image.Height.ShouldBe(info.Height);
        image.Name.ShouldBe(info.Name);
    }

    [TestMethod]
    public async Task ImageCollection_ClearAsync_RemovesAndDisposesAllImages()
    {
        // Arrange
        using var collection = new ImageCollection();
        var images = Enumerable.Range(0, 3).Select(_ => new ImageObj(1, 1)).ToArray();
        images.ShouldAllBe(image => collection.Add(image));

        // Act
        await collection.ClearAsync();

        // Assert
        collection.Images.ShouldBeEmpty();
        images.ShouldAllBe(image => image.Img == null);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ImageCollection_ApplyImagesLimit_KeepsAtMostConfiguredCount(int maxImages)
    {
        // Arrange
        using var collection = new ImageCollection(maxImages: maxImages);
        for (var index = 0; index < 4; index++)
        {
            collection.Add(new ImageObj(1, 1) { Name = $"image-{index}" }).ShouldBeTrue();
        }

        // Act
        await collection.ApplyImagesLimitAsync();

        // Assert
        collection.Images.Count.ShouldBeLessThanOrEqualTo(maxImages);
        collection.Images.Select(image => image.Id).Distinct().Count().ShouldBe(collection.Images.Count);
    }

    [TestMethod]
    public async Task AudioCollection_AddAndAllIndexers_ReturnSameObject()
    {
        // Arrange
        await using var collection = new AudioCollection(this.Logger);
        using var audio = new AudioObj(this.Logger, [1f, 2f], 8_000, 1, 16, "IndexedAudio");

        // Act
        var added = collection.AddAudio(audio);

        // Assert
        added.ShouldBeTrue();
        collection[audio.Id].ShouldBeSameAs(audio);
        collection[0].ShouldBeSameAs(audio);
        collection["indexedaudio", false].ShouldBeSameAs(audio);
        collection["Audio", true].ShouldBeSameAs(audio);
    }

    [TestMethod]
    [DataRow("42", 42L)]
    [DataRow("invalid", 0L)]
    public async Task AudioCollection_CreateFromInfo_ParsesLength(string length, long expectedLength)
    {
        // Arrange
        await using var collection = new AudioCollection(this.Logger);
        var info = new AudioInfo
        {
            Length = length,
            SampleRate = 8_000,
            Channels = 1,
            BitDepth = 16,
            Name = "created"
        };

        // Act
        using var audio = collection.CreateFromInfo(info, tryAdd: false, emptyData: true);

        // Assert
        audio.ShouldNotBeNull();
        audio.Length.ShouldBe(expectedLength);
        audio.Data.ShouldBeEmpty();
        audio.Name.ShouldBe(info.Name);
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("name")]
    [DataRow("instance")]
    public async Task AudioCollection_RemoveAudio_RemovesEntry(string selector)
    {
        // Arrange
        await using var collection = new AudioCollection(this.Logger);
        using var audio = new AudioObj(this.Logger, [1f], 8_000, 1, 16, "remove-me");
        collection.AddAudio(audio).ShouldBeTrue();

        // Act
        var removed = selector switch
        {
            "id" => collection.RemoveAudio(audio.Id, disposeRemoved: false),
            "name" => collection.RemoveAudio(audio.Name, disposeRemoved: false),
            _ => collection.RemoveAudio(audio, disposeRemoved: false)
        };

        // Assert
        removed.ShouldBeTrue();
        collection.Audios.ShouldBeEmpty();
        collection[audio.Id].ShouldBeNull();
    }

    [TestMethod]
    public async Task AudioCollection_ClearAudios_ReturnsRemovedCount()
    {
        // Arrange
        await using var collection = new AudioCollection(this.Logger);
        Enumerable.Range(0, 4)
            .Select(index => new AudioObj(this.Logger, [index], 8_000, 1, 16, $"audio-{index}"))
            .ShouldAllBe(audio => collection.AddAudio(audio));

        // Act
        var removed = collection.ClearAudios();

        // Assert
        removed.ShouldBe(4);
        collection.Audios.ShouldBeEmpty();
        collection.Objects.ShouldBeEmpty();
    }
}
