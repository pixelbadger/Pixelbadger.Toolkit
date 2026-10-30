using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Opus.Ogg;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioOverwriteComponentTests : IDisposable
{
    private readonly string _dir;
    private readonly StringWriter _output = new();
    private readonly Mock<IAudioInputCollector> _collector = new();

    public AudioOverwriteComponentTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private AudioOverwriteComponent Component(IOpusCodecFactory? factory = null) =>
        new(factory ?? new OpusCodecFactory(), _collector.Object, _output);

    private void Returns(short[] pcm) =>
        _collector.Setup(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(pcm);

    private async Task<string> RecordingAsync(int frames, int channels = 1, string name = "memo.opus")
    {
        var path = Path.Combine(_dir, name);
        var collector = new Mock<IAudioInputCollector>();
        collector.Setup(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AudioTestSupport.Tone(440, frames, channels));
        await new AudioRecordComponent(new OpusCodecFactory(), collector.Object, new StringWriter())
            .RunAsync(new AudioRecordOptions(path, null, null, channels, 64000, null, null), CancellationToken.None);
        return path;
    }

    private static AudioEditOptions Options(string file, string at, string? duration = null, int bitrate = 64000) =>
        new(file, at, duration, null, bitrate, "dev", "fmt");

    private static OggOpusFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return OggOpusFile.Read(stream);
    }

    [Fact]
    public async Task RunAsync_ShouldReplaceExactlyTwentyFivePackets_WhenOverwritingHalfSecondMidFile()
    {
        var path = await RecordingAsync(96000);
        var before = Load(path);
        Returns(AudioTestSupport.Tone(880, 24000));

        await Component().RunAsync(Options(path, "1.0"), CancellationToken.None);

        _output.ToString().Should().Be($"Overwrote 0.500s at 0.994s (requested 1.000s) in '{path}': 2.000s{Environment.NewLine}");
        var after = Load(path);
        after.DurationSamples.Should().Be(96000);
        after.AudioPackets.Should().HaveCount(before.AudioPackets.Count);
        after.AudioPackets.Take(50).Should().BeEquivalentTo(before.AudioPackets.Take(50), o => o.WithStrictOrdering());
        after.AudioPackets.Skip(50 + 25).Should().BeEquivalentTo(before.AudioPackets.Skip(50 + 25), o => o.WithStrictOrdering());
        after.AudioPackets.Skip(50).Take(25).Zip(before.AudioPackets.Skip(50).Take(25)).Should().OnlyContain(p => !p.First.SequenceEqual(p.Second));
        after.HeadPacket.Should().Equal(before.HeadPacket);
        after.TagsPacket.Should().Equal(before.TagsPacket);
        after.EndTrim.Should().Be(before.EndTrim);
        AudioTestSupport.DecodeWithFfmpeg(path).Should().HaveCount(96000);
    }

    [Fact]
    public async Task RunAsync_ShouldGrowFile_WhenNewAudioReachesPastTheEnd()
    {
        var path = await RecordingAsync(96000);
        var before = Load(path);
        Returns(AudioTestSupport.Tone(880, 48000));

        await Component().RunAsync(Options(path, "1.5"), CancellationToken.None);

        _output.ToString().Should().Be($"Overwrote 1.000s at 1.494s (requested 1.500s) in '{path}': 2.500s{Environment.NewLine}");
        var after = Load(path);
        after.DurationSamples.Should().Be(71688 + 312 + 48000);
        after.AudioPackets.Take(75).Should().BeEquivalentTo(before.AudioPackets.Take(75), o => o.WithStrictOrdering());
        AudioTestSupport.DecodeWithFfmpeg(path).Should().HaveCount(120000);
    }

    [Fact]
    public async Task RunAsync_ShouldAppend_WhenAtEqualsDuration()
    {
        var path = await RecordingAsync(96000);
        var before = Load(path);
        Returns(AudioTestSupport.Tone(880, 24000));

        await Component().RunAsync(Options(path, "2"), CancellationToken.None);

        _output.ToString().Should().Be($"Overwrote 0.500s at 2.000s (requested 2.000s) in '{path}': 2.520s{Environment.NewLine}");
        var after = Load(path);
        after.AudioPackets.Take(before.AudioPackets.Count).Should().BeEquivalentTo(before.AudioPackets, o => o.WithStrictOrdering());
        after.DurationSamples.Should().Be(96000 + 648 + 24000 + 312);
    }

    [Fact]
    public async Task RunAsync_ShouldReplaceAtLeastOnePacket_WhenNewAudioShorterThanPacket()
    {
        var path = await RecordingAsync(96000);
        var before = Load(path);
        Returns(AudioTestSupport.Tone(880, 480));

        await Component().RunAsync(Options(path, "0.5"), CancellationToken.None);

        var after = Load(path);
        after.AudioPackets.Should().HaveCount(before.AudioPackets.Count);
        after.AudioPackets.Zip(before.AudioPackets).Count(p => !p.First.SequenceEqual(p.Second)).Should().Be(1);
        after.DurationSamples.Should().Be(96000);
    }

    [Fact]
    public async Task RunAsync_ShouldEncodeAtFileChannelCount_WhenFileIsStereo()
    {
        var path = await RecordingAsync(48000, channels: 2);
        Returns(AudioTestSupport.Tone(880, 9600, 2));

        await Component().RunAsync(Options(path, "0.5"), CancellationToken.None);

        _collector.Verify(c => c.CollectAsync(It.Is<AudioInputRequest>(r => r.Channels == 2), It.IsAny<CancellationToken>()), Times.Once);
        Load(path).Head.ChannelCount.Should().Be(2);
        Load(path).DurationSamples.Should().Be(48000);
    }

    [Fact]
    public async Task RunAsync_ShouldThrowBeyondEndBeforeCapturing_WhenAtPastDuration()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);

        var act = () => Component().RunAsync(Options(path, "2.5"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"Time 2.500s is beyond the end of '{path}' (2.000s)");
        _collector.VerifyNoOtherCalls();
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNotFound_WhenFileMissing()
    {
        var missing = Path.Combine(_dir, "missing.opus");

        var act = () => Component().RunAsync(Options(missing, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<FileNotFoundException>()).WithMessage($"File '{missing}' not found");
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNotOggOpus_WhenFileIsGarbage()
    {
        var path = Path.Combine(_dir, "bad.opus");
        await File.WriteAllTextAsync(path, "this is not an ogg file");

        var act = () => Component().RunAsync(Options(path, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("Not an Ogg Opus file");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-0.5")]
    public async Task RunAsync_ShouldThrowFormatError_WhenAtInvalid(string at)
    {
        var path = await RecordingAsync(4800);

        var act = () => Component().RunAsync(Options(path, at), CancellationToken.None);

        (await act.Should().ThrowAsync<FormatException>()).WithMessage($"Invalid time '{at}'. *");
    }

    [Fact]
    public async Task RunAsync_ShouldThrowBitrateError_WhenBitrateOutOfRange()
    {
        var path = await RecordingAsync(4800);

        var act = () => Component().RunAsync(Options(path, "0", bitrate: 510001), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("--bitrate must be between 6000 and 510000");
    }

    [Fact]
    public async Task RunAsync_ShouldLeaveOriginalByteIdenticalAndNoTempFile_WhenEncodingFailsAfterCapture()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);
        Returns(AudioTestSupport.Tone(880, 48000));
        var factory = new Mock<IOpusCodecFactory>();
        factory.Setup(f => f.CreateEncoder(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<OpusApplication>()))
            .Throws(new OpusException(-1, "opus_encoder_create failed: boom"));

        var act = () => Component(factory.Object).RunAsync(Options(path, "1"), CancellationToken.None);

        await act.Should().ThrowAsync<OpusException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNoAudio_WhenCollectorReturnsEmpty()
    {
        var path = await RecordingAsync(4800);
        Returns([]);

        var act = () => Component().RunAsync(Options(path, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("No audio was captured");
    }
}
