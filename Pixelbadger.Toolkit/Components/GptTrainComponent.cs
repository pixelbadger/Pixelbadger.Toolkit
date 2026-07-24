using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public record GptTrainResult(int Steps, float FinalLoss, string CheckpointPath, int ParameterCount, int VocabSize, int CorpusTokenCount);

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
    int VocabSize = 512);

/// <summary>
/// Trains a tiny char-level GPT from a text corpus by gradient descent and writes a checkpoint.
/// With <see cref="GptTrainOptions.Resume"/> set, continues from the checkpoint in the output
/// directory instead of initializing fresh weights (architecture options are then taken from
/// the checkpoint, and saved optimizer state is restored when present).
/// </summary>
public class GptTrainComponent
{
    private readonly ICheckpointService _checkpointService;

    public GptTrainComponent(ICheckpointService checkpointService)
    {
        _checkpointService = checkpointService;
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

        float lastLoss = 0f;
        for (int step = 1; step <= options.Steps; step++)
        {
            var (inputs, targets) = SampleBatch(data, options.BatchSize, config.BlockSize, rng);

            model.ZeroGrad();
            var (_, loss) = model.Forward(inputs, targets);
            loss!.Backward();
            optimizer.Step();

            lastLoss = loss.Data[0];
            onProgress?.Invoke(step, lastLoss);
        }

        await _checkpointService.SaveAsync(outputDirectory, config, tokenizer.ExportState(), model.Parameters());
        await _checkpointService.SaveOptimizerStateAsync(outputDirectory, optimizer.ExportState());

        return new GptTrainResult(options.Steps, lastLoss, outputDirectory, model.ParameterCount(), tokenizer.VocabSize, data.Length);
    }

    internal static (int[][] Inputs, int[][] Targets) SampleBatch(int[] data, int batchSize, int blockSize, Random rng)
    {
        var inputs = new int[batchSize][];
        var targets = new int[batchSize][];
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
        }

        return (inputs, targets);
    }
}
