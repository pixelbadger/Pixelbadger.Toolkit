using System.Text.Json;

namespace Pixelbadger.Toolkit.Services;

public class MarkovModelService : IMarkovModelService
{
    private const string ModelFileName = "model.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public async Task SaveModelAsync(string directory, Dictionary<string, List<string>> model)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(model, SerializerOptions);
        await File.WriteAllTextAsync(Path.Combine(directory, ModelFileName), json);
    }

    public async Task<Dictionary<string, List<string>>> LoadModelAsync(string directory)
    {
        var path = Path.Combine(directory, ModelFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No Markov model found at '{path}'. Run 'markov train' first.");

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
            ?? throw new InvalidDataException("Failed to deserialize Markov model.");
    }
}
