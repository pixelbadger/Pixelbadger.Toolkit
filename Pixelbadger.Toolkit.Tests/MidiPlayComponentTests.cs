using System.Diagnostics;
using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class MidiPlayComponentTests
{
    private const int Block = 1024;

    private readonly List<string> _calls = new();
    private readonly Mock<IMidiSynthSourceFactory> _synthFactory = new();
    private readonly FakeSynth _synth;
    private readonly Mock<IAudioPlaybackSinkFactory> _sinkFactory = new();
    private readonly Mock<IAudioPlaybackSink> _sink = new();
    private readonly RecordingView _view;
    private readonly List<short[]> _written = new();
    private AudioPlaybackOptions? _openedWith;
    private readonly Queue<(float[] L, float[] R)> _blocks = new();

    public MidiPlayComponentTests()
    {
        _synth = new FakeSynth(_blocks, _calls);
        _synthFactory.Setup(f => f.Open(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() => { _calls.Add("synth.Open"); return _synth; });

        _sink.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<short>>(), It.IsAny<CancellationToken>()))
            .Returns((ReadOnlyMemory<short> m, CancellationToken _) => { _calls.Add("sink.Write"); _written.Add(m.ToArray()); return ValueTask.CompletedTask; });
        _sink.Setup(s => s.CompleteAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { _calls.Add("sink.Complete"); return Task.CompletedTask; });
        _sink.Setup(s => s.DisposeAsync()).Returns(() => { _calls.Add("sink.Dispose"); return ValueTask.CompletedTask; });
        _sinkFactory.Setup(f => f.Open(It.IsAny<AudioPlaybackOptions>()))
            .Returns((AudioPlaybackOptions o) => { _calls.Add("sink.Open"); _openedWith = o; return _sink.Object; });

        _view = new RecordingView(_calls);
    }

    private MidiPlayComponent Create() => new(_synthFactory.Object, _sinkFactory.Object, _view);

    private static MidiPlayOptions Options(string? format = "s16le", string? device = "out") =>
        new("song.mid", "bank.sf2", format, device);

    private void Blocks(int count, float value = 0.25f)
    {
        for (var i = 0; i < count; i++)
            _blocks.Enqueue((Enumerable.Repeat(value, Block).ToArray(), Enumerable.Repeat(-value, Block).ToArray()));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDependencyIsNull()
    {
        var f = () => new MidiPlayComponent(null!, _sinkFactory.Object, _view);
        var s = () => new MidiPlayComponent(_synthFactory.Object, null!, _view);
        var v = () => new MidiPlayComponent(_synthFactory.Object, _sinkFactory.Object, null!);

        f.Should().Throw<ArgumentNullException>();
        s.Should().Throw<ArgumentNullException>();
        v.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_ShouldCallInOrderAndReturnZero_WhenPlaybackSucceeds()
    {
        Blocks(2);

        var code = await Create().RunAsync(Options(), CancellationToken.None);

        code.Should().Be(0);
        _calls.Should().Equal("synth.Open", "sink.Open", "view.Start",
            "sink.Write", "sink.Write", "sink.Complete", "view.Finish",
            "sink.Dispose", "synth.Dispose", "view.Dispose");
        _view.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldPassFilesAndLengthToView_WhenStarting()
    {
        Blocks(1);

        await Create().RunAsync(Options(), CancellationToken.None);

        _view.Info.Should().Be(new MidiPlaybackInfo("song.mid", "bank.sf2", TimeSpan.FromSeconds(2)));
        _synthFactory.Verify(f => f.Open("song.mid", "bank.sf2"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ShouldInterleaveAndConvertSamples_WhenWriting()
    {
        var l = new float[Block];
        var r = new float[Block];
        l[0] = 1f; r[0] = -1f;
        l[1] = 2.5f; r[1] = -9f;                       // clamped
        l[2] = 0.5f; r[2] = -0.5f;                     // 16383.5 -> away from zero
        l[3] = 0f; r[3] = float.NaN;
        l[4] = 1f / 32767f; r[4] = 0.4f / 32767f;     // 1 and 0.4 -> 1 and 0
        _blocks.Enqueue((l, r));

        await Create().RunAsync(Options(), CancellationToken.None);

        var pcm = _written.Should().ContainSingle().Which;
        pcm.Length.Should().Be(Block * 2);
        pcm[0].Should().Be(32767);
        pcm[1].Should().Be(-32767);
        pcm[2].Should().Be(32767);
        pcm[3].Should().Be(-32767);
        pcm[4].Should().Be(16384);
        pcm[5].Should().Be(-16384);
        pcm[6].Should().Be(0);
        pcm[7].Should().Be(0);
        pcm[8].Should().Be(1);
        pcm[9].Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ShouldReportExactElapsed_WhenUpdatingView()
    {
        Blocks(3);

        await Create().RunAsync(Options(), CancellationToken.None);

        _view.Updates.Select(u => u.Elapsed).Should().Equal(
            TimeSpan.FromTicks(1024 * TimeSpan.TicksPerSecond / 48000),
            TimeSpan.FromTicks(2048 * TimeSpan.TicksPerSecond / 48000),
            TimeSpan.FromTicks(3072 * TimeSpan.TicksPerSecond / 48000));
        _view.Updates[0].Left.Should().OnlyContain(x => x == 0.25f).And.HaveCount(Block);
        _view.Updates[0].Right.Should().OnlyContain(x => x == -0.25f).And.HaveCount(Block);
        _view.FinishedAt.Should().Be(TimeSpan.FromTicks(3072 * TimeSpan.TicksPerSecond / 48000));
    }

    [Fact]
    public async Task RunAsync_ShouldWriteOnlyRenderedFrames_WhenFinalBlockIsPartial()
    {
        _blocks.Enqueue((new float[300], new float[300]));

        await Create().RunAsync(Options(), CancellationToken.None);

        _written.Should().ContainSingle().Which.Length.Should().Be(600);
    }

    [Fact]
    public async Task RunAsync_ShouldPassOptionsToSink_WhenFormatAndDeviceGiven()
    {
        Blocks(1);

        await Create().RunAsync(Options("wav", "/tmp/x.wav"), CancellationToken.None);

        _openedWith.Should().Be(new AudioPlaybackOptions("wav", "/tmp/x.wav"));
    }

    [Fact]
    public async Task RunAsync_ShouldResolvePlatformDefaults_WhenFormatAndDeviceNull()
    {
        Blocks(1);
        var (format, device) = AudioPlaybackDefaults.ForCurrentPlatform();

        await Create().RunAsync(Options(null, null), CancellationToken.None);

        _openedWith.Should().Be(new AudioPlaybackOptions(format, device));
    }

    [Fact]
    public async Task RunAsync_ShouldResolveEachFieldIndependently_WhenOnlyDeviceGiven()
    {
        Blocks(1);

        await Create().RunAsync(Options(null, "e2e"), CancellationToken.None);

        _openedWith.Should().Be(new AudioPlaybackOptions(AudioPlaybackDefaults.ForCurrentPlatform().Format, "e2e"));
    }

    [Fact]
    public async Task RunAsync_ShouldPropagateAndNotOpenSinkOrStartView_WhenSynthOpenFails()
    {
        _synthFactory.Setup(f => f.Open(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException("File 'song.mid' not found"));

        var act = () => Create().RunAsync(Options(), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>().WithMessage("File 'song.mid' not found");
        _sinkFactory.Verify(f => f.Open(It.IsAny<AudioPlaybackOptions>()), Times.Never);
        _calls.Should().NotContain("view.Start");
        _view.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldDisposeSynthAndViewWithoutStart_WhenSinkOpenFails()
    {
        _sinkFactory.Setup(f => f.Open(It.IsAny<AudioPlaybackOptions>()))
            .Throws(new InvalidOperationException("ffmpeg was not found on PATH."));

        var act = () => Create().RunAsync(Options(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("ffmpeg was not found*");
        _calls.Should().NotContain("view.Start");
        _calls.TakeLast(2).Should().Equal("synth.Dispose", "view.Dispose");
        _calls.Should().NotContain("sink.Dispose");
    }

    [Fact]
    public async Task RunAsync_ShouldPropagateAfterDisposal_WhenCompleteFails()
    {
        Blocks(1);
        _sink.Setup(s => s.CompleteAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioPlaybackException("ffmpeg playback failed (exit 1): boom"));

        var act = () => Create().RunAsync(Options(), CancellationToken.None);

        await act.Should().ThrowAsync<AudioPlaybackException>().WithMessage("ffmpeg playback failed (exit 1): boom");
        _calls.Should().NotContain("view.Finish");
        _calls.Should().NotContain("view.Stop");
        _calls.TakeLast(3).Should().Equal("sink.Dispose", "synth.Dispose", "view.Dispose");
    }

    [Fact]
    public async Task RunAsync_ShouldPropagate_WhenWriteFails()
    {
        Blocks(2);
        _sink.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<short>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(new AudioPlaybackException("ffmpeg playback failed (exit 1): x"))));

        var act = () => Create().RunAsync(Options(), CancellationToken.None);

        await act.Should().ThrowAsync<AudioPlaybackException>();
        _calls.Should().NotContain("view.Finish");
        _view.DisposeCount.Should().Be(1);
        _sink.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ShouldReturn130AndDisposeSinkBeforeStop_WhenCancelledMidPlayback()
    {
        Blocks(10);
        using var cts = new CancellationTokenSource();
        var writes = 0;
        _sink.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<short>>(), It.IsAny<CancellationToken>()))
            .Returns((ReadOnlyMemory<short> _, CancellationToken __) =>
            {
                _calls.Add("sink.Write");
                if (++writes == 3)
                    cts.Cancel();
                return ValueTask.CompletedTask;
            });

        var code = await Create().RunAsync(Options(), cts.Token);

        code.Should().Be(130);
        _calls.Should().Equal("synth.Open", "sink.Open", "view.Start",
            "sink.Write", "sink.Write", "sink.Write",
            "sink.Dispose", "view.Stop", "synth.Dispose", "view.Dispose");
        _view.StoppedAt.Should().Be(TimeSpan.FromTicks(3072 * TimeSpan.TicksPerSecond / 48000));
        _calls.Should().NotContain("view.Finish");
        _sink.Verify(s => s.CompleteAsync(It.IsAny<CancellationToken>()), Times.Never);
        _sink.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ShouldReturn130_WhenCancelledDuringComplete()
    {
        Blocks(1);
        using var cts = new CancellationTokenSource();
        _sink.Setup(s => s.CompleteAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken t) => { cts.Cancel(); return Task.FromCanceled(t); });

        var code = await Create().RunAsync(Options(), cts.Token);

        code.Should().Be(130);
        _calls.Should().ContainSingle(c => c == "view.Stop");
        _calls.Should().NotContain("view.Finish");
    }

    [Fact]
    public async Task RunAsync_ShouldCompleteWith130AndDisposeSink_WhenCancelledWhileWriteIsBlocked()
    {
        Blocks(5);
        using var cts = new CancellationTokenSource();
        var blockedWrite = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        // A pipe write that ignores the token entirely and only unblocks when the sink is disposed (ffmpeg killed).
        _sink.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<short>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { entered.TrySetResult(); return new ValueTask(blockedWrite.Task); });
        _sink.Setup(s => s.DisposeAsync()).Returns(() => { _calls.Add("sink.Dispose"); blockedWrite.TrySetResult(); return ValueTask.CompletedTask; });

        var run = Create().RunAsync(Options(), cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.IsCompleted.Should().BeFalse();

        cts.Cancel();
        // The blocked write never completes by itself; only disposing the sink releases it. 5 s is a hang guard only.
        var code = await run.WaitAsync(TimeSpan.FromSeconds(5));

        code.Should().Be(130);
        _calls.Should().Contain("sink.Dispose");
        _view.StoppedAt.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_ShouldReturn130WithoutWriting_WhenAlreadyCancelled()
    {
        Blocks(3);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var code = await Create().RunAsync(Options(), cts.Token);

        code.Should().Be(130);
        _written.Should().BeEmpty();
        _view.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldDisposeSynthOnce_WhenFinished()
    {
        Blocks(1);

        await Create().RunAsync(Options(), CancellationToken.None);

        _synth.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldWriteNothingAndStillComplete_WhenSynthYieldsNoAudio()
    {
        var code = await Create().RunAsync(Options(), CancellationToken.None);

        code.Should().Be(0);
        _written.Should().BeEmpty();
        _calls.Should().Contain("sink.Complete").And.Contain("view.Finish");
    }

    [Fact]
    public async Task RunAsync_ShouldThrow_WhenOptionsNull()
    {
        var act = () => Create().RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class FakeSynth : IMidiSynthSource
    {
        private readonly Queue<(float[] L, float[] R)> _blocks;
        private readonly List<string> _calls;

        public FakeSynth(Queue<(float[] L, float[] R)> blocks, List<string> calls)
        {
            _blocks = blocks;
            _calls = calls;
        }

        public int DisposeCount { get; private set; }

        public TimeSpan Length => TimeSpan.FromSeconds(2);

        public int Render(Span<float> left, Span<float> right)
        {
            if (_blocks.Count == 0)
                return 0;
            var (l, r) = _blocks.Dequeue();
            l.CopyTo(left);
            r.CopyTo(right);
            return l.Length;
        }

        public void Dispose()
        {
            DisposeCount++;
            _calls.Add("synth.Dispose");
        }
    }

    private sealed class RecordingView : IMidiPlaybackView
    {
        private readonly List<string> _calls;

        public RecordingView(List<string> calls) => _calls = calls;

        public MidiPlaybackInfo? Info { get; private set; }

        public List<(float[] Left, float[] Right, TimeSpan Elapsed)> Updates { get; } = new();

        public TimeSpan? FinishedAt { get; private set; }

        public TimeSpan? StoppedAt { get; private set; }

        public int DisposeCount { get; private set; }

        public void Start(MidiPlaybackInfo info)
        {
            _calls.Add("view.Start");
            Info = info;
        }

        public void Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right, TimeSpan elapsed) =>
            Updates.Add((left.ToArray(), right.ToArray(), elapsed));

        public void Finish(TimeSpan elapsed)
        {
            _calls.Add("view.Finish");
            FinishedAt = elapsed;
        }

        public void Stop(TimeSpan elapsed)
        {
            _calls.Add("view.Stop");
            StoppedAt = elapsed;
        }

        public void Dispose()
        {
            DisposeCount++;
            _calls.Add("view.Dispose");
        }
    }
}
