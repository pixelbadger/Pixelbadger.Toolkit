namespace Pixelbadger.Toolkit.Services;

public record GptCheckpoint(GptConfig Config, TokenizerState Tokenizer, float[][] Weights);

public interface ICheckpointService
{
    Task SaveAsync(string directory, GptConfig config, TokenizerState tokenizer, IReadOnlyList<Tensor> parameters);
    Task<GptCheckpoint> LoadAsync(string directory);
    Task SaveOptimizerStateAsync(string directory, AdamWState state);
    Task<AdamWState?> TryLoadOptimizerStateAsync(string directory);
}
