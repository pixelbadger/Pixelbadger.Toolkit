using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>Commands must return exit codes instead of terminating the process.</summary>
[Collection("AnsiConsole")]
public class CommandExitCodeTests : IDisposable
{
    private readonly string _dir;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;

    public CommandExitCodeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "null.json"), "null");
        File.WriteAllText(Path.Combine(_dir, "garbage.json"), "garbage");
        File.WriteAllText(Path.Combine(_dir, "garbage.opus"), "garbage");
        WriteOneSecondOpus(Path.Combine(_dir, "ok.opus"));
    }

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static void WriteOneSecondOpus(string path)
    {
        var encoded = new OpusPcmEncoder(new OpusCodecFactory()).Encode(AudioTestSupport.Tone(440, 48000), new OpusEncodeSettings(1));
        using var stream = File.Create(path);
        OpusSpliceEditor.CreateRecording(encoded, 1).Write(stream, 1);
    }

    private async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        var store = new InMemoryConfigStore();
        var root = ToolkitCommandTree.Create(store);
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
        {
            var code = await ConfigBootstrap.InvokeAsync(root, args, store);
            return (code, output.ToString(), error.ToString());
        }
    }

    private string Missing(string name) => Path.Combine(_dir, name);

    // Every former Environment.Exit site that is reachable without network, API keys, prompts or the real per-user stores.
    // {d} = temp dir, {m} = <temp dir>/missing (does not exist), {n} = file containing "null", {g} = file containing "garbage".
    public static IEnumerable<object[]> ErrorCases() =>
    [
        // strings
        ["strings reverse --in-file {m}.txt --out-file {d}/o.txt", "Input file '{m}.txt' does not exist."],
        ["strings abjadify --in-file {m}.txt --out-file {d}/o.txt", "Input file '{m}.txt' does not exist."],
        ["strings flesch-reading-ease --in-file {m}.txt", "Input file '{m}.txt' does not exist."],
        ["strings report --in-file {m}.txt", "Input file '{m}.txt' does not exist."],
        ["strings report", "Either '--in-file' or '--string' must be provided."],
        ["strings report --in-file {n} --string x", "Only one of '--in-file' or '--string' may be provided."],
        // interpreters
        ["interpreters brainfuck --file {m}.bf", "Brainfuck program file not found: {m}.bf"],
        ["interpreters ook --file {m}.ook", "Ook program file not found: {m}.ook"],
        ["interpreters bf-to-ook --source {m}.bf --output {d}/o.ook", "Brainfuck program file not found: {m}.bf"],
        // images
        ["images display --file {m}.png", "Image file not found: {m}.png"],
        ["images steganography --mode bogus --image {m}.png", "Mode must be 'encode' or 'decode'"],
        ["images steganography --mode encode --image {m}.png", "--message is required for encode mode"],
        ["images steganography --mode encode --image {m}.png --message hi", "--output is required for encode mode"],
        ["images steganography --mode encode --image {m}.png --message hi --output {d}/o.png", "Image file not found: {m}.png"],
        ["images steganography --mode decode --image {m}.png", "Image file not found: {m}.png"],
        // crypto: file errors keep their message
        ["crypto encrypt --number 5 --public-key-file {m}.pub --out-file {d}/o.json", "Could not find file '{m}.pub'."],
        ["crypto encrypt --number 5 --public-key-file {n} --out-file {d}/o.json", "Failed to deserialize public key."],
        ["crypto decrypt --in-file {m}.json --private-key-file {m}.key", "Could not find file '{m}.json'."],
        ["crypto decrypt --in-file {g} --private-key-file {g}", "Decryption failed: input is invalid or was not encrypted with this key."],
        ["crypto add --in-file1 {m}.a --in-file2 {m}.b --out-file {d}/o.json", "Could not find file '{m}.a'."],
        ["crypto add --in-file1 {n} --in-file2 {m}.b --out-file {d}/o.json", "Failed to deserialize first encrypted number."],
        ["crypto subtract --in-file1 {m}.a --in-file2 {m}.b --out-file {d}/o.json", "Could not find file '{m}.a'."],
        ["crypto subtract --in-file1 {n} --in-file2 {m}.b --out-file {d}/o.json", "Failed to deserialize first encrypted number."],
        ["crypto multiply --in-file {m}.a --scalar 2 --out-file {d}/o.json", "Could not find file '{m}.a'."],
        ["crypto multiply --in-file {n} --scalar 2 --out-file {d}/o.json", "Failed to deserialize encrypted number."],
        ["crypto encrypt-string --string-file {m}.txt --public-key-file {m}.pub --out-file {d}/o.json", "Could not find file '{m}.txt'."],
        ["crypto decrypt-string --in-file {m}.json --private-key-file {m}.key", "Could not find file '{m}.json'."],
        ["crypto decrypt-string --in-file {g} --private-key-file {g}", "Decryption failed: input is invalid or was not encrypted with this key."],
        ["crypto replace --in-file {m}.json --start 0 --replacement-file {m}.txt --out-file {d}/o.json", "Could not find file '{m}.txt'."],
        ["crypto substring --in-file {m}.json --start 0 --out-file {d}/o.json", "Could not find file '{m}.json'."],
        ["crypto substring --in-file {n} --start 0 --out-file {d}/o.json", "Failed to deserialize encrypted string."],
        // markov
        ["markov train --source {m}.txt", "Source file '{m}.txt' does not exist."],
        // gpt
        ["gpt train --source x --out {d}/ckpt --resume", "No GPT checkpoint found in '{d}/ckpt'. Run 'gpt train' first."],
        ["gpt complete --model {d}/nomodel --prompt x", "No GPT checkpoint found in '{d}/nomodel'. Run 'gpt train' first."],
        // web
        ["web serve-html --file {m}.html", "File '{m}.html' not found"],
        // sqlite
        ["sqlite tui --db {m}.db", "Database file '{m}.db' not found"],
        // audio ({a} = valid one second .opus, {o} = garbage .opus)
        ["audio insert --file {m}.opus --at 0 --duration 1s", "File '{m}.opus' not found"],
        ["audio overwrite --file {m}.opus --at 0 --duration 1s", "File '{m}.opus' not found"],
        ["audio record --out-file {d}/o.opus --from-wav {m}.wav", "File '{m}.wav' not found"],
        ["audio insert --file {a} --at 5 --duration 1s", "Time 5.000s is beyond the end of '{a}' (1.000s)"],
        ["audio overwrite --file {a} --at 1.001 --duration 1s", "Time 1.001s is beyond the end of '{a}' (1.000s)"],
        ["audio insert --file {a} --at abc --duration 1s", "Invalid time 'abc'. Use [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix."],
        ["audio record --out-file {d}/o.opus --duration -1", "Invalid time '-1'. Use [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix."],
        ["audio record --out-file {d}/o.opus --channels 3 --duration 1s", "--channels must be 1 or 2"],
        ["audio record --out-file {d}/o.opus --bitrate 1000 --duration 1s", "--bitrate must be between 6000 and 510000"],
        ["audio record --out-file {d}/o.opus", "--duration or --from-wav is required when no interactive console is available"],
        ["audio insert --file {a} --at 0", "--duration or --from-wav is required when no interactive console is available"],
        ["audio insert --file {o} --at 0 --duration 1s", "Not an Ogg Opus file"],
        ["audio overwrite --file {o} --at 0 --duration 1s", "Not an Ogg Opus file"],
        // llm
        ["llm generate-image --provider claude --prompt x --out-file {d}/o.png", "The 'generate-image' action requires image generation which is not supported by the 'claude' provider."],
    ];

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task Invoke_ShouldReturnOneAndPrintExactError_WhenCommandFails(string template, string message)
    {
        var line = Expand(template);

        var (code, output, error) = await RunAsync(line.Split(' '));

        code.Should().Be(1);
        output.Trim().Should().Be("Error: " + Expand(message));
        error.Should().BeEmpty();
    }

    private string Expand(string text) => text
        .Replace("{m}", Missing("missing")).Replace("{n}", Path.Combine(_dir, "null.json"))
        .Replace("{g}", Path.Combine(_dir, "garbage.json")).Replace("{a}", Path.Combine(_dir, "ok.opus"))
        .Replace("{o}", Path.Combine(_dir, "garbage.opus")).Replace("{d}", _dir);

    [Fact]
    public async Task Invoke_ShouldReturnOneAndPrintExactErrorLast_WhenWebServerFailsToStart()
    {
        var file = Path.Combine(_dir, "null.json");

        var (code, output, _) = await RunAsync("web", "serve-html", "--file", file, "--port", "99999");

        code.Should().Be(1);
        Lines(output).Should().Equal(
            $"Serving '{file}' on http://0.0.0.0:99999",
            "Press Ctrl+C to stop the server",
            "Error: Specified argument was out of the range of valid values. (Parameter 'port')");
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    [Fact]
    public async Task Invoke_ShouldReturnZeroAndPrintOutput_WhenBrainfuckProgramRuns()
    {
        var program = Path.Combine(_dir, "hello.bf");
        await File.WriteAllTextAsync(program,
            "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.");

        var (code, output, _) = await RunAsync("interpreters", "brainfuck", "--file", program);

        code.Should().Be(0);
        output.Should().Contain("Hello World!");
    }
}
