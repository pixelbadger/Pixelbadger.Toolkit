using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class GptTrainComponentTests
{
    private readonly Mock<ICheckpointService> _mockCheckpoint = new();
    private readonly Mock<ILossGraphService> _mockLossGraph = new();
    private readonly GptTrainComponent _component;

    public GptTrainComponentTests()
    {
        _component = new GptTrainComponent(_mockCheckpoint.Object, _mockLossGraph.Object);
    }

    [Fact]
    public async Task TrainAsync_ShouldDriveLossDownOnRepetitiveCorpus()
    {
        // A highly repetitive corpus is easy to overfit, giving a deterministic, sharp pass/fail.
        var corpus = string.Concat(Enumerable.Repeat("the quick brown fox. ", 30));
        var options = new GptTrainOptions(
            Steps: 200, BatchSize: 8, BlockSize: 16, NEmbd: 32, NHead: 4, NLayer: 2, LearningRate: 3e-3f, Seed: 1337);

        var losses = new List<float>();
        var result = await _component.TrainAsync(corpus, "ignored", options, (_, loss) => losses.Add(loss));

        float firstLoss = losses[0];
        float finalLoss = result.FinalLoss;

        firstLoss.Should().BeGreaterThan(finalLoss, "training should reduce the loss");
        finalLoss.Should().BeLessThan(1.0f, "the model should overfit this tiny repetitive corpus");
        result.Steps.Should().Be(200);
        result.ParameterCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TrainAsync_ShouldSaveCheckpointWithModelParameters()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var options = new GptTrainOptions(Steps: 3, BatchSize: 2, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1, Seed: 1);

        await _component.TrainAsync(corpus, "out-dir", options);

        _mockCheckpoint.Verify(x => x.SaveAsync(
            "out-dir",
            It.IsAny<GptConfig>(),
            It.IsAny<TokenizerState>(),
            It.IsAny<IReadOnlyList<Tensor>>()), Times.Once);
    }

    [Fact]
    public async Task TrainAsync_ShouldThrow_WhenEmbeddingNotDivisibleByHeads()
    {
        var options = new GptTrainOptions(NEmbd: 10, NHead: 4, BlockSize: 4);

        var act = async () => await _component.TrainAsync("aaaa bbbb cccc", "out", options);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*divisible*");
    }

    [Fact]
    public async Task TrainAsync_ShouldThrow_WhenCorpusShorterThanBlockSize()
    {
        var options = new GptTrainOptions(BlockSize: 64);

        var act = async () => await _component.TrainAsync("short", "out", options);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*too short*");
    }

    [Fact]
    public async Task TrainAsync_ShouldProduceIdenticalLossSequence_WhenRunTwiceWithSameSeed()
    {
        var corpus = string.Concat(Enumerable.Repeat("parallel yet deterministic. ", 20));
        var options = new GptTrainOptions(
            Steps: 20, BatchSize: 4, BlockSize: 16, NEmbd: 32, NHead: 4, NLayer: 2, Seed: 42);

        var losses1 = new List<float>();
        var losses2 = new List<float>();
        await _component.TrainAsync(corpus, "ignored", options, (_, loss) => losses1.Add(loss));
        await _component.TrainAsync(corpus, "ignored", options, (_, loss) => losses2.Add(loss));

        // Exact equality: parallel execution must not change results with thread scheduling.
        losses2.Should().Equal(losses1);
    }

    private static GptCheckpoint CreateCheckpoint(string corpusForVocab)
    {
        var tokenizer = CharTokenizer.Build(corpusForVocab);
        var config = new GptConfig(tokenizer.VocabSize, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1);
        var model = new GptModel(config);
        model.InitWeights(99);
        var weights = model.Parameters().Select(p => p.Data.ToArray()).ToArray();
        return new GptCheckpoint(config, tokenizer.ExportState(), weights);
    }

    [Fact]
    public async Task TrainAsync_ShouldLoadCheckpointAndUseItsConfig_WhenResuming()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var checkpoint = CreateCheckpoint(corpus);
        _mockCheckpoint.Setup(x => x.LoadAsync("ckpt")).ReturnsAsync(checkpoint);
        _mockCheckpoint.Setup(x => x.TryLoadOptimizerStateAsync("ckpt")).ReturnsAsync((AdamWState?)null);

        // Architecture options deliberately differ from the checkpoint; resume must ignore them.
        var options = new GptTrainOptions(
            Steps: 5, BatchSize: 2, BlockSize: 64, NEmbd: 128, NHead: 4, NLayer: 3, Resume: true);
        var result = await _component.TrainAsync(corpus, "ckpt", options);

        result.VocabSize.Should().Be(checkpoint.Config.VocabSize);
        result.ParameterCount.Should().Be(checkpoint.Weights.Sum(w => w.Length));
        _mockCheckpoint.Verify(x => x.LoadAsync("ckpt"), Times.Once);
        _mockCheckpoint.Verify(x => x.SaveAsync(
            "ckpt", checkpoint.Config, It.IsAny<TokenizerState>(), It.IsAny<IReadOnlyList<Tensor>>()), Times.Once);
        _mockCheckpoint.Verify(x => x.SaveOptimizerStateAsync("ckpt", It.IsAny<AdamWState>()), Times.Once);
    }

    [Fact]
    public async Task TrainAsync_ShouldRestoreOptimizerState_WhenPresentOnResume()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var checkpoint = CreateCheckpoint(corpus);
        var state = new AdamWState(
            Step: 7,
            M: checkpoint.Weights.Select(w => new float[w.Length]).ToArray(),
            V: checkpoint.Weights.Select(w => new float[w.Length]).ToArray());
        _mockCheckpoint.Setup(x => x.LoadAsync("ckpt")).ReturnsAsync(checkpoint);
        _mockCheckpoint.Setup(x => x.TryLoadOptimizerStateAsync("ckpt")).ReturnsAsync(state);

        var result = await _component.TrainAsync(corpus, "ckpt", new GptTrainOptions(Steps: 3, BatchSize: 2, Resume: true));

        result.Steps.Should().Be(3);
        _mockCheckpoint.Verify(x => x.TryLoadOptimizerStateAsync("ckpt"), Times.Once);
    }

    [Fact]
    public async Task TrainAsync_ShouldPersistTrainingHistoryWithTheCheckpoint()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var options = new GptTrainOptions(Steps: 4, BatchSize: 2, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1, Seed: 2);

        TrainingHistory? saved = null;
        _mockCheckpoint
            .Setup(x => x.SaveTrainingHistoryAsync("out", It.IsAny<TrainingHistory>()))
            .Callback<string, TrainingHistory>((_, history) => saved = history)
            .Returns(Task.CompletedTask);

        var result = await _component.TrainAsync(corpus, "out", options);

        saved.Should().NotBeNull();
        saved!.Losses.Should().Equal(result.LossHistory);
        saved.TokensSeen.Should().Equal(result.TokensSeenHistory);
        saved.RunBoundaries.Should().Equal(4);
        saved.CorpusTokenCount.Should().Be(result.CorpusTokenCount);
        saved.PositionSeen.Should().HaveCount(result.CorpusTokenCount);
        saved.PositionSeen.Count(seen => seen).Should().Be(result.TokensSeen);
        saved.VocabSeen.Count(seen => seen).Should().Be(result.VocabEntriesSeen);
    }

    [Fact]
    public async Task TrainAsync_ShouldAppendToStoredHistory_WhenResuming()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20)); // 120 tokens
        var checkpoint = CreateCheckpoint(corpus);
        _mockCheckpoint.Setup(x => x.LoadAsync("ckpt")).ReturnsAsync(checkpoint);
        _mockCheckpoint.Setup(x => x.TryLoadOptimizerStateAsync("ckpt")).ReturnsAsync((AdamWState?)null);

        // A prior run of 5 steps that had already visited the first 30 corpus positions.
        var positionSeen = new bool[120];
        for (int i = 0; i < 30; i++)
            positionSeen[i] = true;
        var priorHistory = new TrainingHistory(
            Losses: new[] { 3f, 2.9f, 2.8f, 2.7f, 2.6f },
            TokensSeen: new[] { 9, 15, 21, 27, 30 },
            RunBoundaries: new[] { 5 },
            CorpusTokenCount: 120,
            PositionSeen: positionSeen,
            VocabSeen: new bool[checkpoint.Config.VocabSize]);
        _mockCheckpoint.Setup(x => x.TryLoadTrainingHistoryAsync("ckpt")).ReturnsAsync(priorHistory);

        var result = await _component.TrainAsync(
            corpus, "ckpt", new GptTrainOptions(Steps: 6, BatchSize: 2, Resume: true, LossGraphPath: "loss.html"));

        result.Steps.Should().Be(6, "Steps reports this run only");
        result.TotalSteps.Should().Be(11, "the history spans both runs");
        result.LossHistory.Should().HaveCount(11);
        result.LossHistory.Take(5).Should().Equal(priorHistory.Losses, "the earlier run's losses are preserved");
        result.TokensSeenHistory.Take(5).Should().Equal(priorHistory.TokensSeen);
        result.RunBoundaries.Should().Equal(5, 11);
        result.TokensSeen.Should().BeGreaterThan(30, "coverage continues from the positions already visited");
        result.TokensSeenHistory[5].Should().BeGreaterThanOrEqualTo(30);
        result.TokensSeenHistory.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task TrainAsync_ShouldRestartCoverageButKeepLosses_WhenResumedCorpusLengthDiffers()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20)); // 120 tokens
        var checkpoint = CreateCheckpoint(corpus);
        _mockCheckpoint.Setup(x => x.LoadAsync("ckpt")).ReturnsAsync(checkpoint);
        _mockCheckpoint.Setup(x => x.TryLoadOptimizerStateAsync("ckpt")).ReturnsAsync((AdamWState?)null);

        // History recorded against a different (shorter) corpus: its visited positions do not apply here.
        var priorHistory = new TrainingHistory(
            Losses: new[] { 3f, 2.5f },
            TokensSeen: new[] { 40, 60 },
            RunBoundaries: new[] { 2 },
            CorpusTokenCount: 60,
            PositionSeen: Enumerable.Repeat(true, 60).ToArray(),
            VocabSeen: Enumerable.Repeat(true, checkpoint.Config.VocabSize).ToArray());
        _mockCheckpoint.Setup(x => x.TryLoadTrainingHistoryAsync("ckpt")).ReturnsAsync(priorHistory);

        var result = await _component.TrainAsync(
            corpus, "ckpt", new GptTrainOptions(Steps: 2, BatchSize: 1, Resume: true));

        result.LossHistory.Should().HaveCount(4);
        result.LossHistory.Take(2).Should().Equal(priorHistory.Losses);
        result.CorpusTokenCount.Should().Be(120);
        result.TokensSeen.Should().BeLessThanOrEqualTo(18, "coverage restarted, so only this run's two windows count");
    }

    [Fact]
    public async Task TrainAsync_ShouldNotLoadStoredHistory_WhenNotResuming()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));

        await _component.TrainAsync(corpus, "out", new GptTrainOptions(Steps: 2, BatchSize: 2, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1));

        _mockCheckpoint.Verify(x => x.TryLoadTrainingHistoryAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TrainAsync_ShouldThrow_WhenResumeCorpusContainsUnknownCharacters()
    {
        var checkpoint = CreateCheckpoint(string.Concat(Enumerable.Repeat("abcde ", 20)));
        _mockCheckpoint.Setup(x => x.LoadAsync(It.IsAny<string>())).ReturnsAsync(checkpoint);
        _mockCheckpoint.Setup(x => x.TryLoadOptimizerStateAsync(It.IsAny<string>())).ReturnsAsync((AdamWState?)null);

        var act = async () => await _component.TrainAsync(
            string.Concat(Enumerable.Repeat("abcde xyz ", 20)), "ckpt", new GptTrainOptions(Resume: true));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not present in the model vocabulary*");
    }

    [Fact]
    public async Task TrainAsync_ShouldRecordLossForEveryStep()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var options = new GptTrainOptions(Steps: 12, BatchSize: 2, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1, Seed: 5);

        var reported = new List<float>();
        var result = await _component.TrainAsync(corpus, "out", options, (_, loss) => reported.Add(loss));

        result.LossHistory.Should().HaveCount(12);
        result.LossHistory.Should().Equal(reported, "the history must hold the same per-step losses that were reported");
        result.LossHistory[^1].Should().Be(result.FinalLoss);
        result.LossGraphPath.Should().BeNull();
    }

    [Fact]
    public async Task TrainAsync_ShouldWriteLossGraph_WhenGraphPathProvided()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var options = new GptTrainOptions(
            Steps: 4, BatchSize: 2, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1, Seed: 5,
            LossGraphPath: "loss.html");

        LossGraphReport? captured = null;
        _mockLossGraph
            .Setup(x => x.WriteAsync("loss.html", It.IsAny<LossGraphReport>()))
            .Callback<string, LossGraphReport>((_, report) => captured = report)
            .Returns(Task.CompletedTask);

        var result = await _component.TrainAsync(corpus, "out", options);

        result.LossGraphPath.Should().Be("loss.html");
        _mockLossGraph.Verify(x => x.WriteAsync("loss.html", It.IsAny<LossGraphReport>()), Times.Once);
        captured.Should().NotBeNull();
        captured!.Losses.Should().Equal(result.LossHistory);
        captured.BatchSize.Should().Be(2);
        captured.LearningRate.Should().Be(options.LearningRate);
        captured.VocabSize.Should().Be(result.VocabSize);
        captured.ParameterCount.Should().Be(result.ParameterCount);
        captured.CheckpointPath.Should().Be("out");
        captured.Resumed.Should().BeFalse();
        captured.TokensSeen.Should().Equal(result.TokensSeenHistory);
        captured.VocabEntriesSeen.Should().Be(result.VocabEntriesSeen);
    }

    [Fact]
    public async Task TrainAsync_ShouldCountDistinctCorpusPositionsSampled()
    {
        // 4 steps x 1 window of 8 inputs + 1 shifted target = at most 36 distinct positions.
        var corpus = string.Concat(Enumerable.Repeat("abcdefgh ", 40));
        var options = new GptTrainOptions(Steps: 4, BatchSize: 1, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1, Seed: 7);

        var result = await _component.TrainAsync(corpus, "out", options);

        result.TokensSeenHistory.Should().HaveCount(4);
        result.TokensSeenHistory.Should().BeInAscendingOrder("coverage is cumulative and can never shrink");
        result.TokensSeenHistory[0].Should().Be(9, "the first window covers block-size inputs plus one target");
        result.TokensSeenHistory[^1].Should().Be(result.TokensSeen);
        result.TokensSeen.Should().BeInRange(9, 36);
        result.TokensSeen.Should().BeLessThan(result.CorpusTokenCount, "4 tiny windows cannot cover this corpus");
    }

    [Fact]
    public async Task TrainAsync_ShouldCoverWholeCorpusAndVocabulary_WhenGivenEnoughSteps()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20)); // 120 tokens, 6 distinct chars
        var options = new GptTrainOptions(
            Steps: 200, BatchSize: 8, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1, Seed: 3, Tokenizer: TokenizerKind.Char);

        var result = await _component.TrainAsync(corpus, "out", options);

        result.CorpusTokenCount.Should().Be(120);
        result.TokensSeen.Should().Be(120, "200 steps of 8 random windows must reach every position of a 120-token corpus");
        result.VocabEntriesSeen.Should().Be(result.VocabSize);
    }

    [Fact]
    public async Task TrainAsync_ShouldNotWriteLossGraph_WhenGraphPathOmitted()
    {
        var corpus = string.Concat(Enumerable.Repeat("abcde ", 20));
        var options = new GptTrainOptions(Steps: 3, BatchSize: 2, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1);

        await _component.TrainAsync(corpus, "out", options);

        _mockLossGraph.Verify(x => x.WriteAsync(It.IsAny<string>(), It.IsAny<LossGraphReport>()), Times.Never);
    }

    [Fact]
    public void SampleBatch_ShouldProduceTargetsShiftedByOne()
    {
        var data = Enumerable.Range(0, 20).ToArray();
        var rng = new Random(0);

        var (inputs, targets, starts) = GptTrainComponent.SampleBatch(data, batchSize: 3, blockSize: 4, rng);

        inputs.Should().HaveCount(3);
        targets.Should().HaveCount(3);
        for (int b = 0; b < 3; b++)
        {
            inputs[b].Should().HaveCount(4);
            for (int t = 0; t < 4; t++)
                targets[b][t].Should().Be(inputs[b][t] + 1); // data is the identity sequence
        }

        // data is the identity sequence, so each window's first token is its start offset.
        starts.Should().HaveCount(3);
        for (int b = 0; b < 3; b++)
        {
            starts[b].Should().Be(inputs[b][0]);
            starts[b].Should().BeInRange(0, data.Length - 5);
        }
    }
}
