using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class GptCompleteComponentTests
{
    private readonly Mock<ICheckpointService> _mockCheckpoint = new();
    private readonly GptCompleteComponent _component;

    public GptCompleteComponentTests()
    {
        _component = new GptCompleteComponent(_mockCheckpoint.Object);
    }

    private void SetupCheckpoint(GptConfig config, char[] vocab, int seed)
    {
        var model = new GptModel(config);
        model.InitWeights(seed);
        var weights = model.Parameters().Select(p => (float[])p.Data.Clone()).ToArray();
        _mockCheckpoint.Setup(x => x.LoadAsync(It.IsAny<string>()))
            .ReturnsAsync(new GptCheckpoint(config, vocab, weights));
    }

    [Fact]
    public async Task CompleteAsync_ShouldBeDeterministic_WithGreedyDecoding()
    {
        var config = new GptConfig(VocabSize: 6, BlockSize: 4, NEmbd: 8, NHead: 2, NLayer: 1);
        SetupCheckpoint(config, "abcdef".ToCharArray(), seed: 5);

        var first = await _component.CompleteAsync("model", "abc", maxTokens: 20, temperature: 0f, seed: 1);
        var second = await _component.CompleteAsync("model", "abc", maxTokens: 20, temperature: 0f, seed: 99);

        first.Text.Should().Be(second.Text, "greedy decoding ignores the seed and is fully deterministic");
        first.Text.Length.Should().Be(3 + 20); // prompt + generated
        first.Text.Should().StartWith("abc");
    }

    [Fact]
    public async Task CompleteAsync_ShouldCropContextToBlockSize()
    {
        var config = new GptConfig(VocabSize: 6, BlockSize: 4, NEmbd: 8, NHead: 2, NLayer: 1);
        SetupCheckpoint(config, "abcdef".ToCharArray(), seed: 5);

        // Prompt longer than the block size must not throw and should still extend the text.
        var result = await _component.CompleteAsync("model", "abcdef", maxTokens: 5, temperature: 0f, seed: 1);

        result.Text.Length.Should().Be(6 + 5);
    }

    [Fact]
    public void ArgMax_ShouldReturnIndexOfLargestValue()
    {
        GptCompleteComponent.ArgMax(new[] { 0.1f, 0.9f, 0.3f, 0.2f }).Should().Be(1);
    }

    [Fact]
    public void SampleWithTemperature_ShouldBeReproducibleForFixedSeed()
    {
        var logits = new[] { 1.0f, 2.0f, 0.5f, -1.0f };

        var a = GptCompleteComponent.SampleWithTemperature(logits, 0.8f, new Random(7));
        var b = GptCompleteComponent.SampleWithTemperature(logits, 0.8f, new Random(7));

        a.Should().Be(b);
    }

    [Fact]
    public void SampleWithTemperature_ShouldAlwaysSelectDominantLogit()
    {
        // One logit overwhelmingly larger -> probability mass collapses onto it.
        var logits = new[] { -50f, 50f, -50f };

        for (int seed = 0; seed < 10; seed++)
            GptCompleteComponent.SampleWithTemperature(logits, 1.0f, new Random(seed)).Should().Be(1);
    }
}
