using System.CommandLine;
using System.Text.Json;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Opus.Ogg;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class AudioCommandTests : IDisposable
{
    private readonly string _dir;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly InMemoryConfigStore _store = new();

    public AudioCommandTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
        ToolkitConsole.InstallRouting();
    }

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private async Task<(int Code, string Out)> RunAsync(params string[] args)
    {
        var root = ToolkitCommandTree.Create(_store);
        var output = new StringWriter();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, new StringWriter()))
        {
            var code = await ConfigBootstrap.InvokeAsync(root, args, _store);
            return (code, output.ToString());
        }
    }

    private string Wav(string name, int frames, double frequency = 440, int rate = 48000)
    {
        var path = Path.Combine(_dir, name);
        AudioTestSupport.WriteWav(path, rate, 1, AudioTestSupport.Tone(frequency, frames));
        return path;
    }

    private static OggOpusFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return OggOpusFile.Read(stream);
    }

    [Fact]
    public void Create_ShouldDefineRecordInsertOverwrite_WhenBuilt()
    {
        var audio = AudioCommand.Create();

        audio.Name.Should().Be("audio");
        audio.Subcommands.Select(c => c.Name).Should().Equal("record", "insert", "overwrite");
    }

    [Fact]
    public void Create_ShouldDeclareContractOptions_WhenBuilt()
    {
        var audio = AudioCommand.Create();
        var record = audio.Subcommands.Single(c => c.Name == "record");
        var insert = audio.Subcommands.Single(c => c.Name == "insert");
        var overwrite = audio.Subcommands.Single(c => c.Name == "overwrite");

        record.Options.Where(o => o.Required).Select(o => o.Name).Should().Equal("--out-file");
        record.Options.Select(o => o.Name).Should().Contain(
            ["--out-file", "--duration", "--from-wav", "--channels", "--bitrate", "--device", "--capture-format"]);
        foreach (var edit in new[] { insert, overwrite })
        {
            edit.Options.Where(o => o.Required).Select(o => o.Name).Should().BeEquivalentTo("--file", "--at");
            edit.Options.Select(o => o.Name).Should().Contain(["--file", "--at", "--duration", "--from-wav", "--bitrate", "--device", "--capture-format"]);
            edit.Options.Select(o => o.Name).Should().NotContain("--channels");
        }

        ((Option<int>)record.Options.Single(o => o.Name == "--channels")).GetDefaultValue().Should().Be(1);
        ((Option<int>)record.Options.Single(o => o.Name == "--bitrate")).GetDefaultValue().Should().Be(64000);
    }

    [Fact]
    public void Build_ShouldMakeEveryNonRequiredOptionConfigurable_WhenTreeBuilt()
    {
        var tree = ConfigTree.Build(ToolkitCommandTree.Create(_store));

        tree.Find("audio", "record")!.Options.Keys.Should().BeEquivalentTo("duration", "from-wav", "channels", "bitrate", "device", "capture-format");
        tree.Find("audio", "insert")!.Options.Keys.Should().BeEquivalentTo("duration", "from-wav", "bitrate", "device", "capture-format");
        tree.Find("audio", "overwrite")!.Options.Keys.Should().BeEquivalentTo("duration", "from-wav", "bitrate", "device", "capture-format");
    }

    [Fact]
    public async Task Invoke_ShouldRecordWavAndPrintExactLine_WhenFromWav()
    {
        var wav = Wav("in.wav", 24000, rate: 24000);
        var path = Path.Combine(_dir, "out.opus");

        var (code, output) = await RunAsync("audio", "record", "--out-file", path, "--from-wav", wav);

        code.Should().Be(0);
        output.Should().Be($"Recorded '{path}': 1.000s{Environment.NewLine}");
        Load(path).DurationSamples.Should().Be(48000);
    }

    [Fact]
    public async Task Invoke_ShouldTruncateWavToDuration_WhenFromWavAndDurationGiven()
    {
        var wav = Wav("in.wav", 96000);
        var path = Path.Combine(_dir, "out.opus");

        var (code, output) = await RunAsync("audio", "record", "--out-file", path, "--from-wav", wav, "--duration", "500ms");

        code.Should().Be(0);
        output.Should().Be($"Recorded '{path}': 0.500s{Environment.NewLine}");
    }

    [Fact]
    public async Task Invoke_ShouldRecordStereoAtBitrate_WhenChannelsAndBitrateGiven()
    {
        var wav = Wav("in.wav", 48000);
        var low = Path.Combine(_dir, "low.opus");
        var high = Path.Combine(_dir, "high.opus");

        (await RunAsync("audio", "record", "--out-file", low, "--from-wav", wav, "--channels", "2", "--bitrate", "8000")).Code.Should().Be(0);
        (await RunAsync("audio", "record", "--out-file", high, "--from-wav", wav, "--channels", "2", "--bitrate", "128000")).Code.Should().Be(0);

        Load(low).Head.ChannelCount.Should().Be(2);
        new FileInfo(high).Length.Should().BeGreaterThan(new FileInfo(low).Length);
    }

    [Fact]
    public async Task Invoke_ShouldInsertThenOverwrite_WhenEditingRecordedFile()
    {
        var path = Path.Combine(_dir, "memo.opus");
        await RunAsync("audio", "record", "--out-file", path, "--from-wav", Wav("a.wav", 96000));

        var insert = await RunAsync("audio", "insert", "--file", path, "--at", "1", "--from-wav", Wav("b.wav", 48000, 880));
        var overwrite = await RunAsync("audio", "overwrite", "--file", path, "--at", "1.0", "--from-wav", Wav("c.wav", 24000, 660));

        insert.Code.Should().Be(0);
        insert.Out.Should().Be($"Inserted 1.000s at 0.994s (requested 1.000s) into '{path}': 3.020s{Environment.NewLine}");
        overwrite.Code.Should().Be(0);
        overwrite.Out.Should().Be($"Overwrote 0.500s at 0.994s (requested 1.000s) in '{path}': 3.020s{Environment.NewLine}");
    }

    [Fact]
    public async Task Invoke_ShouldPrintErrorAndWriteNothing_WhenWavRateUnsupported()
    {
        var wav = Wav("odd.wav", 2205, rate: 22050);
        var path = Path.Combine(_dir, "out.opus");

        var (code, output) = await RunAsync("audio", "record", "--out-file", path, "--from-wav", wav);

        code.Should().Be(1);
        output.Trim().Should().Be("Error: Unsupported WAV sample rate 22050 Hz. Supported rates: 8000, 12000, 16000, 24000, 48000 Hz. Resample first, e.g. ffmpeg -i in.wav -ar 48000 out.wav");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldReportMissingFfmpeg_WhenPathHasNoFfmpeg()
    {
        var previous = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", _dir);
            var (code, output) = await RunAsync("audio", "record", "--out-file", Path.Combine(_dir, "o.opus"), "--duration", "1s");

            code.Should().Be(1);
            output.Trim().Should().Be("Error: ffmpeg was not found on PATH. Install ffmpeg (e.g. 'apt install ffmpeg' or 'brew install ffmpeg') or use --from-wav.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuseCaptureWithoutDuration_WhenCalledOverMcp()
    {
        var root = ToolkitCommandTree.Create(_store);
        var tool = McpToolCatalogue.Build(root, McpEntryResolver.Resolve(root, "audio/record")!).Single();
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            $$"""{"out-file":{{JsonSerializer.Serialize(Path.Combine(_dir, "mcp.opus"))}}}""");

        var result = await new McpToolExecutor(_store).ExecuteAsync(tool, args);

        result.IsError.Should().BeTrue();
        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Error: --duration or --from-wav is required when no interactive console is available");
        File.Exists(Path.Combine(_dir, "mcp.opus")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecordWav_WhenCalledOverMcpWithFromWav()
    {
        var root = ToolkitCommandTree.Create(_store);
        var tool = McpToolCatalogue.Build(root, McpEntryResolver.Resolve(root, "audio/record")!).Single();
        var path = Path.Combine(_dir, "mcp.opus");
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            $$"""{"out-file":{{JsonSerializer.Serialize(path)}},"from-wav":{{JsonSerializer.Serialize(Wav("m.wav", 48000))}}}""");

        var result = await new McpToolExecutor(_store).ExecuteAsync(tool, args);

        result.IsError.Should().BeFalse(result.Text);
        result.Text.Should().Contain($"Recorded '{path}': 1.000s");
        Load(path).DurationSamples.Should().Be(48000);
    }

    [Fact]
    public async Task Invoke_ShouldUseConfiguredBitrate_WhenConfigDefaultIsSet()
    {
        var wav = Wav("in.wav", 48000);
        var low = Path.Combine(_dir, "default.opus");
        var configured = Path.Combine(_dir, "configured.opus");
        await RunAsync("audio", "record", "--out-file", low, "--from-wav", wav);
        _store.Seed("audio", "record", "bitrate", "8000");

        (await RunAsync("audio", "record", "--out-file", configured, "--from-wav", wav)).Code.Should().Be(0);

        new FileInfo(configured).Length.Should().BeLessThan(new FileInfo(low).Length);
    }
}
