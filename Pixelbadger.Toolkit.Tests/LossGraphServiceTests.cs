using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class LossGraphServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly LossGraphService _service = new();

    public LossGraphServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    private static LossGraphReport CreateReport(
        IReadOnlyList<float> losses,
        string checkpointPath = "./.gpt",
        IReadOnlyList<int>? tokensSeen = null,
        int vocabEntriesSeen = 250,
        IReadOnlyList<int>? runBoundaries = null) =>
        new(losses,
            // Default coverage: a fixed 1000 positions per step, capped at the corpus size.
            tokensSeen ?? Enumerable.Range(1, losses.Count).Select(i => Math.Min(i * 1000, 12345)).ToArray(),
            runBoundaries ?? new[] { losses.Count },
            new GptConfig(VocabSize: 300, BlockSize: 64, NEmbd: 128, NHead: 4, NLayer: 3),
            BatchSize: 16,
            LearningRate: 3e-4f,
            VocabSize: 300,
            VocabEntriesSeen: vocabEntriesSeen,
            CorpusTokenCount: 12345,
            ParameterCount: 678901,
            CheckpointPath: checkpointPath,
            Resumed: false);

    [Fact]
    public async Task WriteAsync_ShouldWriteSelfContainedHtmlFile()
    {
        var path = Path.Combine(_testDirectory, "loss.html");

        await _service.WriteAsync(path, CreateReport(new[] { 4.2f, 3.1f, 2.5f }));

        File.Exists(path).Should().BeTrue();
        var html = await File.ReadAllTextAsync(path);
        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("</html>");
        html.Should().NotContain("http://").And.NotContain("https://", "the report must not load external resources");
    }

    [Fact]
    public async Task WriteAsync_ShouldCreateMissingDirectories()
    {
        var path = Path.Combine(_testDirectory, "reports", "nested", "loss.html");

        await _service.WriteAsync(path, CreateReport(new[] { 1.0f }));

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ShouldOverwriteExistingFile()
    {
        var path = Path.Combine(_testDirectory, "loss.html");
        await File.WriteAllTextAsync(path, "stale");

        await _service.WriteAsync(path, CreateReport(new[] { 1.5f, 1.25f }));

        var html = await File.ReadAllTextAsync(path);
        html.Should().NotContain("stale");
        html.Should().Contain("[1.5,1.25]");
    }

    [Fact]
    public void Render_ShouldEmbedEveryStepInOrder()
    {
        var losses = Enumerable.Range(0, 500).Select(i => 4f - i * 0.001f).ToArray();

        var html = LossGraphService.Render(CreateReport(losses));

        var series = ExtractSeries(html);
        series.Should().HaveCount(500);
        series[0].Should().Be("4");
        series[^1].Should().Be("3.501");
    }

    [Fact]
    public void Render_ShouldEmitNullForNonFiniteLosses()
    {
        var html = LossGraphService.Render(CreateReport(new[] { 2f, float.NaN, float.PositiveInfinity, 1.5f }));

        ExtractSeries(html).Should().Equal("2", "null", "null", "1.5");
    }

    [Fact]
    public void Render_ShouldReportStepCountAndLossSummary()
    {
        var html = LossGraphService.Render(CreateReport(new[] { 4.5f, 2.25f, 3f }));

        html.Should().Contain(">3</div>", "the step count is shown as a stat");
        html.Should().Contain("4.5000");  // first loss
        html.Should().Contain("3.0000");  // final loss
        html.Should().Contain("2.2500");  // best loss
    }

    [Fact]
    public void Render_ShouldReportRunConfiguration()
    {
        var html = LossGraphService.Render(CreateReport(new[] { 1f }));

        html.Should().Contain("678,901");   // parameter count
        html.Should().Contain("12,345");    // corpus tokens
        html.Should().Contain("0.0003");    // learning rate
        html.Should().Contain("./.gpt");    // checkpoint path
    }

    [Fact]
    public void Render_ShouldEmbedCumulativeTokensSeenPerStep()
    {
        var report = CreateReport(new[] { 3f, 2f, 1f }, tokensSeen: new[] { 500, 900, 1200 });

        var html = LossGraphService.Render(report);

        html.Should().Contain("const TOKENS_SEEN = [500,900,1200];");
        html.Should().Contain("const CORPUS_TOKENS = 12345;");
    }

    [Fact]
    public void Render_ShouldEmbedRunBoundariesAndRunCount()
    {
        var losses = Enumerable.Repeat(1f, 30).ToArray();

        var html = LossGraphService.Render(CreateReport(losses, runBoundaries: new[] { 10, 25, 30 }));

        html.Should().Contain("const RUN_BOUNDARIES = [10,25,30];");
        html.Should().Contain("<div class=\"label\">Runs</div><div class=\"value\">3</div>");
        html.Should().Contain("<tr><td>Training runs</td><td>3</td></tr>");
    }

    [Fact]
    public void Render_ShouldReportCorpusAndVocabCoverage()
    {
        var report = CreateReport(new[] { 3f, 2f }, tokensSeen: new[] { 6000, 9876 }, vocabEntriesSeen: 210);

        var html = LossGraphService.Render(report);

        html.Should().Contain("Corpus coverage");
        html.Should().Contain(">80.0%</div>", "9,876 of 12,345 corpus tokens were sampled");
        html.Should().Contain("Vocab coverage");
        html.Should().Contain(">70.0%</div>", "210 of 300 vocabulary entries were sampled");
        html.Should().Contain("9,876 of 12,345 (80.0%)");
        html.Should().Contain("210 of 300 (70.0%)");
    }

    [Fact]
    public void Render_ShouldHtmlEncodeCheckpointPath()
    {
        var html = LossGraphService.Render(CreateReport(new[] { 1f }, "<script>alert(1)</script>"));

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public void Render_ShouldProduceEmptySeriesAndNoLossStats_WhenNoStepsRecorded()
    {
        var html = LossGraphService.Render(CreateReport(Array.Empty<float>()));

        html.Should().Contain("const LOSSES = [];");
        html.Should().Contain("const TOKENS_SEEN = [];");
        html.Should().Contain("n/a");
    }

    /// <summary>Pulls the embedded <c>const LOSSES = [...]</c> array back out of the rendered page.</summary>
    private static string[] ExtractSeries(string html)
    {
        const string marker = "const LOSSES = [";
        int start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = html.IndexOf(']', start);
        var body = html[start..end];
        return body.Length == 0 ? Array.Empty<string>() : body.Split(',');
    }
}
