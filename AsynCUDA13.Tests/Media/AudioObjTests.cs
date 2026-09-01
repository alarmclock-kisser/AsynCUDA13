using System.Text;
using AsynCUDA13.Media;
using Shouldly;

namespace AsynCUDA13.Tests.Media;

[TestClass]
public sealed class AudioObjTests : TestBase
{
    [TestMethod]
    [DataRow(8_000, 1, 16, 8)]
    [DataRow(44_100, 2, 32, 16)]
    public void DataConstructor_AssignsFormatAndDuration(int sampleRate, int channels, int bitDepth, int length)
    {
        // Arrange
        var samples = Enumerable.Range(0, length).Select(index => index / 10f).ToArray();

        // Act
        using var audio = new AudioObj(this.Logger, samples, sampleRate, channels, bitDepth, "sample");
        audio.Length = samples.LongLength;

        // Assert
        audio.ShouldSatisfyAllConditions(
            value => value.Data.ShouldBe(samples),
            value => value.SampleRate.ShouldBe(sampleRate),
            value => value.Channels.ShouldBe(channels),
            value => value.BitDepth.ShouldBe(bitDepth),
            value => value.Duration.ShouldBe(TimeSpan.FromSeconds((double) length / channels / sampleRate)),
            value => value.Name.ShouldBe("sample"));
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(32)]
    public void GetWavBytes_WithValidSamples_ReturnsRiffWave(int bits)
    {
        // Arrange
        using var audio = new AudioObj(this.Logger, [0f, 0.25f, -0.25f, 1f], 8_000, 1, bits);

        // Act
        var bytes = audio.GetWavBytes(bits);

        // Assert
        bytes.Length.ShouldBeGreaterThan(44);
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(bytes, 8, 4).ShouldBe("WAVE");
    }

    [TestMethod]
    public void GetWavBytes_WithoutSamples_ReturnsEmptyArray()
    {
        // Arrange
        using var audio = new AudioObj(this.Logger);

        // Act
        var bytes = audio.GetWavBytes();

        // Assert
        bytes.ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void GetChunks_WithInvalidChunkSize_Throws(int chunkSize)
    {
        // Arrange
        using var audio = CreateAudio();

        // Act
        var exception = Should.Throw<ArgumentException>(() => audio.GetChunks(chunkSize));

        // Assert
        exception.ParamName.ShouldBe(nameof(chunkSize));
    }

    [TestMethod]
    [DataRow(-0.1f)]
    [DataRow(1f)]
    [DataRow(1.5f)]
    public void GetChunks_WithInvalidOverlap_Throws(float overlap)
    {
        // Arrange
        using var audio = CreateAudio();

        // Act
        var exception = Should.Throw<ArgumentException>(() => audio.GetChunks(4, overlap));

        // Assert
        exception.ParamName.ShouldBe(nameof(overlap));
    }

    [TestMethod]
    [DataRow(4, 0f, 2)]
    [DataRow(4, 0.5f, 3)]
    [DataRow(8, 0f, 1)]
    public void GetChunks_ReturnsFixedLengthChunks(int chunkSize, float overlap, int expectedCount)
    {
        // Arrange
        using var audio = CreateAudio();

        // Act
        var chunks = audio.GetChunks(chunkSize, overlap);

        // Assert
        chunks.Length.ShouldBe(expectedCount);
        chunks.ShouldAllBe(chunk => chunk.Length == chunkSize);
        chunks.SelectMany(chunk => chunk).ShouldContain(1f);
    }

    [TestMethod]
    public async Task NormalizeAsync_ScalesPeakToTargetLevel()
    {
        // Arrange
        using var audio = new AudioObj(this.Logger, [-2f, -1f, 0f, 1f], 8_000, 1, 32);

        // Act
        await audio.NormalizeAsync(0.5f);

        // Assert
        audio.Data.Max(Math.Abs).ShouldBe(0.5f, 0.0001f);
        audio.Data.Zip(new[] { -0.5f, -0.25f, 0f, 0.25f })
            .ShouldAllBe(pair => Math.Abs(pair.First - pair.Second) <= 0.0001f);
    }

    [TestMethod]
    public async Task ResampleAsync_WithSameRate_OnlyUpdatesBitDepth()
    {
        // Arrange
        using var audio = CreateAudio();
        var samples = audio.Data.ToArray();

        // Act
        var result = await audio.ResampleAsync(audio.SampleRate, 24);

        // Assert
        result.ShouldBeTrue();
        audio.BitDepth.ShouldBe(24);
        audio.Data.ShouldBe(samples);
    }

    [TestMethod]
    public void Dispose_ClearsMutableDataButPreservesIdentity()
    {
        // Arrange
        var audio = CreateAudio();
        var id = audio.Id;
        var createdAt = audio.CreatedAt;

        // Act
        audio.Dispose();

        // Assert
        audio.Data.ShouldBeEmpty();
        audio.Name.ShouldBeEmpty();
        audio.Id.ShouldBe(id);
        audio.CreatedAt.ShouldBe(createdAt);
    }

    private AudioObj CreateAudio()
    {
        var audio = new AudioObj(this.Logger, [0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f], 8_000, 1, 16);
        audio.Length = audio.Data.LongLength;
        return audio;
    }
}
