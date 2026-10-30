using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>
/// End-to-end tests against the built tool. ffmpeg's lavfi input stands in for the microphone, and ffprobe / ffmpeg's
/// native opus decoder verify the files independently.
/// </summary>
public class AudioCommandIntegrationTests : IDisposable
{
    private readonly string _dir;

    public AudioCommandIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static string Sine(double frequency) => $"sine=frequency={frequency.ToString(CultureInfo.InvariantCulture)}";

    // lavfi produces audio faster than real time; arealtime paces it like a microphone for tests that stop a capture by hand.
    private static string RealtimeSine(double frequency) => Sine(frequency) + ",arealtime";

    private string Path_(string name) => Path.Combine(_dir, name);

    private static async Task<(int ExitCode, string Out, string Err)> Pbtk(params string[] args) =>
        await ToolkitProcess.RunAsync(null, args);

    private static string[] Capture(double frequency, string duration) =>
        ["--capture-format", "lavfi", "--device", Sine(frequency), "--duration", duration];

    private async Task<string> RecordAsync(string name, double frequency, string duration, params string[] extra)
    {
        var path = Path_(name);
        var r = await Pbtk(["audio", "record", "--out-file", path, .. Capture(frequency, duration), .. extra]);
        r.ExitCode.Should().Be(0, r.Out + r.Err);
        return path;
    }

