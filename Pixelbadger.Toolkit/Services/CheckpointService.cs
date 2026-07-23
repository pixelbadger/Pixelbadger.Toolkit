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

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private sealed record ConfigDto(
        int VocabSize, int BlockSize, int NEmbd, int NHead, int NLayer, string Vocabulary);

    public async Task SaveAsync(
        string directory, GptConfig config, IReadOnlyList<char> vocabulary, IReadOnlyList<Tensor> parameters)
    {
        Directory.CreateDirectory(directory);

        var dto = new ConfigDto(
            config.VocabSize, config.BlockSize, config.NEmbd, config.NHead, config.NLayer,
            new string(vocabulary.ToArray()));
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

        return new GptCheckpoint(config, dto.Vocabulary.ToCharArray(), weights);
    }
}
