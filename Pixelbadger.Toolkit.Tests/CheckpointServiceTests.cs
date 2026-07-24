using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class CheckpointServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly CheckpointService _service = new();

    public CheckpointServiceTests()
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
    public async Task SaveThenLoad_ShouldRoundTripConfigCharTokenizerAndWeights()
    {
        var config = new GptConfig(VocabSize: 6, BlockSize: 4, NEmbd: 8, NHead: 2, NLayer: 1);
        var model = new GptModel(config);
        model.InitWeights(123);
        var tokenizer = new TokenizerState(TokenizerKind.Char, "abcdef", Merges: null);
        var dir = Path.Combine(_testDirectory, "ckpt");

        await _service.SaveAsync(dir, config, tokenizer, model.Parameters());
        var loaded = await _service.LoadAsync(dir);

        loaded.Config.Should().Be(config);
        loaded.Tokenizer.Kind.Should().Be(TokenizerKind.Char);
        loaded.Tokenizer.Vocabulary.Should().Be("abcdef");
        loaded.Tokenizer.Merges.Should().BeNull();
        loaded.Weights.Length.Should().Be(model.Parameters().Count);
        for (int i = 0; i < loaded.Weights.Length; i++)
            loaded.Weights[i].Should().Equal(model.Parameters()[i].Data);
    }

    [Fact]
    public async Task SaveThenLoad_ShouldRoundTripBpeMerges()
    {
        var config = new GptConfig(VocabSize: 258, BlockSize: 4, NEmbd: 8, NHead: 2, NLayer: 1);
        var model = new GptModel(config);
        model.InitWeights(123);
        var merges = new[] { new[] { 104, 105 }, new[] { 256, 106 } };
        var tokenizer = new TokenizerState(TokenizerKind.Bpe, Vocabulary: null, merges);
        var dir = Path.Combine(_testDirectory, "ckpt");

        await _service.SaveAsync(dir, config, tokenizer, model.Parameters());
        var loaded = await _service.LoadAsync(dir);

        loaded.Tokenizer.Kind.Should().Be(TokenizerKind.Bpe);
        loaded.Tokenizer.Vocabulary.Should().BeNull();
        loaded.Tokenizer.Merges.Should().BeEquivalentTo(merges, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task LoadedWeights_ShouldReproduceIdenticalForwardPass()
    {
        var config = new GptConfig(VocabSize: 6, BlockSize: 4, NEmbd: 8, NHead: 2, NLayer: 1);
        var original = new GptModel(config);
        original.InitWeights(7);
        var tokenizer = new TokenizerState(TokenizerKind.Char, "abcdef", Merges: null);
        var dir = Path.Combine(_testDirectory, "ckpt");
        await _service.SaveAsync(dir, config, tokenizer, original.Parameters());

        var loaded = await _service.LoadAsync(dir);
        var restored = new GptModel(loaded.Config);
        restored.LoadWeights(loaded.Weights);

        var batch = new[] { new[] { 1, 2, 3, 0 } };
        var (originalLogits, _) = original.Forward(batch);
        var (restoredLogits, _) = restored.Forward(batch);

        restoredLogits.Data.Should().Equal(originalLogits.Data);
    }

    [Fact]
    public async Task LoadAsync_ShouldThrow_WhenCheckpointMissing()
    {
        var act = async () => await _service.LoadAsync(Path.Combine(_testDirectory, "does-not-exist"));

        await act.Should().ThrowAsync<FileNotFoundException>().WithMessage("*gpt train*");
    }

    [Fact]
    public async Task SaveThenLoadOptimizerState_ShouldRoundTripStepAndMoments()
    {
        var state = new AdamWState(
            Step: 42,
            M: [[1.5f, -2.5f], [3.25f]],
            V: [[0.5f, 0.25f], [0.125f]]);
        var dir = Path.Combine(_testDirectory, "ckpt");

        await _service.SaveOptimizerStateAsync(dir, state);
        var loaded = await _service.TryLoadOptimizerStateAsync(dir);

        loaded.Should().NotBeNull();
        loaded!.Step.Should().Be(42);
        loaded.M.Length.Should().Be(2);
        loaded.M[0].Should().Equal(1.5f, -2.5f);
        loaded.M[1].Should().Equal(3.25f);
        loaded.V[0].Should().Equal(0.5f, 0.25f);
        loaded.V[1].Should().Equal(0.125f);
    }

    [Fact]
    public async Task TryLoadOptimizerStateAsync_ShouldReturnNull_WhenFileAbsent()
    {
        var loaded = await _service.TryLoadOptimizerStateAsync(Path.Combine(_testDirectory, "no-optimizer"));

        loaded.Should().BeNull();
    }
}
