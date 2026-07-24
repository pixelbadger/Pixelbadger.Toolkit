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

    [Theory]
    [InlineData("bpe")]
    [InlineData("char")]
    public async Task GptCommand_ShouldTrainAndGenerate_ForEitherTokenizer(string tokenizer)
    {
        var corpusPath = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(corpusPath, string.Concat(Enumerable.Repeat("hello gpt world. ", 40)));
        var modelDir = Path.Combine(_testDirectory, "model");

        var trainExit = await GptCommand.Create().Parse(new[]
        {
            "train",
            "--source", corpusPath,
            "--out", modelDir,
            "--tokenizer", tokenizer,
            "--vocab-size", "300",
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

    [Fact]
    public async Task GptCommand_ShouldResumeTrainingFromExistingCheckpoint()
    {
        var corpusPath = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(corpusPath, string.Concat(Enumerable.Repeat("resume me please. ", 30)));
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
        File.Exists(Path.Combine(modelDir, "optimizer.bin")).Should().BeTrue();

        // Resume picks up the saved weights, config, and optimizer state and keeps training.
        var resumeExit = await GptCommand.Create().Parse(new[]
        {
            "train",
            "--source", corpusPath,
            "--out", modelDir,
            "--steps", "5",
            "--batch-size", "4",
            "--seed", "2",
            "--resume"
        }).InvokeAsync();

        resumeExit.Should().Be(0);

        var completeExit = await GptCommand.Create().Parse(new[]
        {
            "complete",
            "--model", modelDir,
            "--prompt", "resume",
            "--max-tokens", "10",
            "--temperature", "0"
        }).InvokeAsync();

        completeExit.Should().Be(0);
    }

    [Fact]
    public async Task GptCommand_ShouldWriteLossGraphWithOnePointPerStep_WhenLossGraphRequested()
    {
        var corpusPath = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(corpusPath, string.Concat(Enumerable.Repeat("graph my loss. ", 30)));
        var modelDir = Path.Combine(_testDirectory, "model");
        var graphPath = Path.Combine(_testDirectory, "reports", "loss.html");

        var trainExit = await GptCommand.Create().Parse(new[]
        {
            "train",
            "--source", corpusPath,
            "--out", modelDir,
            "--loss-graph", graphPath,
            "--steps", "7",
            "--batch-size", "4",
            "--block-size", "8",
            "--n-embd", "16",
            "--n-head", "2",
            "--n-layer", "1",
            "--seed", "1"
        }).InvokeAsync();

        trainExit.Should().Be(0);
        File.Exists(graphPath).Should().BeTrue();

        var html = await File.ReadAllTextAsync(graphPath);
        html.Should().StartWith("<!DOCTYPE html>");

        const string marker = "const LOSSES = [";
        int start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = html.IndexOf(']', start);
        html[start..end].Split(',').Should().HaveCount(7, "every training step must appear in the graph data");
    }

    [Fact]
    public async Task GptCommand_ShouldGraphFullHistoryAcrossRuns_WhenResuming()
    {
        var corpusPath = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(corpusPath, string.Concat(Enumerable.Repeat("keep my whole history. ", 30)));
        var modelDir = Path.Combine(_testDirectory, "model");
        var graphPath = Path.Combine(_testDirectory, "loss.html");

        string[] TrainArgs(params string[] extra) => new[]
        {
            "train",
            "--source", corpusPath,
            "--out", modelDir,
            "--loss-graph", graphPath,
            "--batch-size", "4",
            "--block-size", "8",
            "--n-embd", "16",
            "--n-head", "2",
            "--n-layer", "1",
            "--seed", "1"
        }.Concat(extra).ToArray();

        var firstExit = await GptCommand.Create().Parse(TrainArgs("--steps", "6")).InvokeAsync();
        firstExit.Should().Be(0);
        File.Exists(Path.Combine(modelDir, "history.bin")).Should().BeTrue();

        var firstRunLosses = ReadSeries(await File.ReadAllTextAsync(graphPath), "const LOSSES = [");
        firstRunLosses.Should().HaveCount(6);

        var resumeExit = await GptCommand.Create().Parse(TrainArgs("--steps", "4", "--resume")).InvokeAsync();
        resumeExit.Should().Be(0);

        var html = await File.ReadAllTextAsync(graphPath);
        var losses = ReadSeries(html, "const LOSSES = [");
        var tokensSeen = ReadSeries(html, "const TOKENS_SEEN = [");

        losses.Should().HaveCount(10, "the resumed graph must span both runs");
        losses.Take(6).Should().Equal(firstRunLosses, "the first run's losses are preserved verbatim");
        tokensSeen.Should().HaveCount(10);
        ReadSeries(html, "const RUN_BOUNDARIES = [").Should().Equal("6", "10");

        // Coverage is cumulative across runs, so it can only ever grow.
        var counts = tokensSeen.Select(int.Parse).ToArray();
        counts.Should().BeInAscendingOrder();
        counts[6].Should().BeGreaterThanOrEqualTo(counts[5], "the resumed run continues from the visited positions");
    }

    private static string[] ReadSeries(string html, string marker)
    {
        int start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = html.IndexOf(']', start);
        var body = html[start..end];
        return body.Length == 0 ? Array.Empty<string>() : body.Split(',');
    }
}
