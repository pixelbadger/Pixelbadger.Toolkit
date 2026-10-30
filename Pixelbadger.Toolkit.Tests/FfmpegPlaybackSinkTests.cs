using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class FfmpegPlaybackSinkTests : IDisposable
{
    private readonly MidiTestAssets.TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Ffmpeg => FfmpegLocator.Resolve() ?? throw new InvalidOperationException("ffmpeg is required for these tests");

    private static readonly string[] Prefix = { "-hide_banner", "-loglevel", "error", "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", "pipe:0" };

    private static IReadOnlyList<string> Args(string format, string device) =>
        FfmpegPlaybackSink.BuildArguments(new AudioPlaybackOptions(format, device));

    [Fact]
    public void BuildArguments_ShouldUsePulseWithShortBufferAndNoDevice_WhenDeviceIsDefault()
    {
        Args("pulse", "default").Should().Equal(Prefix.Concat(new[]
        {
            "-buffer_duration", "50", "-f", "pulse", "Pixelbadger Toolkit"
        }));
    }

    [Fact]
    public void BuildArguments_ShouldPassDeviceVerbatim_WhenFormatIsPulseWithNamedDevice()
    {
        Args("pulse", "e2e").Should().Equal(Prefix.Concat(new[]
        {
            "-buffer_duration", "50", "-f", "pulse", "-device", "e2e", "Pixelbadger Toolkit"
        }));
    }

    [Fact]
    public void BuildArguments_ShouldUseDeviceIndex_WhenFormatIsAudiotoolbox()
    {
        Args("audiotoolbox", "2").Should().Equal(Prefix.Concat(new[]
        {
            "-f", "audiotoolbox", "-audio_device_index", "2", "-"
        }));
    }

    [Theory]
    [InlineData("sdl")]
    [InlineData("sdl2")]
    public void BuildArguments_ShouldIgnoreDevice_WhenFormatIsSdl(string format)
    {
        Args(format, "ignored").Should().Equal(Prefix.Concat(new[]
        {
            "-f", format, "Pixelbadger Toolkit"
        }));
    }

    [Fact]
    public void BuildArguments_ShouldUseDeviceAsTarget_WhenFormatIsOther()
    {
        Args("wav", "/tmp/out.wav").Should().Equal(Prefix.Concat(new[] { "-f", "wav", "/tmp/out.wav" }));
    }

    [Fact]
    public void BuildArguments_ShouldPassFormatAndDeviceWithSpacesVerbatim_WhenCustom()
    {
        var args = Args("alsa", "hw:0,0 x");

        args.Should().EndWith(new[] { "-f", "alsa", "hw:0,0 x" });
    }

    [Fact]
    public void Open_ShouldThrow_WhenFfmpegNotFound()
    {
        var factory = new FfmpegPlaybackSinkFactory(() => null);

        var act = () => factory.Open(new AudioPlaybackOptions("s16le", "x"));

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "ffmpeg was not found on PATH. Install ffmpeg (e.g. 'apt install ffmpeg' or 'brew install ffmpeg').");
    }

    [Fact]
    public void Open_ShouldThrowAudioPlaybackException_WhenBinaryCannotStart()
    {
        var factory = new FfmpegPlaybackSinkFactory(() => _dir.Combine("no-such-ffmpeg"));

        var act = () => factory.Open(new AudioPlaybackOptions("s16le", "x"));

        act.Should().Throw<AudioPlaybackException>().WithMessage("Could not start ffmpeg:*");
    }

    [Fact]
    public async Task WriteAsync_ShouldDeliverExactBytes_WhenWritingToS16leFile()
    {
        var target = _dir.Combine("out.s16le");
        var input = new short[9600];
        for (var i = 0; i < input.Length; i++)
            input[i] = (short)((i * 37) % 60000 - 30000);
        var factory = new FfmpegPlaybackSinkFactory(() => Ffmpeg);

        await using (var sink = factory.Open(new AudioPlaybackOptions("s16le", target)))
        {
            await sink.WriteAsync(input.AsMemory(0, 4000), CancellationToken.None);
            await sink.WriteAsync(input.AsMemory(4000), CancellationToken.None);
            await sink.CompleteAsync(CancellationToken.None);
        }

        File.ReadAllBytes(target).Should().Equal(MemoryMarshal.AsBytes(input.AsSpan()).ToArray());
    }

    [Fact]
    public async Task CompleteAsync_ShouldBeIdempotent_WhenCalledTwice()
    {
        var target = _dir.Combine("out.s16le");
        await using var sink = new FfmpegPlaybackSinkFactory(() => Ffmpeg).Open(new AudioPlaybackOptions("s16le", target));
        await sink.WriteAsync(new short[200], CancellationToken.None);

        await sink.CompleteAsync(CancellationToken.None);
        await sink.CompleteAsync(CancellationToken.None);

        new FileInfo(target).Length.Should().Be(400);
    }

    [Fact]
    public async Task WriteAndComplete_ShouldThrowAudioPlaybackException_WhenFfmpegExitsNonZero()
    {
        await using var sink = new FfmpegPlaybackSinkFactory(() => Ffmpeg)
            .Open(new AudioPlaybackOptions("no-such-format", _dir.Combine("x")));

        var act = async () =>
        {
            for (var i = 0; i < 50; i++)
                await sink.WriteAsync(new short[48000], CancellationToken.None);
            await sink.CompleteAsync(CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<AudioPlaybackException>();
        ex.Which.Message.Should().StartWith("ffmpeg playback failed (exit ");
        ex.Which.Message.Should().MatchRegex(@"^ffmpeg playback failed \(exit [1-9]\d*\): .+");
        ex.Which.Message.Should().NotContain("no error output");
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrowAudioPlaybackException_WhenFfmpegExitsNonZeroAfterNoWrites()
    {
        await using var sink = new FfmpegPlaybackSinkFactory(() => Ffmpeg)
            .Open(new AudioPlaybackOptions("no-such-format", _dir.Combine("x")));

        var act = () => sink.CompleteAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AudioPlaybackException>();
        ex.Which.Message.Should().MatchRegex(@"^ffmpeg playback failed \(exit [1-9]\d*\): .+");
    }

    [Fact]
    public async Task DisposeAsync_ShouldKillRunningFfmpeg_WhenStillRunning()
    {
        var sink = (FfmpegPlaybackSink)new FfmpegPlaybackSinkFactory(() => Ffmpeg)
            .Open(new AudioPlaybackOptions("null", "-"));
        var pid = sink.ProcessId;
        await sink.WriteAsync(new short[2000], CancellationToken.None);

        await sink.DisposeAsync();

        sink.ProcessExitedAfterDispose.Should().BeTrue();
        ProcessIsGone(pid).Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotentAndNotThrow_WhenCalledTwice()
    {
        var sink = new FfmpegPlaybackSinkFactory(() => Ffmpeg).Open(new AudioPlaybackOptions("null", "-"));

        await sink.DisposeAsync();
        var act = async () => await sink.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteAsync_ShouldThrowOperationCanceled_WhenTokenAlreadyCancelled()
    {
        await using var sink = new FfmpegPlaybackSinkFactory(() => Ffmpeg).Open(new AudioPlaybackOptions("null", "-"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sink.WriteAsync(new short[100], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WriteAsync_ShouldThrowOperationCanceled_WhenCancelledWhilePipeIsBlocked()
    {
        if (OperatingSystem.IsWindows())
            return; // the wrapper below is a /bin/sh script

        // `-re` makes ffmpeg consume input in real time, so writing far ahead of it blocks on the pipe.
        var script = _dir.Combine("slow-ffmpeg.sh");
        File.WriteAllText(script, $"#!/bin/sh\nexec \"{Ffmpeg}\" -re \"$@\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var sink = new FfmpegPlaybackSinkFactory(() => script).Open(new AudioPlaybackOptions("null", "-"));
        using var cts = new CancellationTokenSource();
        try
        {
            var blocked = Task.Run(async () =>
            {
                var chunk = new short[48000 * 2];
                while (true)
                    await sink.WriteAsync(chunk, cts.Token);
            });
            await Task.Delay(1000);
            blocked.IsCompleted.Should().BeFalse();

            cts.Cancel();
            // 5 s is a hang guard only; the assertion is that the blocked write ends with a cancellation.
            var act = async () => await blocked.WaitAsync(TimeSpan.FromSeconds(5));
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    private static bool ProcessIsGone(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