    private static OggOpusFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return OggOpusFile.Read(stream);
    }

    private static void AssertDurationConsistent(string path, long expectedSamples)
    {
        Load(path).DurationSamples.Should().Be(expectedSamples);
        AudioTestSupport.DecodeWithFfmpeg(path).Should().HaveCount((int)expectedSamples);
        // ffprobe reports the container duration including the 312-sample pre-skip (6.5 ms).
        AudioTestSupport.Probe(path).Duration.Should().BeApproximately(expectedSamples / 48000.0 + 312 / 48000.0, 0.002);
    }

    [Fact]
    public async Task Record_ShouldWriteTwoSecondMonoFileThatFfprobeAndFfmpegAccept_WhenLavfiSineCaptured()
    {
        var path = Path_("rec.opus");

        var r = await Pbtk(["audio", "record", "--out-file", path, .. Capture(440, "2s")]);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Recorded '{path}': 2.000s{Environment.NewLine}");
        r.Err.Should().BeEmpty();
        var probe = AudioTestSupport.Probe(path);
        probe.Codec.Should().Be("opus");
        probe.Channels.Should().Be(1);
        probe.Duration.Should().BeApproximately(2.0, 0.02);
        var decoded = AudioTestSupport.DecodeWithFfmpeg(path);
        decoded.Should().HaveCount(96000);
        AudioTestSupport.IsDominant(decoded, 0.1, 1.9, 440, 880).Should().BeTrue();
    }

    [Fact]
    public async Task Record_ShouldWriteStereoFile_WhenChannelsIsTwo()
    {
        var path = await RecordAsync("stereo.opus", 440, "1s", "--channels", "2", "--bitrate", "96000");

        AudioTestSupport.Probe(path).Channels.Should().Be(2);
        Load(path).Head.ChannelCount.Should().Be(2);
        Load(path).DurationSamples.Should().Be(48000);
    }

    [Fact]
    public async Task Record_ShouldMatchWavLength_WhenFromWavAt24kHz()
    {
        var wav = Path_("in.wav");
        var gen = OggTestSupport.Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1.5:sample_rate=24000", "-c:a", "pcm_s16le", wav);
        gen.ExitCode.Should().Be(0, gen.StdErr);
        var path = Path_("fromwav.opus");

        var r = await Pbtk("audio", "record", "--out-file", path, "--from-wav", wav);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Recorded '{path}': 1.500s{Environment.NewLine}");
        AudioTestSupport.Probe(path).Duration.Should().BeApproximately(1.5, 0.02);
        var decoded = AudioTestSupport.DecodeWithFfmpeg(path);
        decoded.Should().HaveCount(72000);
        AudioTestSupport.IsDominant(decoded, 0.1, 1.4, 440, 880).Should().BeTrue();
    }

    [Fact]
    public async Task Record_ShouldTruncateWav_WhenFromWavAndDurationGiven()
    {
        var wav = Path_("in.wav");
        AudioTestSupport.WriteWav(wav, 48000, 1, AudioTestSupport.Tone(440, 96000));
        var path = Path_("cut.opus");

        var r = await Pbtk("audio", "record", "--out-file", path, "--from-wav", wav, "--duration", "0.75");

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Recorded '{path}': 0.750s{Environment.NewLine}");
    }

    [Fact]
    public async Task Insert_ShouldInsertOneSecondAt880Hz_WhenInsertingMidFile()
    {
        var path = await RecordAsync("memo.opus", 440, "2s");
        var before = Load(path);

        var r = await Pbtk(["audio", "insert", "--file", path, "--at", "1.0", .. Capture(880, "1s")]);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Inserted 1.000s at 0.994s (requested 1.000s) into '{path}': 3.020s{Environment.NewLine}");
        AssertDurationConsistent(path, 144960);
        var after = Load(path);
        after.AudioPackets.Take(50).Should().BeEquivalentTo(before.AudioPackets.Take(50), o => o.WithStrictOrdering());
        after.AudioPackets.Skip(50 + 51).Should().BeEquivalentTo(before.AudioPackets.Skip(50), o => o.WithStrictOrdering());
        after.HeadPacket.Should().Equal(before.HeadPacket);
        after.TagsPacket.Should().Equal(before.TagsPacket);
        var decoded = AudioTestSupport.DecodeWithFfmpeg(path);
        AudioTestSupport.IsDominant(decoded, 0.1, 0.9, 440, 880).Should().BeTrue("0 to 1 s is the original 440 Hz");
        AudioTestSupport.IsDominant(decoded, 1.06, 1.94, 880, 440).Should().BeTrue("the inserted span is 880 Hz");
        AudioTestSupport.IsDominant(decoded, 2.08, 3.0, 440, 880).Should().BeTrue("the remainder is the original 440 Hz");
    }

    [Fact]
    public async Task Insert_ShouldSnapAndPrintSnappedAndRequestedTimes_WhenRequestedBetweenBoundaries()
    {
        var path = await RecordAsync("memo.opus", 440, "2s");

        var r = await Pbtk(["audio", "insert", "--file", path, "--at", "1.013", .. Capture(880, "0.5s")]);

        r.ExitCode.Should().Be(0, r.Err);
        // 1.013 s = 48624 samples; the nearest boundary is 48648 (= 51 * 960 - 312) = 1.0135 s.
        r.Out.Should().Be($"Inserted 0.500s at {(48648 / 48000.0).ToString("0.000", CultureInfo.InvariantCulture)}s (requested 1.013s) into '{path}': 2.520s{Environment.NewLine}");
        AssertDurationConsistent(path, 96000 + 26 * 960);
    }

    [Fact]
    public async Task Insert_ShouldAppend_WhenAtEqualsDuration()
    {
        var path = await RecordAsync("memo.opus", 440, "2s");

        var r = await Pbtk(["audio", "insert", "--file", path, "--at", "2", .. Capture(880, "0.5s")]);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Inserted 0.500s at 2.000s (requested 2.000s) into '{path}': 2.520s{Environment.NewLine}");
        AssertDurationConsistent(path, 96000 + 648 + 24000 + 312);
        var decoded = AudioTestSupport.DecodeWithFfmpeg(path);
        AudioTestSupport.IsDominant(decoded, 0.1, 1.9, 440, 880).Should().BeTrue();
        AudioTestSupport.IsDominant(decoded, 2.1, 2.45, 880, 440).Should().BeTrue();
    }

    [Fact]
    public async Task Overwrite_ShouldReplaceHalfSecondKeepingLength_WhenOverwritingMidFile()
    {
        var path = await RecordAsync("memo.opus", 440, "2s");
        var before = Load(path);

        var r = await Pbtk(["audio", "overwrite", "--file", path, "--at", "1.0", .. Capture(880, "0.5s")]);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Overwrote 0.500s at 0.994s (requested 1.000s) in '{path}': 2.000s{Environment.NewLine}");
        AssertDurationConsistent(path, 96000);
        var after = Load(path);
        after.AudioPackets.Should().HaveCount(before.AudioPackets.Count);
        after.AudioPackets.Take(50).Should().BeEquivalentTo(before.AudioPackets.Take(50), o => o.WithStrictOrdering());
        after.AudioPackets.Skip(75).Should().BeEquivalentTo(before.AudioPackets.Skip(75), o => o.WithStrictOrdering());
        after.AudioPackets.Skip(50).Take(25).Zip(before.AudioPackets.Skip(50).Take(25)).Should().OnlyContain(p => !p.First.SequenceEqual(p.Second));
        var decoded = AudioTestSupport.DecodeWithFfmpeg(path);
        AudioTestSupport.IsDominant(decoded, 0.1, 0.9, 440, 880).Should().BeTrue();
        AudioTestSupport.IsDominant(decoded, 1.06, 1.44, 880, 440).Should().BeTrue("the replaced span is 880 Hz");
        AudioTestSupport.IsDominant(decoded, 1.56, 1.95, 440, 880).Should().BeTrue("original audio resumes after the replaced span");
    }

    [Fact]
    public async Task Overwrite_ShouldGrowFileToExactLength_WhenNewAudioReachesPastTheEnd()
    {
        var path = await RecordAsync("memo.opus", 440, "2s");

        var r = await Pbtk(["audio", "overwrite", "--file", path, "--at", "1.5", .. Capture(880, "1s")]);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Overwrote 1.000s at 1.494s (requested 1.500s) in '{path}': 2.500s{Environment.NewLine}");
        AssertDurationConsistent(path, 71688 + 312 + 48000);
        var decoded = AudioTestSupport.DecodeWithFfmpeg(path);
        AudioTestSupport.IsDominant(decoded, 0.1, 1.4, 440, 880).Should().BeTrue();
        AudioTestSupport.IsDominant(decoded, 1.56, 2.44, 880, 440).Should().BeTrue();
    }

    [Fact]
    public async Task Overwrite_ShouldAppend_WhenAtEqualsDuration()
    {
        var path = await RecordAsync("memo.opus", 440, "2s");

        var r = await Pbtk(["audio", "overwrite", "--file", path, "--at", "2", .. Capture(880, "0.5s")]);

        r.ExitCode.Should().Be(0, r.Err);
        r.Out.Should().Be($"Overwrote 0.500s at 2.000s (requested 2.000s) in '{path}': 2.520s{Environment.NewLine}");
        AssertDurationConsistent(path, 96000 + 648 + 24000 + 312);
    }

    [Fact]
    public async Task Insert_ShouldLeaveOriginalByteIdenticalAndNoTempFile_WhenWavRateUnsupported()
    {
        var path = await RecordAsync("memo.opus", 440, "1s");
        var original = await File.ReadAllBytesAsync(path);
        var wav = Path_("odd.wav");
        AudioTestSupport.WriteWav(wav, 22050, 1, AudioTestSupport.Tone(880, 22050));

        var r = await Pbtk("audio", "insert", "--file", path, "--at", "0.5", "--from-wav", wav);

        r.ExitCode.Should().Be(1);
        r.Out.Trim().Should().Be("Error: Unsupported WAV sample rate 22050 Hz. Supported rates: 8000, 12000, 16000, 24000, 48000 Hz. Resample first, e.g. ffmpeg -i in.wav -ar 48000 out.wav");
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Overwrite_ShouldLeaveOriginalByteIdenticalAndNoTempFile_WhenCaptureDeviceFails()
    {
        var path = await RecordAsync("memo.opus", 440, "1s");
        var original = await File.ReadAllBytesAsync(path);

        var r = await Pbtk("audio", "overwrite", "--file", path, "--at", "0.5", "--capture-format", "lavfi", "--device", "nonexistent_filter_xyz", "--duration", "1s");

        r.ExitCode.Should().Be(1);
        r.Out.Should().StartWith("Error: ffmpeg capture failed (exit ");
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Insert_ShouldFailWithoutTouchingFile_WhenAtBeyondEnd()
    {
        var path = await RecordAsync("memo.opus", 440, "1s");
        var original = await File.ReadAllBytesAsync(path);

        var r = await Pbtk(["audio", "insert", "--file", path, "--at", "1.5", .. Capture(880, "1s")]);

        r.ExitCode.Should().Be(1);
        r.Out.Trim().Should().Be($"Error: Time 1.500s is beyond the end of '{path}' (1.000s)");
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    [Fact]
    public async Task Record_ShouldRefuseAndExitOne_WhenNoDurationAndStdinRedirected()
    {
        var path = Path_("never.opus");

        var r = await ToolkitProcess.RunWithStdinAsync(null, "", "audio", "record", "--out-file", path, "--capture-format", "lavfi", "--device", Sine(440));

        r.ExitCode.Should().Be(1);
        r.StandardOutput.Trim().Should().Be("Error: --duration or --from-wav is required when no interactive console is available");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Record_ShouldWriteCompleteFileAndExitZero_WhenCtrlCStopsCaptureOnTerminal()
    {
        // The tool needs a terminal on stdin to wait for Enter, so run it under `script`, which provides a pty.
        var path = Path_("ctrlc.opus");
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Pixelbadger.Toolkit/bin",
            new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('/')).Parent!.Name, "net10.0/Pixelbadger.Toolkit.dll"));
        var configHome = Path_("config");
        Directory.CreateDirectory(configHome);
        var psi = new ProcessStartInfo("script")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["TERM"] = "xterm";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["XDG_CONFIG_HOME"] = configHome;
        psi.Environment["APPDATA"] = configHome;
        foreach (var a in new[]
                 {
                     "-qefc",
                     $"dotnet \"{dll}\" audio record --out-file \"{path}\" --capture-format lavfi --device \"{RealtimeSine(440)}\"",
                     "/dev/null"
                 })
            psi.ArgumentList.Add(a);

        using var script = Process.Start(psi)!;
        var stdout = new System.Text.StringBuilder();
        script.OutputDataReceived += (_, e) => { lock (stdout) stdout.AppendLine(e.Data); };
        script.BeginOutputReadLine();
        var stderr = script.StandardError.ReadToEndAsync();

        await WaitUntilAsync(() => { lock (stdout) return stdout.ToString().Contains("Recording..."); }, "the Recording... message");
        await Task.Delay(1000);
        var tool = FindProcess(p => p.Command.StartsWith("dotnet", StringComparison.Ordinal) && p.Command.Contains(path));
        tool.Should().NotBeNull("the tool process must be running under script");
        var targets = new List<int> { tool!.Value.Pid };
        targets.AddRange(ChildrenOf(tool.Value.Pid));
        // A terminal Ctrl+C reaches the tool and its ffmpeg child alike.
        foreach (var pid in targets) Signal(pid);

        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await script.WaitForExitAsync(exitTimeout.Token);
        var text = stdout.ToString() + await stderr;

        script.ExitCode.Should().Be(0, text);
        text.Should().Contain($"Recorded '{path}': ");
        var file = Load(path);
        var seconds = file.DurationSamples / 48000.0;
        seconds.Should().BeInRange(0.5, 3.0);
        AudioTestSupport.Probe(path).Codec.Should().Be("opus");
        AudioTestSupport.DecodeWithFfmpeg(path).Should().HaveCount((int)file.DurationSamples);
        text.Should().Contain($"Recorded '{path}': {seconds.ToString("0.000", CultureInfo.InvariantCulture)}s");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}");
            await Task.Delay(50);
        }
    }

    private static List<(int Pid, string Command)> ListProcesses()
    {
        var result = new List<(int, string)>();
        foreach (var dir in Directory.GetDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                result.Add((pid, File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ')));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return result;
    }

    private static (int Pid, string Command)? FindProcess(Func<(int Pid, string Command), bool> predicate)
    {
        foreach (var p in ListProcesses())
            if (predicate(p)) return p;
        return null;
    }

    private static IEnumerable<int> ChildrenOf(int pid)
    {
        foreach (var dir in Directory.GetDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var child)) continue;
            string stat;
            try { stat = File.ReadAllText(Path.Combine(dir, "stat")); }
            catch (IOException) { continue; }
            var afterName = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            if (int.Parse(afterName[1], CultureInfo.InvariantCulture) == pid) yield return child;
        }
    }

    private static void Signal(int pid)
    {
        using var kill = Process.Start(new ProcessStartInfo("kill", $"-INT {pid}") { UseShellExecute = false })!;
        kill.WaitForExit();
    }
}
