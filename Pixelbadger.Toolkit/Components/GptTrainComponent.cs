using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

/// <param name="Steps">Steps run this time; a resumed run's history also covers the earlier runs.</param>
/// <param name="LossHistory">Loss for every training step of every run against this checkpoint, in order.</param>
/// <param name="TokensSeenHistory">Cumulative count of distinct corpus positions sampled, per step.</param>
/// <param name="RunBoundaries">Cumulative step counts at the end of each run, including this one.</param>
/// <param name="TokensSeen">Distinct corpus positions sampled so far, out of <paramref name="CorpusTokenCount"/>.</param>
/// <param name="VocabEntriesSeen">Distinct vocabulary entries sampled so far, out of <paramref name="VocabSize"/>.</param>
public record GptTrainResult(
    int Steps,
    float FinalLoss,
    string CheckpointPath,
    int ParameterCount,
    int VocabSize,
    int CorpusTokenCount,
    IReadOnlyList<float> LossHistory,
    IReadOnlyList<int> TokensSeenHistory,
    IReadOnlyList<int> RunBoundaries,
    int TokensSeen,
    int VocabEntriesSeen,
    string? LossGraphPath = null)
{
    /// <summary>Steps accumulated across every run that has trained this checkpoint.</summary>
    public int TotalSteps => LossHistory.Count;
}

public record GptTrainOptions(
    int Steps = 2000,
    int BatchSize = 16,
    int BlockSize = 64,
    int NEmbd = 128,
    int NHead = 4,
    int NLayer = 3,
    float LearningRate = 3e-4f,
    int Seed = 1337,
    bool Resume = false,
    TokenizerKind Tokenizer = TokenizerKind.Bpe,
    int VocabSize = 512,
    string? LossGraphPath = null);

/// <summary>
/// Trains a tiny char-level GPT from a text corpus by gradient descent and writes a checkpoint.
/// With <see cref="GptTrainOptions.Resume"/> set, continues from the checkpoint in the output
/// directory instead of initializing fresh weights (architecture options are then taken from
/// the checkpoint, and saved optimizer state is restored when present).
/// The loss of every step is recorded, and written out as an HTML graph when
/// <see cref="GptTrainOptions.LossGraphPath"/> is set.
/// </summary>
public class GptTrainComponent
{
    private readonly ICheckpointService _checkpointService;
    private readonly ILossGraphService _lossGraphService;

    public GptTrainComponent(ICheckpointService checkpointService, ILossGraphService lossGraphService)
    {
        _checkpointService = checkpointService;
        _lossGraphService = lossGraphService;
    }

