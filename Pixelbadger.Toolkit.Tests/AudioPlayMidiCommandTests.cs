using System.CommandLine;
using System.Text.Json;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class AudioPlayMidiCommandTests : IDisposable
{
    private const string Ffmpeg = "ffmpeg was not found on PATH. Install ffmpeg (e.g. 'apt install ffmpeg' or 'brew install ffmpeg').";

    private readonly MidiTestAssets.TempDirectory _temp = new();
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly InMemoryConfigStore _store = new();

    public AudioPlayMidiCommandTests()
    {
        ToolkitConsole.InstallRouting();
    }

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        _temp.Dispose();
    }

    private string Mid() => MidiTestAssets.WriteMidi(_temp.Path, "song.mid", (60, 0.0, 0.5), (64, 0.5, 0.5));
    private string Sf2(Waveform waveform = Waveform.Sine, string name = "bank.sf2") => MidiTestAssets.WriteSoundFont(_temp.Path, name, waveform);

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

    private static byte[] Render(string mid, string sf2)
    {
        using var source = new MeltySynthSourceFactory().Open(mid, sf2);
        var left = new float[1024];
        var right = new float[1024];
        var bytes = new List<byte>();
        int n;
        while ((n = source.Render(left, right)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                bytes.AddRange(BitConverter.GetBytes(MidiPlayComponent.ToSample(left[i])));
                bytes.AddRange(BitConverter.GetBytes(MidiPlayComponent.ToSample(right[i])));
            }
        }

        return bytes.ToArray();
    }

    [Fact]
    public void Create_ShouldDeclarePlayMidiOptionsAndDescriptions_WhenBuilt()
    {
        var play = AudioCommand.Create().Subcommands.Single(c => c.Name == "play-midi");

        play.Description.Should().Be("Play a MIDI file live through the system audio output using a SoundFont (SF2) synthesiser");
        play.Options.Where(o => o.Required).Select(o => o.Name).Should().BeEquivalentTo("--file", "--soundfont");
        play.Options.Select(o => o.Name).Should().BeEquivalentTo("--file", "--soundfont", "--output-format", "--device");
        Description(play, "--file").Should().Be("MIDI file (.mid) to play");
        Description(play, "--soundfont").Should().Be("SoundFont (.sf2) whose instruments synthesise the MIDI file");
        Description(play, "--output-format").Should().Be("ffmpeg output format used for playback (default: pulse on Linux, audiotoolbox on macOS, sdl2 on Windows)");
        Description(play, "--device").Should().Be("Output device passed to ffmpeg (default: the system default output; pulse sink name on Linux, audiotoolbox device index on macOS; ignored by sdl2)");
    }

    private static string? Description(Command command, string name) => command.Options.Single(o => o.Name == name).Description;

    [Fact]
    public void Build_ShouldMakeOnlyOutputFormatAndDeviceConfigurable_WhenTreeBuilt()
    {
        var tree = ConfigTree.Build(ToolkitCommandTree.Create(_store));

        tree.Find("audio", "play-midi")!.Options.Keys.Should().BeEquivalentTo("output-format", "device");
    }

    [Fact]
    public void Build_ShouldExposePlayMidiAsMcpTool_WhenCatalogueBuilt()
    {
        var root = ToolkitCommandTree.Create(_store);

        var tool = McpToolCatalogue.Build(root, McpEntryResolver.Resolve(root, "audio/play-midi")!).Single();

        tool.Name.Should().Be("audio_play-midi");
        var schema = JsonSerializer.SerializeToNode(tool.InputSchema)!;
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("file", "soundfont");
        schema["properties"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("file", "soundfont", "output-format", "device");
    }

    [Fact]
    public async Task Invoke_ShouldPrintPlainLinesAndWriteRenderedAudio_WhenOutputFormatIsS16leFile()
    {
        var mid = Mid();
        var sf2 = Sf2();
        var target = _temp.Combine("out.s16le");
        var expected = Render(mid, sf2);

        var (code, output) = await RunAsync("audio", "play-midi", "--file", mid, "--soundfont", sf2, "--output-format", "s16le", "--device", target);

        code.Should().Be(0, output);
        output.Should().Be(
            $"Playing '{mid}' (0:01) with '{sf2}'{Environment.NewLine}Played '{mid}' (0:01){Environment.NewLine}");
        var written = await File.ReadAllBytesAsync(target);
        written.Length.Should().Be(expected.Length);
        written.Length.Should().BeGreaterThan(48000 * 4);
        written.Should().Equal(expected);
        written.Any(b => b != 0).Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_ShouldWriteDifferentAudio_WhenSoundFontDiffers()
    {
        var mid = Mid();
        var sine = _temp.Combine("sine.s16le");
        var square = _temp.Combine("square.s16le");

        (await RunAsync("audio", "play-midi", "--file", mid, "--soundfont", Sf2(Waveform.Sine, "a.sf2"), "--output-format", "s16le", "--device", sine)).Code.Should().Be(0);
        (await RunAsync("audio", "play-midi", "--file", mid, "--soundfont", Sf2(Waveform.Square, "b.sf2"), "--output-format", "s16le", "--device", square)).Code.Should().Be(0);

        (await File.ReadAllBytesAsync(sine)).Should().NotEqual(await File.ReadAllBytesAsync(square));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnPlainLines_WhenCalledOverMcp()
    {
        var mid = Mid();
        var sf2 = Sf2();
        var target = _temp.Combine("mcp.s16le");
        var root = ToolkitCommandTree.Create(_store);
        var tool = McpToolCatalogue.Build(root, McpEntryResolver.Resolve(root, "audio/play-midi")!).Single();
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            $$"""{"file":{{JsonSerializer.Serialize(mid)}},"soundfont":{{JsonSerializer.Serialize(sf2)}},"output-format":"s16le","device":{{JsonSerializer.Serialize(target)}}}""");

        var result = await new McpToolExecutor(_store).ExecuteAsync(tool, args);

        result.IsError.Should().BeFalse(result.Text);
        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain($"Playing '{mid}' (0:01) with '{sf2}'").And.Contain($"Played '{mid}' (0:01)");
        File.ReadAllBytes(target).Should().Equal(Render(mid, sf2));
    }

    [Fact]
    public async Task Invoke_ShouldReportMissingMidi_WhenFileDoesNotExist()
    {
        var missing = _temp.Combine("nope.mid");

        var (code, output) = await RunAsync("audio", "play-midi", "--file", missing, "--soundfont", Sf2(), "--output-format", "s16le", "--device", _temp.Combine("o"));

        code.Should().Be(1);
        output.Trim().Should().Be($"Error: File '{missing}' not found");
        File.Exists(_temp.Combine("o")).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldReportMissingSoundFont_WhenSoundFontDoesNotExist()
    {
        var missing = _temp.Combine("nope.sf2");

        var (code, output) = await RunAsync("audio", "play-midi", "--file", Mid(), "--soundfont", missing, "--output-format", "s16le", "--device", _temp.Combine("o"));

        code.Should().Be(1);
        output.Trim().Should().Be($"Error: SoundFont '{missing}' not found");
        File.Exists(_temp.Combine("o")).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldReportUnreadableMidi_WhenFileIsGarbage()
    {
        var garbage = _temp.Combine("bad.mid");
        File.WriteAllText(garbage, "this is not midi");

        var (code, output) = await RunAsync("audio", "play-midi", "--file", garbage, "--soundfont", Sf2(), "--output-format", "s16le", "--device", _temp.Combine("o"));

        code.Should().Be(1);
        output.Trim().Should().StartWith($"Error: Could not read MIDI file '{garbage}': ");
        File.Exists(_temp.Combine("o")).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldReportUnreadableSoundFont_WhenFileIsGarbage()
    {
        var garbage = _temp.Combine("bad.sf2");
        File.WriteAllText(garbage, "this is not a soundfont");

        var (code, output) = await RunAsync("audio", "play-midi", "--file", Mid(), "--soundfont", garbage, "--output-format", "s16le", "--device", _temp.Combine("o"));

        code.Should().Be(1);
        output.Trim().Should().StartWith($"Error: Could not read SoundFont '{garbage}': ");
        File.Exists(_temp.Combine("o")).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldReportMissingFfmpeg_WhenFfmpegCannotBeLocated()
    {
        using var seam = AudioSeams.Override(ffmpegLocator: () => null);

        var (code, output) = await RunAsync("audio", "play-midi", "--file", Mid(), "--soundfont", Sf2());

        code.Should().Be(1);
        output.Trim().Should().Be("Error: " + Ffmpeg);
    }

    [Fact]
    public async Task Invoke_ShouldReportFfmpegFailure_WhenOutputFormatIsInvalid()
    {
        var (code, output) = await RunAsync("audio", "play-midi", "--file", Mid(), "--soundfont", Sf2(), "--output-format", "not_a_format", "--device", _temp.Combine("o"));

        code.Should().Be(1);
        output.Should().Contain("Error: ffmpeg playback failed (exit ");
    }

    [Fact]
    public async Task Invoke_ShouldUseConfiguredOutputFormat_WhenDefaultsAreStored()
    {
        var mid = Mid();
        var sf2 = Sf2();
        var target = _temp.Combine("cfg.s16le");
        (await RunAsync("config", "audio", "play-midi", "--output-format", "s16le", "--device", target)).Code.Should().Be(0);

        var (code, output) = await RunAsync("audio", "play-midi", "--file", mid, "--soundfont", sf2);

        code.Should().Be(0, output);
        File.ReadAllBytes(target).Should().Equal(Render(mid, sf2));
    }
}
