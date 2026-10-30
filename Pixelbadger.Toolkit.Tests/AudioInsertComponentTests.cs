using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Opus.Ogg;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioInsertComponentTests : IDisposable
{
    private readonly string _dir;
    private readonly StringWriter _output = new();
    private readonly Mock<IAudioInputCollector> _collector = new();

    public AudioInsertComponentTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private AudioInsertComponent Component(IOpusCodecFactory? factory = null) =>
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
    public async Task RunAsync_ShouldInsertAtSnappedBoundaryAndKeepOriginalPackets_WhenInsertingMidFile()
    {
        var path = await RecordingAsync(96000);
        var before = Load(path);
        Returns(AudioTestSupport.Tone(880, 48000));

        await Component().RunAsync(Options(path, "1.0"), CancellationToken.None);

        _output.ToString().Should().Be($"Inserted 1.000s at 0.994s (requested 1.000s) into '{path}': 3.020s{Environment.NewLine}");
        var after = Load(path);
        after.DurationSamples.Should().Be(96000 + 48960);
        after.AudioPackets.Should().HaveCount(before.AudioPackets.Count + 51);
        after.AudioPackets.Take(50).Should().BeEquivalentTo(before.AudioPackets.Take(50), o => o.WithStrictOrdering());
        after.AudioPackets.Skip(50 + 51).Should().BeEquivalentTo(before.AudioPackets.Skip(50), o => o.WithStrictOrdering());
        after.HeadPacket.Should().Equal(before.HeadPacket);
        after.TagsPacket.Should().Equal(before.TagsPacket);
        after.EndTrim.Should().Be(before.EndTrim);
        AudioTestSupport.DecodeWithFfmpeg(path).Should().HaveCount(96000 + 48960);
    }

    [Fact]
    public async Task RunAsync_ShouldSnapToNearestBoundary_WhenRequestedBetweenBoundaries()
    {
        var path = await RecordingAsync(96000);
        Returns(AudioTestSupport.Tone(880, 4800));

        await Component().RunAsync(Options(path, "1.013"), CancellationToken.None);

        _output.ToString().Should().StartWith("Inserted 0.100s at 1.014s (requested 1.013s) into ");
    }

    [Fact]
    public async Task RunAsync_ShouldAppend_WhenAtEqualsDuration()
    {
        var path = await RecordingAsync(96000);
        var before = Load(path);
        Returns(AudioTestSupport.Tone(880, 24000));

        await Component().RunAsync(Options(path, "2"), CancellationToken.None);

        _output.ToString().Should().Be($"Inserted 0.500s at 2.000s (requested 2.000s) into '{path}': 2.520s{Environment.NewLine}");
        var after = Load(path);
        after.AudioPackets.Take(before.AudioPackets.Count).Should().BeEquivalentTo(before.AudioPackets, o => o.WithStrictOrdering());
        after.DurationSamples.Should().Be(96000 + 648 + 24000 + 312);
    }

    [Fact]
    public async Task RunAsync_ShouldInsertAtStart_WhenAtIsZero()
    {
        var path = await RecordingAsync(48000);
        Returns(AudioTestSupport.Tone(880, 9600));

        await Component().RunAsync(Options(path, "0"), CancellationToken.None);

        _output.ToString().Should().StartWith("Inserted 0.200s at 0.000s (requested 0.000s) into ");
        Load(path).DurationSamples.Should().Be(48000 + 11 * 960);
    }

    [Fact]
    public async Task RunAsync_ShouldEncodeAtFileChannelCount_WhenFileIsStereo()
    {
        var path = await RecordingAsync(48000, channels: 2);
        Returns(AudioTestSupport.Tone(880, 9600, 2));

        await Component().RunAsync(Options(path, "0.5"), CancellationToken.None);

        _collector.Verify(c => c.Validate(It.Is<AudioInputRequest>(r => r.Channels == 2)), Times.Once);
        _collector.Verify(c => c.CollectAsync(It.Is<AudioInputRequest>(r => r.Channels == 2), It.IsAny<CancellationToken>()), Times.Once);
        Load(path).Head.ChannelCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldAcceptTimeFormats_WhenAtUsesSuffixOrClock()
    {
        var path = await RecordingAsync(96000);
        Returns(AudioTestSupport.Tone(880, 960));

        await Component().RunAsync(Options(path, "0:01.5"), CancellationToken.None);
        await Component().RunAsync(Options(path, "500ms"), CancellationToken.None);

        _output.ToString().Should().Contain("(requested 1.500s)").And.Contain("(requested 0.500s)");
    }

    [Fact]
    public async Task RunAsync_ShouldThrowBeyondEndBeforeCapturing_WhenAtPastDuration()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);

        var act = () => Component().RunAsync(Options(path, "2.001"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"Time 2.001s is beyond the end of '{path}' (2.000s)");
        _collector.VerifyNoOtherCalls();
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNotFound_WhenFileMissing()
    {
        var missing = Path.Combine(_dir, "missing.opus");

        var act = () => Component().RunAsync(Options(missing, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<FileNotFoundException>()).WithMessage($"File '{missing}' not found");
        _collector.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNotOggOpus_WhenFileIsGarbage()
    {
        var path = Path.Combine(_dir, "bad.opus");
        await File.WriteAllTextAsync(path, "this is not an ogg file");

        var act = () => Component().RunAsync(Options(path, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("Not an Ogg Opus file");
    }

    [Fact]
    public async Task RunAsync_ShouldThrowMultichannelError_WhenFileUsesMappingFamilyOne()
    {
        var path = Path.Combine(_dir, "surround.opus");
        var head = new byte[27];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1; head[9] = 6; head[10] = 0x38; head[11] = 0x01; head[12] = 0x80; head[13] = 0xBB; head[18] = 1;
        var tags = "OpusTags\0\0\0\0\0\0\0\0"u8.ToArray();
        await using (var stream = File.Create(path))
        {
            var writer = new OggPageWriter(stream);
            writer.WritePage(new OggPage(OggPageFlags.BeginOfStream, 0, 7, 0, [(byte)head.Length], head));
            writer.WritePage(new OggPage(OggPageFlags.None, 0, 7, 1, [(byte)tags.Length], tags));
        }

        var act = () => Component().RunAsync(Options(path, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>())
            .WithMessage("Unsupported Ogg Opus channel mapping family 1; only mono and stereo files are supported");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public async Task RunAsync_ShouldThrowFormatError_WhenAtInvalid(string at)
    {
        var path = await RecordingAsync(4800);

        var act = () => Component().RunAsync(Options(path, at), CancellationToken.None);

        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage($"Invalid time '{at}'. Use [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix.");
        _collector.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_ShouldThrowBitrateErrorBeforeReadingFile_WhenBitrateOutOfRange()
    {
        var act = () => Component().RunAsync(Options(Path.Combine(_dir, "missing.opus"), "0", bitrate: 1000), CancellationToken.None);

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
    public async Task RunAsync_ShouldLeaveOriginalUntouched_WhenCaptureFails()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);
        _collector.Setup(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioCaptureException("ffmpeg capture failed (exit 1): nope"));

        var act = () => Component().RunAsync(Options(path, "1"), CancellationToken.None);

        (await act.Should().ThrowAsync<AudioCaptureException>()).WithMessage("ffmpeg capture failed (exit 1): nope");
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNoAudio_WhenCollectorReturnsEmpty()
    {
        var path = await RecordingAsync(4800);
        var original = await File.ReadAllBytesAsync(path);
        Returns([]);

        var act = () => Component().RunAsync(Options(path, "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("No audio was captured");
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    private void ReturnsThen(short[] pcm, Action sideEffect) =>
        _collector.Setup(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => { sideEffect(); return Task.FromResult(pcm); });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_ShouldFailAndKeepOtherWritersContent_WhenFileChangedWhileRecording(bool changeLength)
    {
        var path = await RecordingAsync(96000);
        var otherWriter = (await File.ReadAllBytesAsync(path)).ToArray();
        if (changeLength)
            otherWriter = [.. otherWriter, 1, 2, 3];
        var otherWriteTime = File.GetLastWriteTimeUtc(path).AddMinutes(5);
        ReturnsThen(AudioTestSupport.Tone(880, 48000), () =>
        {
            File.WriteAllBytes(path, otherWriter);
            File.SetLastWriteTimeUtc(path, otherWriteTime);
        });

        var act = () => Component().RunAsync(Options(path, "1"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"File '{path}' changed while recording; no changes were written");
        (await File.ReadAllBytesAsync(path)).Should().Equal(otherWriter);
        Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetFiles(_dir).Should().ContainSingle();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldLeaveOriginalByteIdenticalAndNoTempFile_WhenWriteFailsInsideAtomicReplace()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);
        Returns(AudioTestSupport.Tone(880, 48000));
        long tempBytesWritten = 0;
        using var seam = AudioSeams.Override(afterWrite: stream =>
        {
            tempBytesWritten = stream.Length;
            throw new IOException("disk full");
        });

        var act = () => Component().RunAsync(Options(path, "1"), CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()).WithMessage("disk full");
        tempBytesWritten.Should().BeGreaterThan(0, "the failure happened after the new file was written to the temp file");
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetFiles(_dir).Should().ContainSingle();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldWriteNothingAndThrowCancelled_WhenCancelledAfterCaptureWithDuration()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);
        using var cts = new CancellationTokenSource();
        ReturnsThen(AudioTestSupport.Tone(880, 48000), cts.Cancel);

        var act = () => Component().RunAsync(Options(path, "1", "1s"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(_dir).Should().ContainSingle();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldWriteNothingAndThrowCancelled_WhenCancelledAfterCaptureWithWav()
    {
        var path = await RecordingAsync(96000);
        var original = await File.ReadAllBytesAsync(path);
        using var cts = new CancellationTokenSource();
        ReturnsThen(AudioTestSupport.Tone(880, 48000), cts.Cancel);

        var act = () => Component().RunAsync(new AudioEditOptions(path, "1", null, "x.wav", 64000, null, null), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    [Fact]
    public async Task RunAsync_ShouldSaveCapturedAudio_WhenCancelledAfterCaptureWithoutDuration()
    {
        var path = await RecordingAsync(96000);
        using var cts = new CancellationTokenSource();
        ReturnsThen(AudioTestSupport.Tone(880, 48000), cts.Cancel);

        await Component().RunAsync(Options(path, "1"), cts.Token);

        Load(path).DurationSamples.Should().Be(96000 + 48960);
        _output.ToString().Should().Contain("Inserted 1.000s at 0.994s");
    }
}
