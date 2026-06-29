using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public record GptCompleteResult(string Text);

/// <summary>
/// Loads a trained GPT checkpoint and autoregressively generates a continuation of a prompt.
/// Temperature 0 yields deterministic greedy decoding.
/// </summary>
public class GptCompleteComponent
{
    private readonly ICheckpointService _checkpointService;

    public GptCompleteComponent(ICheckpointService checkpointService)
    {
        _checkpointService = checkpointService;
    }

    public async Task<GptCompleteResult> CompleteAsync(
        string modelDirectory,
        string prompt,
        int maxTokens,
        float temperature,
        int seed)
    {
        var checkpoint = await _checkpointService.LoadAsync(modelDirectory);
        var tokenizer = CharTokenizer.FromVocabulary(checkpoint.Vocabulary);

        var model = new GptModel(checkpoint.Config);
        model.LoadWeights(checkpoint.Weights);

        var ids = new List<int>();
        if (!string.IsNullOrEmpty(prompt))
            ids.AddRange(tokenizer.Encode(prompt));
        if (ids.Count == 0)
            ids.Add(0); // Seed with the first vocabulary symbol when no prompt is given.

        var rng = new Random(seed);
        int blockSize = checkpoint.Config.BlockSize;
        int vocab = checkpoint.Config.VocabSize;

        for (int i = 0; i < maxTokens; i++)
        {
            int contextLength = Math.Min(ids.Count, blockSize);
            var context = new int[contextLength];
            for (int j = 0; j < contextLength; j++)
                context[j] = ids[ids.Count - contextLength + j];

            var (logits, _) = model.Forward(new[] { context });

            // Logits for the final time-step.
            var lastLogits = new float[vocab];
            Array.Copy(logits.Data, (contextLength - 1) * vocab, lastLogits, 0, vocab);

            int next = temperature <= 0f
                ? ArgMax(lastLogits)
                : SampleWithTemperature(lastLogits, temperature, rng);

            ids.Add(next);
        }

        return new GptCompleteResult(tokenizer.Decode(ids));
    }

    internal static int ArgMax(float[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] > values[best])
                best = i;
        return best;
    }

    internal static int SampleWithTemperature(float[] logits, float temperature, Random rng)
    {
        int n = logits.Length;
        var probs = new float[n];
        float max = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            probs[i] = logits[i] / temperature;
            if (probs[i] > max) max = probs[i];
        }

        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            probs[i] = MathF.Exp(probs[i] - max);
            sum += probs[i];
        }

        float r = (float)rng.NextDouble() * sum;
        float cumulative = 0f;
        for (int i = 0; i < n; i++)
        {
            cumulative += probs[i];
            if (r <= cumulative)
                return i;
        }
        return n - 1;
    }
}
