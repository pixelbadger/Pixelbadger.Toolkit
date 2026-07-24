using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class GptTrainComponentTests
{
    private readonly Mock<ICheckpointService> _mockCheckpoint = new();
    private readonly GptTrainComponent _component;

    public GptTrainComponentTests()
    {
        _component = new GptTrainComponent(_mockCheckpoint.Object);
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
            It.IsAny<IReadOnlyList<char>>(),
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
        var vocab = corpusForVocab.Distinct().OrderBy(ch => ch).ToArray();
        var config = new GptConfig(vocab.Length, BlockSize: 8, NEmbd: 8, NHead: 2, NLayer: 1);
        var model = new GptModel(config);
        model.InitWeights(99);
        var weights = model.Parameters().Select(p => p.Data.ToArray()).ToArray();
        return new GptCheckpoint(config, vocab, weights);
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
            "ckpt", checkpoint.Config, It.IsAny<IReadOnlyList<char>>(), It.IsAny<IReadOnlyList<Tensor>>()), Times.Once);
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
    public void SampleBatch_ShouldProduceTargetsShiftedByOne()
    {
        var data = Enumerable.Range(0, 20).ToArray();
        var rng = new Random(0);

        var (inputs, targets) = GptTrainComponent.SampleBatch(data, batchSize: 3, blockSize: 4, rng);

        inputs.Should().HaveCount(3);
        targets.Should().HaveCount(3);
        for (int b = 0; b < 3; b++)
        {
            inputs[b].Should().HaveCount(4);
            for (int t = 0; t < 4; t++)
                targets[b][t].Should().Be(inputs[b][t] + 1); // data is the identity sequence
        }
    }
}
