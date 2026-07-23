namespace Pixelbadger.Toolkit.Services;

public record GptCheckpoint(GptConfig Config, char[] Vocabulary, float[][] Weights);

public interface ICheckpointService
{
    Task SaveAsync(string directory, GptConfig config, IReadOnlyList<char> vocabulary, IReadOnlyList<Tensor> parameters);
    Task<GptCheckpoint> LoadAsync(string directory);
}
