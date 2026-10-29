using System.Diagnostics;
using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class FfmpegCaptureSourceTests
{
    private static string Ffmpeg => FfmpegLocator.Resolve() ?? throw new InvalidOperationException("ffmpeg is required for these tests");

    private static FfmpegCaptureSource Start(string device, int channels = 1, string? ffmpeg = null) =>
        FfmpegCaptureSource.Start(new FfmpegCaptureOptions(ffmpeg ?? Ffmpeg, "lavfi", device, channels));

    private static async Task<int> ReadAll(IAudioCaptureSource source, int bufferSize = 4096)
    {
        var buffer = new short[bufferSize];
        var total = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, CancellationToken.None)) > 0)
            total += n;
        return total;
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnExactSamplesThenZero_WhenMono()
    {
        await using var source = Start("sine=frequency=440:duration=0.5");
        source.SampleRate.Should().Be(48000);
        source.Channels.Should().Be(1);
        (await ReadAll(source)).Should().Be(24000);
        (await source.ReadAsync(new short[16], CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnInterleavedSamples_WhenStereo()
    {
        await using var source = Start("sine=frequency=440:duration=0.5", 2);
        source.Channels.Should().Be(2);
        (await ReadAll(source)).Should().Be(48000);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnOnlyWholeSamples_WhenBufferIsOdd()
    {
        await using var source = Start("sine=frequency=440:duration=0.1");
        (await ReadAll(source, 7)).Should().Be(4800);
    }

    [Fact]
    public async Task ReadAsync_ShouldProduceNonSilentSine_WhenReading()
    {
        await using var source = Start("sine=frequency=440:duration=0.2");
        var buffer = new short[4800];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await source.ReadAsync(buffer.AsMemory(read), CancellationToken.None);
            if (n == 0) break;
            read += n;
        }

        read.Should().Be(4800);
        buffer.Max(s => (int)s).Should().BeGreaterThan(1000);
        buffer.Min(s => (int)s).Should().BeLessThan(-1000);
    }

    [Fact]
    public async Task ReadAsync_ShouldThrowAudioCaptureException_WhenFfmpegFails()
    {
        await using var source = Start("nonexistent_filter_xyz");
        var act = async () => await ReadAll(source);
        var ex = await act.Should().ThrowAsync<AudioCaptureException>();
        ex.Which.Message.Should().Contain("ffmpeg capture failed (exit ");
        ex.Which.Message.Should().MatchRegex(@"^ffmpeg capture failed \(exit [1-9]\d*\): \S");
    }

    [Fact]
    public void Start_ShouldThrowAudioCaptureException_WhenExecutableMissing()
    {
        var act = () => FfmpegCaptureSource.Start(new FfmpegCaptureOptions("/nonexistent/ffmpeg-xyz", "lavfi", "sine", 1));
        act.Should().Throw<AudioCaptureException>().WithMessage("Could not start ffmpeg:*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Start_ShouldThrow_WhenChannelsInvalid(int channels)
    {
        var act = () => Start("sine", channels);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DisposeAsync_ShouldKillProcessAndBeIdempotent_WhenSourceIsEndless()
    {
        var source = Start("sine=frequency=440");
        var buffer = new short[1024];
        (await source.ReadAsync(buffer, CancellationToken.None)).Should().BeGreaterThan(0);
        var pid = source.ProcessId;

        await source.DisposeAsync();
        await source.DisposeAsync();

        source.ProcessExitedAfterDispose.Should().BeTrue();
        var gone = false;
        try
        {
            using var p = Process.GetProcessById(pid);
            gone = p.HasExited;
        }
        catch (ArgumentException)
        {
            gone = true;
        }

        gone.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ShouldKillWholeProcessTree_WhenWrapperSpawnsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pidFile = Path.Combine(dir, "child.pid");
            var wrapper = Path.Combine(dir, "ffmpeg-wrapper.sh");
            await File.WriteAllTextAsync(wrapper, $"#!/bin/sh\nsleep 300 &\necho $! > '{pidFile}'\nwait\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var source = Start("ignored", 1, wrapper);
            for (var i = 0; i < 100 && !File.Exists(pidFile); i++)
                await Task.Delay(50);
            File.Exists(pidFile).Should().BeTrue();
            var childPid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());

            await source.DisposeAsync();

            var alive = true;
            for (var i = 0; i < 100 && alive; i++)
            {
                try
                {
                    using var child = Process.GetProcessById(childPid);
                    alive = !child.HasExited;
                }
                catch (ArgumentException)
                {
                    alive = false;
                }

                if (alive) await Task.Delay(50);
            }

            alive.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ReadAsync_ShouldThrowOperationCanceled_WhenTokenAlreadyCancelled()
    {
        await using var source = Start("sine=frequency=440");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await source.ReadAsync(new short[1024], cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_ShouldThrowOperationCanceled_WhenCancelledWhileWaitingForData()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var wrapper = Path.Combine(dir, "silent.sh");
            await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nexec sleep 300\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            await using var source = Start("ignored", 1, wrapper);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var act = async () => await source.ReadAsync(new short[1024], cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ReadAsync_ShouldThrowObjectDisposed_WhenDisposed()
    {
        var source = Start("sine=frequency=440:duration=0.1");
        await source.DisposeAsync();
        var act = async () => await source.ReadAsync(new short[16], CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
