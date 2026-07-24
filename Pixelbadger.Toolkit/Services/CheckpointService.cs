using System.Text.Json;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// Persists and restores a GPT checkpoint as a directory containing a JSON sidecar
/// (model config + vocabulary) and a binary blob of the weight tensors.
/// </summary>
public sealed class CheckpointService : ICheckpointService
{
    internal const string ConfigFileName = "config.json";
    internal const string WeightsFileName = "weights.bin";
    internal const string OptimizerFileName = "optimizer.bin";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private sealed record ConfigDto(
        int VocabSize, int BlockSize, int NEmbd, int NHead, int NLayer,
        TokenizerKind TokenizerKind, string? Vocabulary, int[][]? Merges);

    public async Task SaveAsync(
        string directory, GptConfig config, TokenizerState tokenizer, IReadOnlyList<Tensor> parameters)
    {
        Directory.CreateDirectory(directory);

        var dto = new ConfigDto(
            config.VocabSize, config.BlockSize, config.NEmbd, config.NHead, config.NLayer,
            tokenizer.Kind, tokenizer.Vocabulary, tokenizer.Merges);
        var json = JsonSerializer.Serialize(dto, SerializerOptions);
        await File.WriteAllTextAsync(Path.Combine(directory, ConfigFileName), json);

        await using var stream = File.Create(Path.Combine(directory, WeightsFileName));
        await using var writer = new BinaryWriter(stream);
        writer.Write(parameters.Count);
        foreach (var p in parameters)
        {
            writer.Write(p.Length);
            for (int i = 0; i < p.Length; i++)
                writer.Write(p.Data[i]);
        }
    }

    public async Task<GptCheckpoint> LoadAsync(string directory)
    {
        var configPath = Path.Combine(directory, ConfigFileName);
        var weightsPath = Path.Combine(directory, WeightsFileName);

        if (!File.Exists(configPath) || !File.Exists(weightsPath))
            throw new FileNotFoundException(
                $"No GPT checkpoint found in '{directory}'. Run 'gpt train' first.");

        var json = await File.ReadAllTextAsync(configPath);
        var dto = JsonSerializer.Deserialize<ConfigDto>(json)
            ?? throw new InvalidDataException("Failed to deserialize GPT config.");

        var config = new GptConfig(dto.VocabSize, dto.BlockSize, dto.NEmbd, dto.NHead, dto.NLayer);

        await using var stream = File.OpenRead(weightsPath);
        using var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        var weights = new float[count][];
        for (int p = 0; p < count; p++)
        {
            int length = reader.ReadInt32();
            var data = new float[length];
            for (int i = 0; i < length; i++)
                data[i] = reader.ReadSingle();
            weights[p] = data;
        }

        var tokenizer = new TokenizerState(dto.TokenizerKind, dto.Vocabulary, dto.Merges);
        return new GptCheckpoint(config, tokenizer, weights);
    }

    public Task SaveOptimizerStateAsync(string directory, AdamWState state)
    {
        Directory.CreateDirectory(directory);

        using var stream = File.Create(Path.Combine(directory, OptimizerFileName));
        using var writer = new BinaryWriter(stream);
        writer.Write(state.Step);
        writer.Write(state.M.Length);
        for (int p = 0; p < state.M.Length; p++)
        {
            writer.Write(state.M[p].Length);
            for (int i = 0; i < state.M[p].Length; i++)
                writer.Write(state.M[p][i]);
            for (int i = 0; i < state.V[p].Length; i++)
                writer.Write(state.V[p][i]);
        }

        return Task.CompletedTask;
    }

    public Task<AdamWState?> TryLoadOptimizerStateAsync(string directory)
    {
        var path = Path.Combine(directory, OptimizerFileName);
        if (!File.Exists(path))
            return Task.FromResult<AdamWState?>(null);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        int step = reader.ReadInt32();
        int count = reader.ReadInt32();
        var m = new float[count][];
        var v = new float[count][];
        for (int p = 0; p < count; p++)
        {
            int length = reader.ReadInt32();
            m[p] = new float[length];
            v[p] = new float[length];
            for (int i = 0; i < length; i++)
                m[p][i] = reader.ReadSingle();
            for (int i = 0; i < length; i++)
                v[p][i] = reader.ReadSingle();
        }

        return Task.FromResult<AdamWState?>(new AdamWState(step, m, v));
    }
}
