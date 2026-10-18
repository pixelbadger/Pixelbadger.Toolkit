using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
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
    }

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
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

    public static IEnumerable<object[]> ErrorCases() =>
    [
        ["strings reverse --in-file {m}.txt --out-file {d}/o.txt"],
        ["interpreters brainfuck --file {m}.bf"],
        ["interpreters ook --file {m}.ook"],
        ["interpreters bf-to-ook --source {m}.bf --output {d}/o.ook"],
        ["images display --file {m}.png"],
        ["images steganography --mode bogus --image {m}.png"],
        ["images steganography --mode encode --image {m}.png"],
        ["crypto decrypt --in-file {m}.json --private-key-file {m}.key"],
        ["crypto encrypt --number 5 --public-key-file {m}.pub --out-file {d}/o.json"],
        ["crypto add --in-file1 {m}.a --in-file2 {m}.b --out-file {d}/o.json"],
        ["markov train --source {m}.txt"],
        ["gpt complete --model {d}/nomodel --prompt x"],
        ["web serve-html --file {m}.html"],
        ["llm generate-image --provider claude --prompt x --out-file {d}/o.png"],
    ];

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task Invoke_ShouldReturnOneAndPrintError_WhenCommandFails(string template)
    {
        var line = template.Replace("{m}", Missing("missing")).Replace("{d}", _dir);

        var (code, output, error) = await RunAsync(line.Split(' '));

        code.Should().Be(1);
        (output + error).Should().Contain("Error:");
    }

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
