using AsynCUDA13.Media;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.MediaDtos;
using Shouldly;

namespace AsynCUDA13.Tests.Media;

[TestClass]
public sealed class MediaIdentityTests : TestBase
{
    [TestMethod]
    public void MediaIdentityContract_HasNoSetters()
    {
        // Arrange
        var idProperty = typeof(IMediaObj).GetProperty(nameof(IMediaObj.Id));
        var createdAtProperty = typeof(IMediaObj).GetProperty(nameof(IMediaObj.CreatedAt));

        // Act
        var setters = new[] { idProperty?.SetMethod, createdAtProperty?.SetMethod };

        // Assert
        idProperty.ShouldNotBeNull();
        createdAtProperty.ShouldNotBeNull();
        setters.ShouldAllBe(setter => setter == null);
    }

    [TestMethod]
    public void NewMediaObjects_AssignUniqueNonDefaultIdentities()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        using var firstImage = new ImageObj(2, 2);
        using var secondImage = new ImageObj(2, 2);
        using var firstAudio = new AudioObj(this.Logger);
        using var secondAudio = new AudioObj(this.Logger);
        var after = DateTime.UtcNow;
        IMediaObj[] media = [firstImage, secondImage, firstAudio, secondAudio];

        // Assert
        media.ShouldAllBe(item => item.Id != Guid.Empty);
        media.Select(item => item.Id).Distinct().Count().ShouldBe(media.Length);
        media.ShouldAllBe(item => item.CreatedAt >= before && item.CreatedAt <= after);
        media.ShouldAllBe(item => item.CreatedAt.Kind == DateTimeKind.Utc);
    }

    [TestMethod]
    public void ImageClone_CreatesNewIdentityWithoutCopyingTimestamps()
    {
        // Arrange
        using var source = new ImageObj(3, 2) { Name = "source", Pointer = 42 };

        // Act
        using var clone = source.Clone();

        // Assert
        clone.ShouldNotBeSameAs(source);
        clone.Id.ShouldNotBe(source.Id);
        clone.CreatedAt.ShouldBeGreaterThanOrEqualTo(source.CreatedAt);
        clone.Name.ShouldBe(source.Name);
        clone.Pointer.ShouldBe(source.Pointer);
    }

    [TestMethod]
    public async Task ImageCloneAsync_CreatesNewIdentity()
    {
        // Arrange
        using var source = new ImageObj(2, 4);

        // Act
        using var clone = await source.CloneAsync();

        // Assert
        clone.Id.ShouldNotBe(source.Id);
        clone.CreatedAt.ShouldNotBe(default);
        clone.Img.ShouldNotBeSameAs(source.Img);
    }

    [TestMethod]
    public void ImageCreateFromInfo_NeverCopiesIdentity()
    {
        // Arrange
        using var collection = new ImageCollection();
        var dtoId = Guid.NewGuid();
        var dtoCreatedAt = DateTime.UtcNow.AddYears(-5);
        var info = new ImageInfo
        {
            Id = dtoId,
            CreatedAt = dtoCreatedAt,
            Width = 2,
            Height = 3,
            Name = "dto-image"
        };

        // Act
        using var created = collection.CreateFromInfo(info, tryAdd: false);

        // Assert
        created.ShouldNotBeNull();
        created.Id.ShouldNotBe(dtoId);
        created.CreatedAt.ShouldNotBe(dtoCreatedAt);
        created.CreatedAt.ShouldBeGreaterThan(dtoCreatedAt);
        created.Name.ShouldBe(info.Name);
    }

    [TestMethod]
    public async Task AudioCreateFromInfo_NeverCopiesIdentity()
    {
        // Arrange
        await using var collection = new AudioCollection(this.Logger);
        var dtoId = Guid.NewGuid();
        var dtoCreatedAt = DateTime.UtcNow.AddYears(-5);
        var info = new AudioInfo
        {
            Id = dtoId,
            CreatedAt = dtoCreatedAt,
            Length = "8",
            SampleRate = 8_000,
            Channels = 1,
            BitDepth = 16,
            Name = "dto-audio"
        };

        // Act
        using var created = collection.CreateFromInfo(info, tryAdd: false);

        // Assert
        created.ShouldNotBeNull();
        created.Id.ShouldNotBe(dtoId);
        created.CreatedAt.ShouldNotBe(dtoCreatedAt);
        created.CreatedAt.ShouldBeGreaterThan(dtoCreatedAt);
        created.Name.ShouldBe(info.Name);
    }
}
