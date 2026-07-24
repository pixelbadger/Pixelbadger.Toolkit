namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// A checkpoint's training history: the per-step loss series, the per-step count of distinct corpus
/// positions sampled so far, where each run ended, and the latest run's configuration. Series span
/// every run that has trained the checkpoint, not just the most recent one.
/// </summary>
public record LossGraphReport(
    IReadOnlyList<float> Losses,
    IReadOnlyList<int> TokensSeen,
    IReadOnlyList<int> RunBoundaries,
    GptConfig Config,
    int BatchSize,
    float LearningRate,
    int VocabSize,
    int VocabEntriesSeen,
    int CorpusTokenCount,
    int ParameterCount,
    string CheckpointPath,
    bool Resumed);

/// <summary>Renders a training run's loss history as a standalone HTML report.</summary>
public interface ILossGraphService
{
    /// <summary>Writes the loss graph for <paramref name="report"/> to <paramref name="outputPath"/>.</summary>
    Task WriteAsync(string outputPath, LossGraphReport report);
}