    public async Task<GptTrainResult> TrainAsync(
        string corpus,
        string outputDirectory,
        GptTrainOptions options,
        Action<int, float>? onProgress = null)
    {
        if (string.IsNullOrEmpty(corpus))
            throw new ArgumentException("Training corpus is empty.", nameof(corpus));

        ITokenizer tokenizer;
        GptConfig config;
        GptModel model;
        AdamW optimizer;

        if (options.Resume)
        {
            var checkpoint = await _checkpointService.LoadAsync(outputDirectory);
            tokenizer = Tokenizers.Restore(checkpoint.Tokenizer);
            config = checkpoint.Config;
            model = new GptModel(config);
            model.LoadWeights(checkpoint.Weights);

            optimizer = new AdamW(model.Parameters(), options.LearningRate);
            var optimizerState = await _checkpointService.TryLoadOptimizerStateAsync(outputDirectory);
            if (optimizerState is not null)
                optimizer.LoadState(optimizerState);
        }
        else
        {
            if (options.NEmbd % options.NHead != 0)
                throw new ArgumentException($"n-embd ({options.NEmbd}) must be divisible by n-head ({options.NHead}).");

            tokenizer = Tokenizers.Build(options.Tokenizer, corpus, options.VocabSize);
            config = new GptConfig(tokenizer.VocabSize, options.BlockSize, options.NEmbd, options.NHead, options.NLayer);
            model = new GptModel(config);
            model.InitWeights(options.Seed);
            optimizer = new AdamW(model.Parameters(), options.LearningRate);
        }

        var data = tokenizer.Encode(corpus);

        if (data.Length < config.BlockSize + 1)
            throw new ArgumentException(
                $"Corpus is too short ({data.Length} chars) for block size {config.BlockSize}; need at least {config.BlockSize + 1}.");

        var rng = new Random(options.Seed);

        // Resuming picks up the checkpoint's recorded history so the graph spans every run, not just this one.
        var priorHistory = options.Resume
            ? await _checkpointService.TryLoadTrainingHistoryAsync(outputDirectory)
            : null;

        // Batches are sampled from random offsets, so track which corpus positions and which vocabulary
        // entries have been visited — a low coverage means much of the corpus never trained. The visited
        // sets only carry over when the corpus is unchanged; otherwise they describe text this run is not
        // training on, so coverage restarts from this run's samples.
        bool coverageCarriesOver = priorHistory is not null
            && priorHistory.CorpusTokenCount == data.Length
            && priorHistory.VocabSeen.Length == config.VocabSize;

        var positionSeen = coverageCarriesOver ? priorHistory!.PositionSeen : new bool[data.Length];
        var vocabSeen = coverageCarriesOver ? priorHistory!.VocabSeen : new bool[config.VocabSize];
        int positionsSeen = CountSet(positionSeen);
        int vocabEntriesSeen = CountSet(vocabSeen);

        // Every step's loss and coverage is retained so the emitted graph has full granularity.
        var lossHistory = new List<float>(options.Steps + (priorHistory?.Losses.Length ?? 0));
        var tokensSeenHistory = new List<int>(lossHistory.Capacity);
        var runBoundaries = new List<int>();
        if (priorHistory is not null)
        {
            lossHistory.AddRange(priorHistory.Losses);
            tokensSeenHistory.AddRange(priorHistory.TokensSeen);
            runBoundaries.AddRange(priorHistory.RunBoundaries);
        }

        float lastLoss = 0f;
        for (int step = 1; step <= options.Steps; step++)
        {
            var (inputs, targets, starts) = SampleBatch(data, options.BatchSize, config.BlockSize, rng);

            foreach (int start in starts)
            {
                // A window trains on its block-size inputs plus the final shifted target token.
                for (int offset = 0; offset <= config.BlockSize; offset++)
                {
                    int position = start + offset;
                    if (!positionSeen[position])
                    {
                        positionSeen[position] = true;
                        positionsSeen++;
                    }

                    int token = data[position];
                    if (!vocabSeen[token])
                    {
                        vocabSeen[token] = true;
                        vocabEntriesSeen++;
                    }
                }
            }

            model.ZeroGrad();
            var (_, loss) = model.Forward(inputs, targets);
            loss!.Backward();
            optimizer.Step();

            lastLoss = loss.Data[0];
            lossHistory.Add(lastLoss);
            tokensSeenHistory.Add(positionsSeen);
            onProgress?.Invoke(step, lastLoss);
        }

        runBoundaries.Add(lossHistory.Count);

        await _checkpointService.SaveAsync(outputDirectory, config, tokenizer.ExportState(), model.Parameters());
        await _checkpointService.SaveOptimizerStateAsync(outputDirectory, optimizer.ExportState());
        await _checkpointService.SaveTrainingHistoryAsync(outputDirectory, new TrainingHistory(
            lossHistory.ToArray(), tokensSeenHistory.ToArray(), runBoundaries.ToArray(),
            data.Length, positionSeen, vocabSeen));

        int parameterCount = model.ParameterCount();

        var lossGraphPath = string.IsNullOrWhiteSpace(options.LossGraphPath) ? null : options.LossGraphPath;
        if (lossGraphPath is not null)
        {
            var report = new LossGraphReport(
                lossHistory, tokensSeenHistory, runBoundaries, config, options.BatchSize, options.LearningRate,
                tokenizer.VocabSize, vocabEntriesSeen, data.Length, parameterCount, outputDirectory, options.Resume);
            await _lossGraphService.WriteAsync(lossGraphPath, report);
        }

        return new GptTrainResult(
            options.Steps, lastLoss, outputDirectory, parameterCount, tokenizer.VocabSize, data.Length,
            lossHistory, tokensSeenHistory, runBoundaries, positionsSeen, vocabEntriesSeen, lossGraphPath);
    }

    private static int CountSet(bool[] flags)
    {
        int count = 0;
        foreach (var flag in flags)
        {
            if (flag)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Draws <paramref name="batchSize"/> random windows from the corpus. The window start offsets are
    /// returned alongside the batch so the caller can track which corpus positions have been visited.
    /// </summary>
    internal static (int[][] Inputs, int[][] Targets, int[] Starts) SampleBatch(int[] data, int batchSize, int blockSize, Random rng)
    {
        var inputs = new int[batchSize][];
        var targets = new int[batchSize][];
        var starts = new int[batchSize];
        int maxStart = data.Length - blockSize - 1;

        for (int b = 0; b < batchSize; b++)
        {
            int start = rng.Next(maxStart + 1);
            var inp = new int[blockSize];
            var tgt = new int[blockSize];
            for (int t = 0; t < blockSize; t++)
            {
                inp[t] = data[start + t];
                tgt[t] = data[start + t + 1];
            }
            inputs[b] = inp;
            targets[b] = tgt;
            starts[b] = start;
        }

        return (inputs, targets, starts);
    }
}
