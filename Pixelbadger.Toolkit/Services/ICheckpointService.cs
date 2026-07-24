namespace Pixelbadger.Toolkit.Services;

public record GptCheckpoint(GptConfig Config, TokenizerState Tokenizer, float[][] Weights);

/// <summary>
/// The training history accumulated across every run that has touched a checkpoint, so a resumed
/// run can graph the whole curve rather than just its own steps.
/// </summary>
/// <param name="Losses">Loss for every step of every run, in order.</param>
/// <param name="TokensSeen">Cumulative distinct corpus positions sampled, per step.</param>
/// <param name="RunBoundaries">Cumulative step counts at the end of each completed run.</param>
/// <param name="CorpusTokenCount">Token count of the corpus the coverage sets refer to.</param>
/// <param name="PositionSeen">Which corpus positions have been sampled at least once.</param>
/// <param name="VocabSeen">Which vocabulary entries have been sampled at least once.</param>
public record TrainingHistory(
    float[] Losses,
    int[] TokensSeen,
    int[] RunBoundaries,
    int CorpusTokenCount,
    bool[] PositionSeen,
    bool[] VocabSeen);

public interface ICheckpointService
{
    Task SaveAsync(string directory, GptConfig config, TokenizerState tokenizer, IReadOnlyList<Tensor> parameters);
    Task<GptCheckpoint> LoadAsync(string directory);
    Task SaveOptimizerStateAsync(string directory, AdamWState state);
    Task<AdamWState?> TryLoadOptimizerStateAsync(string directory);
    Task SaveTrainingHistoryAsync(string directory, TrainingHistory history);
    Task<TrainingHistory?> TryLoadTrainingHistoryAsync(string directory);
}
