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
