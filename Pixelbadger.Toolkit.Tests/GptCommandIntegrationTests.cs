using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class GptCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public GptCommandIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task GptCommand_ShouldTrainCheckpointThenGenerateText()
    {
        var corpusPath = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(corpusPath, string.Concat(Enumerable.Repeat("hello gpt world. ", 30)));
        var modelDir = Path.Combine(_testDirectory, "model");

        var trainExit = await GptCommand.Create().Parse(new[]
        {
            "train",
            "--source", corpusPath,
            "--out", modelDir,
            "--steps", "5",
            "--batch-size", "4",
            "--block-size", "8",
            "--n-embd", "16",
            "--n-head", "2",
            "--n-layer", "1",
            "--seed", "1"
        }).InvokeAsync();

        trainExit.Should().Be(0);
        File.Exists(Path.Combine(modelDir, "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(modelDir, "weights.bin")).Should().BeTrue();

        // complete must load the freshly-written checkpoint and generate without error.
        var completeExit = await GptCommand.Create().Parse(new[]
        {
            "complete",
            "--model", modelDir,
            "--prompt", "hello",
            "--max-tokens", "10",
            "--temperature", "0"
        }).InvokeAsync();

        completeExit.Should().Be(0);
    }
}
