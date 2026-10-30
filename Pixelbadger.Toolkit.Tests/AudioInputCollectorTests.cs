using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioInputCollectorTests : IDisposable
{
    private readonly string _dir;
    private readonly StringWriter _output = new();

    public AudioInputCollectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class FakeSource : IAudioCaptureSource
    {
        private readonly int _channels;
        private readonly long _totalSamples;
        private readonly Exception? _failure;
        private long _served;

        public FakeSource(int channels, long totalSamples = long.MaxValue, Exception? failure = null)
        {
            _channels = channels;
            _totalSamples = totalSamples;
            _failure = failure;
        }

        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public int SampleRate => 48000;
        public int Channels => _channels;

        public async ValueTask<int> ReadAsync(Memory<short> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (_failure is not null && Reads == 3)
                throw _failure;
            // Paced like a microphone so that stop requests are observed while capturing.
            await Task.Delay(2, cancellationToken);
            var n = (int)Math.Min(buffer.Length, _totalSamples - _served);
            for (var i = 0; i < n; i++) buffer.Span[i] = (short)((_served + i) % 1000 + 1);
            _served += n;
            return n;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeInput : ITerminalInput
    {
        private readonly Func<bool> _enterPressed;
        private bool _consumed;

        public FakeInput(Func<bool> enterPressed) => _enterPressed = enterPressed;

        public bool KeyAvailable => !_consumed && _enterPressed();
        public ConsoleKeyInfo ReadKey()
        {
            _consumed = true;
            return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        }

        public (int Width, int Height) Size => (80, 24);
    }

    private AudioInputCollector Collector(
        FakeSource? source = null,
        Func<bool>? enter = null,
        Func<string?>? ffmpeg = null,
        bool nonInteractive = false,
        (string Format, string? Device)? defaults = null,
        List<FfmpegCaptureOptions>? seen = null) =>
        new(new FakeInput(enter ?? (() => false)), _output,
            o =>
            {
                seen?.Add(o);
                return source ?? new FakeSource(o.Channels);
            },
            ffmpeg ?? (() => "/usr/bin/ffmpeg"),
            () => nonInteractive,
            () => defaults ?? ("pulse", "default"));

    private string Wav(int rate, int channels, short[] samples)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".wav");
        AudioTestSupport.WriteWav(path, rate, channels, samples);
        return path;
    }

    [Fact]
    public async Task CollectAsync_ShouldReturnExactlyDurationFrames_WhenDurationGivenMono()
    {
        var source = new FakeSource(1);
        var request = new AudioInputRequest(1, TimeSpan.FromSeconds(0.5), null, null, null);

        var pcm = await Collector(source).CollectAsync(request, CancellationToken.None);

        pcm.Length.Should().Be(24000);
        pcm[0].Should().Be(1);
        pcm[999].Should().Be(1000);
        pcm[1000].Should().Be(1);
        source.Disposed.Should().BeTrue();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ShouldReturnExactlyDurationFramesPerChannel_WhenStereo()
    {
        var request = new AudioInputRequest(2, TimeSpan.FromMilliseconds(250), null, null, null);

        var pcm = await Collector(new FakeSource(2)).CollectAsync(request, CancellationToken.None);

        pcm.Length.Should().Be(12000 * 2);
    }

    [Fact]
    public async Task CollectAsync_ShouldRoundDurationToNearestSample_WhenNotWholeSamples()
    {
        // 1.0105 ms = 48.504 samples rounds to 49 frames (1.01 ms = 48.48 rounds to 48).
        var request = new AudioInputRequest(1, TimeSpan.FromTicks(10105), null, null, null);

        var pcm = await Collector(new FakeSource(1)).CollectAsync(request, CancellationToken.None);

        pcm.Length.Should().Be(49);
    }

    [Fact]
    public async Task CollectAsync_ShouldKeepWhatWasCaptured_WhenSourceEndsBeforeDuration()
    {
        var request = new AudioInputRequest(1, TimeSpan.FromSeconds(10), null, null, null);

        var pcm = await Collector(new FakeSource(1, totalSamples: 5000)).CollectAsync(request, CancellationToken.None);

        pcm.Length.Should().Be(5000);
        _output.ToString().Should().Be("Warning: capture ended after 0.104s, before the requested 10.000s" + Environment.NewLine);
    }

    [Fact]
    public async Task CollectAsync_ShouldNotWarn_WhenCaptureReachesDuration()
    {
        var request = new AudioInputRequest(1, TimeSpan.FromSeconds(0.1), null, null, null);

        await Collector(new FakeSource(1)).CollectAsync(request, CancellationToken.None);

        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ShouldNotWarn_WhenWavIsShorterThanDuration()
    {
        var wav = Wav(48000, 1, AudioTestSupport.Tone(440, 4800));

        await Collector().CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(5), wav, null, null), CancellationToken.None);

        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ShouldKeepEverySampleInOrder_WhenCaptureSpansSeveralBuffers()
    {
        // 3 s of mono is 144000 samples: more than one internal block, so a block boundary falls inside the capture.
        var request = new AudioInputRequest(1, TimeSpan.FromSeconds(3), null, null, null);

        var pcm = await Collector(new FakeSource(1)).CollectAsync(request, CancellationToken.None);

        pcm.Length.Should().Be(144000);
        pcm.Select((v, i) => v == (short)(i % 1000 + 1)).Should().OnlyContain(ok => ok);
    }

    [Fact]
    public async Task CollectAsync_ShouldDropPartialFrame_WhenStereoSourceEndsMidFrame()
    {
        var request = new AudioInputRequest(2, TimeSpan.FromSeconds(10), null, null, null);

        var pcm = await Collector(new FakeSource(2, totalSamples: 4001)).CollectAsync(request, CancellationToken.None);

        pcm.Length.Should().Be(4000);
    }

    [Fact]
    public async Task CollectAsync_ShouldPrintRecordingMessageAndStopOnEnter_WhenNoDuration()
    {
        var source = new FakeSource(1);
        var collector = Collector(source, enter: () => source.Reads >= 5);

        var pcm = await collector.CollectAsync(new AudioInputRequest(1, null, null, null, null), CancellationToken.None);

        pcm.Length.Should().BeGreaterThan(0);
        pcm.Length.Should().BeLessThan(960 * 200);
        source.Disposed.Should().BeTrue();
        _output.ToString().Should().Be("Recording... press Enter or Ctrl+C to stop" + Environment.NewLine);
    }

    [Fact]
    public async Task CollectAsync_ShouldFinishNormallyWithCapturedAudio_WhenCancelledByCtrlCWithoutDuration()
    {
        var source = new FakeSource(1);
        using var cts = new CancellationTokenSource();
        var collector = Collector(source);

        var task = collector.CollectAsync(new AudioInputRequest(1, null, null, null, null), cts.Token);
        while (source.Reads < 5) await Task.Delay(2);
        cts.Cancel();
        var pcm = await task;

        pcm.Length.Should().BeGreaterThan(0);
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task CollectAsync_ShouldThrowCancelledAndDisposeSource_WhenCancelledWhileDurationCaptureRuns()
    {
        var source = new FakeSource(1);
        using var cts = new CancellationTokenSource();
        var collector = Collector(source);

        var task = collector.CollectAsync(new AudioInputRequest(1, TimeSpan.FromMinutes(5), null, null, null), cts.Token);
        while (source.Reads < 5) await Task.Delay(2);
        cts.Cancel();

        Func<Task> act = () => task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        source.Disposed.Should().BeTrue();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ShouldThrowCancelled_WhenCancelledBeforeWavRead()
    {
        var wav = Wav(48000, 1, AudioTestSupport.Tone(440, 4800));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Collector().CollectAsync(new AudioInputRequest(1, null, wav, null, null), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CollectAsync_ShouldNotPollKeyboard_WhenDurationGiven()
    {
        var polled = false;
        var collector = Collector(new FakeSource(1, 4800), enter: () => polled = true);

        var pcm = await collector.CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(0.1), null, null, null), CancellationToken.None);

        pcm.Length.Should().Be(4800);
        polled.Should().BeFalse();
    }

    [Fact]
    public async Task CollectAsync_ShouldThrowCaptureError_WhenSourceFailsAndNoStopRequestedWithinTwoSeconds()
    {
        var failure = new AudioCaptureException("ffmpeg capture failed (exit 1): boom");
        var collector = Collector(new FakeSource(1, failure: failure));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var act = () => collector.CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(5), null, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<AudioCaptureException>()).WithMessage("ffmpeg capture failed (exit 1): boom");
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9), "the failure is only reported after the two-second stop window");
    }

    [Fact]
    public async Task CollectAsync_ShouldTreatFailureAsUserStop_WhenCancelledOneAndAHalfSecondsAfterSourceDied()
    {
        var source = new FakeSource(1, failure: new AudioCaptureException("ffmpeg capture failed (exit 255): no error output"));
        using var cts = new CancellationTokenSource();
        var collector = Collector(source);

        var task = collector.CollectAsync(new AudioInputRequest(1, null, null, null, null), cts.Token);
        while (source.Reads < 3) await Task.Delay(1);
        cts.CancelAfter(1500);
        var pcm = await task;

        pcm.Length.Should().Be(2 * 960, "the two reads before the failure were kept");
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task CollectAsync_ShouldAbortWithCancelled_WhenDurationCaptureFailsAsCtrlCArrives()
    {
        var source = new FakeSource(1, failure: new AudioCaptureException("ffmpeg capture failed (exit 255): no error output"));
        using var cts = new CancellationTokenSource();
        var collector = Collector(source);

        var task = collector.CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(30), null, null, null), cts.Token);
        while (source.Reads < 3) await Task.Delay(1);
        cts.CancelAfter(100);

        Func<Task> act = () => task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CollectAsync_ShouldFinishNormally_WhenFfmpegDiesAsCtrlCArrivesWithoutDuration()
    {
        var source = new FakeSource(1, failure: new AudioCaptureException("ffmpeg capture failed (exit 255): no error output"));
        using var cts = new CancellationTokenSource();
        var collector = Collector(source);

        var task = collector.CollectAsync(new AudioInputRequest(1, null, null, null, null), cts.Token);
        while (source.Reads < 3) await Task.Delay(1);
        cts.CancelAfter(50);
        var pcm = await task;

        pcm.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CollectAsync_ShouldPassPlatformDefaultsToCapture_WhenNoDeviceOrFormatGiven()
    {
        var seen = new List<FfmpegCaptureOptions>();
        var collector = Collector(seen: seen, defaults: ("avfoundation", ":0"));

        await collector.CollectAsync(new AudioInputRequest(2, TimeSpan.FromSeconds(0.05), null, null, null), CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be(new FfmpegCaptureOptions("/usr/bin/ffmpeg", "avfoundation", ":0", 2));
    }

    [Fact]
    public async Task CollectAsync_ShouldPassExplicitDeviceAndFormat_WhenGiven()
    {
        var seen = new List<FfmpegCaptureOptions>();
        var collector = Collector(seen: seen);

        await collector.CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(0.05), null, "sine=frequency=440", "lavfi"), CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be(new FfmpegCaptureOptions("/usr/bin/ffmpeg", "lavfi", "sine=frequency=440", 1));
    }

    [Fact]
    public async Task CollectAsync_ShouldReadWavAndTruncateToDuration_WhenFromWavAndDuration()
    {
        var wav = Wav(48000, 1, AudioTestSupport.Tone(440, 48000));
        var seen = new List<FfmpegCaptureOptions>();
        var collector = Collector(seen: seen, ffmpeg: () => null);

        var pcm = await collector.CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(0.25), wav, null, null), CancellationToken.None);

        pcm.Length.Should().Be(12000);
        seen.Should().BeEmpty();
        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ShouldKeepWholeWav_WhenDurationLongerThanWav()
    {
        var wav = Wav(48000, 1, AudioTestSupport.Tone(440, 4800));

        var pcm = await Collector().CollectAsync(new AudioInputRequest(1, TimeSpan.FromSeconds(5), wav, null, null), CancellationToken.None);

        pcm.Length.Should().Be(4800);
    }

    [Fact]
    public async Task CollectAsync_ShouldUpsampleWavTo48kAndConvertChannels_WhenWavIsStereo24k()
    {
        var wav = Wav(24000, 2, AudioTestSupport.Tone(440, 2400, 2));

        var mono = await Collector().CollectAsync(new AudioInputRequest(1, null, wav, null, null), CancellationToken.None);
        var stereo = await Collector().CollectAsync(new AudioInputRequest(2, null, wav, null, null), CancellationToken.None);

        mono.Length.Should().Be(4800);
        stereo.Length.Should().Be(9600);
    }

    [Fact]
    public async Task CollectAsync_ShouldNotRequireInteractivityOrFfmpeg_WhenFromWavWithoutDuration()
    {
        var wav = Wav(48000, 1, AudioTestSupport.Tone(440, 480));

        var pcm = await Collector(nonInteractive: true, ffmpeg: () => null)
            .CollectAsync(new AudioInputRequest(1, null, wav, null, null), CancellationToken.None);

        pcm.Length.Should().Be(480);
    }

    [Fact]
    public async Task CollectAsync_ShouldRejectUnsupportedWavRate_WhenRateIs22050()
    {
        var wav = Wav(22050, 1, AudioTestSupport.Tone(440, 2205));

        var act = () => Collector().CollectAsync(new AudioInputRequest(1, null, wav, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>())
            .WithMessage("Unsupported WAV sample rate 22050 Hz. Supported rates: 8000, 12000, 16000, 24000, 48000 Hz. Resample first, e.g. ffmpeg -i in.wav -ar 48000 out.wav");
    }

    [Fact]
    public async Task CollectAsync_ShouldRejectNonWavFile_WhenFromWavIsNotWav()
    {
        var path = Path.Combine(_dir, "bad.wav");
        await File.WriteAllTextAsync(path, "not a wav file at all");

        var act = () => Collector().CollectAsync(new AudioInputRequest(1, null, path, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("Not a WAV file");
    }

    [Fact]
    public void Validate_ShouldThrowNotFound_WhenWavMissing()
    {
        var missing = Path.Combine(_dir, "missing.wav");

        var act = () => Collector().Validate(new AudioInputRequest(1, null, missing, null, null));

        act.Should().Throw<FileNotFoundException>().WithMessage($"File '{missing}' not found");
    }

    [Fact]
    public void Validate_ShouldThrowInstallGuidance_WhenFfmpegMissingForCapture()
    {
        var act = () => Collector(ffmpeg: () => null).Validate(new AudioInputRequest(1, TimeSpan.FromSeconds(1), null, null, null));

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "ffmpeg was not found on PATH. Install ffmpeg (e.g. 'apt install ffmpeg' or 'brew install ffmpeg') or use --from-wav.");
    }

    [Fact]
    public void Validate_ShouldThrowDeviceRequired_WhenPlatformHasNoDefaultDeviceAndNoneGiven()
    {
        var collector = Collector(defaults: ("dshow", null));

        var act = () => collector.Validate(new AudioInputRequest(1, TimeSpan.FromSeconds(1), null, null, null));

        act.Should().Throw<InvalidOperationException>().WithMessage("--device is required on Windows, e.g. --device \"audio=Microphone\"");
        collector.Invoking(c => c.Validate(new AudioInputRequest(1, TimeSpan.FromSeconds(1), null, "audio=Mic", null))).Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldRefuse_WhenNoDurationAndNoInteractiveConsole()
    {
        var act = () => Collector(nonInteractive: true).Validate(new AudioInputRequest(1, null, null, null, null));

        act.Should().Throw<InvalidOperationException>().WithMessage("--duration or --from-wav is required when no interactive console is available");
    }

    [Fact]
    public void Validate_ShouldAccept_WhenDurationGivenAndNoInteractiveConsole()
    {
        Collector(nonInteractive: true).Invoking(c => c.Validate(new AudioInputRequest(1, TimeSpan.FromSeconds(1), null, null, null)))
            .Should().NotThrow();
    }

    [Fact]
    public async Task CollectAsync_ShouldNotStartCapture_WhenRefusedAsNonInteractive()
    {
        var seen = new List<FfmpegCaptureOptions>();
        var collector = Collector(nonInteractive: true, seen: seen);

        var act = () => collector.CollectAsync(new AudioInputRequest(1, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        seen.Should().BeEmpty();
        _output.ToString().Should().BeEmpty();
    }
}
