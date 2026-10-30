using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Opus.Ogg;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioRecordComponentTests : IDisposable
{
    private readonly string _dir;
    private readonly StringWriter _output = new();
    private readonly Mock<IAudioInputCollector> _collector = new();

    public AudioRecordComponentTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private AudioRecordComponent Component(IOpusCodecFactory? factory = null) =>
        new(factory ?? new OpusCodecFactory(), _collector.Object, _output);

    private static OggOpusFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return OggOpusFile.Read(stream);
    }

    private string Out(string name = "rec.opus") => Path.Combine(_dir, name);

    private void Returns(short[] pcm) =>
        _collector.Setup(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(pcm);

    private static AudioRecordOptions Options(string outFile, int channels = 1, int bitrate = 64000, string? duration = null, string? wav = null) =>
        new(outFile, duration, wav, channels, bitrate, "dev", "fmt");

    [Fact]
    public async Task RunAsync_ShouldWriteValidMonoRecordingAndPrintDuration_WhenAudioCollected()
    {
        Returns(AudioTestSupport.Tone(440, 96000));
        var path = Out();

        await Component().RunAsync(Options(path), CancellationToken.None);

        _output.ToString().Should().Be($"Recorded '{path}': 2.000s{Environment.NewLine}");
        var file = Load(path);
        file.Head.ChannelCount.Should().Be(1);
        file.Head.PreSkip.Should().Be(312);
        file.Head.InputSampleRate.Should().Be(48000u);
        file.DurationSamples.Should().Be(96000);
        file.AudioPackets.Should().HaveCount(101);
        AudioTestSupport.DecodeWithFfmpeg(path).Should().HaveCount(96000);
    }

    [Fact]
    public async Task RunAsync_ShouldWriteStereoRecording_WhenChannelsIsTwo()
    {
        Returns(AudioTestSupport.Tone(440, 48000, 2));
        var path = Out();

        await Component().RunAsync(Options(path, channels: 2), CancellationToken.None);

        Load(path).Head.ChannelCount.Should().Be(2);
        AudioTestSupport.Probe(path).Channels.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ShouldPassRequestToCollector_WhenOptionsGiven()
    {
        Returns(AudioTestSupport.Tone(440, 4800));

        await Component().RunAsync(Options(Out(), channels: 2, duration: "1.5s", wav: "x.wav"), CancellationToken.None);

        var expected = new AudioInputRequest(2, TimeSpan.FromSeconds(1.5), "x.wav", "dev", "fmt");
        _collector.Verify(c => c.Validate(expected), Times.Once);
        _collector.Verify(c => c.CollectAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ShouldOverwriteExistingFile_WhenOutFileExists()
    {
        Returns(AudioTestSupport.Tone(440, 4800));
        var path = Out();
        await File.WriteAllTextAsync(path, "old");

        await Component().RunAsync(Options(path), CancellationToken.None);

        Load(path).DurationSamples.Should().Be(4800);
        Directory.GetFiles(_dir, "*", SearchOption.TopDirectoryOnly).Should().ContainSingle();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task RunAsync_ShouldThrowChannelsError_WhenChannelsInvalid(int channels)
    {
        var act = () => Component().RunAsync(Options(Out(), channels: channels), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("--channels must be 1 or 2");
        _collector.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(5999)]
    [InlineData(510001)]
    [InlineData(0)]
    public async Task RunAsync_ShouldThrowBitrateError_WhenBitrateOutOfRange(int bitrate)
    {
        var act = () => Component().RunAsync(Options(Out(), bitrate: bitrate), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("--bitrate must be between 6000 and 510000");
        _collector.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(510000)]
    public async Task RunAsync_ShouldAcceptBitrate_WhenAtLimits(int bitrate)
    {
        Returns(AudioTestSupport.Tone(440, 9600));

        await Component().RunAsync(Options(Out(), bitrate: bitrate), CancellationToken.None);

        File.Exists(Out()).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1:2:3:4")]
    public async Task RunAsync_ShouldThrowFormatError_WhenDurationInvalid(string duration)
    {
        var act = () => Component().RunAsync(Options(Out(), duration: duration), CancellationToken.None);

        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage($"Invalid time '{duration}'. Use [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix.");
        _collector.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_ShouldThrowNoAudio_WhenCollectorReturnsEmpty()
    {
        Returns([]);

        var act = () => Component().RunAsync(Options(Out()), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("No audio was captured");
        File.Exists(Out()).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldPropagateSourceErrorAndWriteNothing_WhenValidateFails()
    {
        _collector.Setup(c => c.Validate(It.IsAny<AudioInputRequest>())).Throws(new FileNotFoundException("File 'w.wav' not found"));

        var act = () => Component().RunAsync(Options(Out(), wav: "w.wav"), CancellationToken.None);

        (await act.Should().ThrowAsync<FileNotFoundException>()).WithMessage("File 'w.wav' not found");
        _collector.Verify(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Directory.GetFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldLeaveNoFile_WhenEncodingFails()
    {
        Returns(AudioTestSupport.Tone(440, 4800));
        var factory = new Mock<IOpusCodecFactory>();
        factory.Setup(f => f.CreateEncoder(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<OpusApplication>()))
            .Throws(new OpusException(-1, "opus_encoder_create failed: boom"));

        var act = () => Component(factory.Object).RunAsync(Options(Out()), CancellationToken.None);

        (await act.Should().ThrowAsync<OpusException>()).WithMessage("opus_encoder_create failed: boom");
        Directory.GetFiles(_dir).Should().BeEmpty();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldFormatDurationWithInvariantCulture_WhenCurrentCultureUsesComma()
    {
        Returns(AudioTestSupport.Tone(440, 72000));
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            await Component().RunAsync(Options(Out()), CancellationToken.None);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        _output.ToString().Should().Contain("': 1.500s");
    }

    private void ReturnsThen(short[] pcm, Action sideEffect) =>
        _collector.Setup(c => c.CollectAsync(It.IsAny<AudioInputRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => { sideEffect(); return Task.FromResult(pcm); });

    [Fact]
    public async Task RunAsync_ShouldLeaveNoFile_WhenWriteFailsInsideAtomicReplace()
    {
        Returns(AudioTestSupport.Tone(440, 4800));
        using var seam = AudioSeams.Override(afterWrite: _ => throw new IOException("disk full"));

        var act = () => Component().RunAsync(Options(Out()), CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()).WithMessage("disk full");
        Directory.GetFiles(_dir).Should().BeEmpty();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldKeepExistingOutFileAndThrowCancelled_WhenCancelledAfterCaptureWithDuration()
    {
        var existing = Out();
        await File.WriteAllBytesAsync(existing, [1, 2, 3]);
        using var cts = new CancellationTokenSource();
        ReturnsThen(AudioTestSupport.Tone(440, 4800), cts.Cancel);

        var act = () => Component().RunAsync(Options(existing, duration: "1s"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(existing)).Should().Equal(1, 2, 3);
        Directory.GetFiles(_dir).Should().ContainSingle();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldWriteNoFileAndThrowCancelled_WhenCancelledAfterCaptureWithWav()
    {
        using var cts = new CancellationTokenSource();
        ReturnsThen(AudioTestSupport.Tone(440, 4800), cts.Cancel);

        var act = () => Component().RunAsync(Options(Out(), wav: "x.wav"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldSaveCapturedAudio_WhenCancelledAfterCaptureWithoutDuration()
    {
        using var cts = new CancellationTokenSource();
        ReturnsThen(AudioTestSupport.Tone(440, 48000), cts.Cancel);

        await Component().RunAsync(Options(Out()), cts.Token);

        Load(Out()).DurationSamples.Should().Be(48000);
        _output.ToString().Should().Be($"Recorded '{Out()}': 1.000s{Environment.NewLine}");
    }
}
