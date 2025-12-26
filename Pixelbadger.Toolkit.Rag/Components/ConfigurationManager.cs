using System.Text.Json;

namespace Pixelbadger.Toolkit.Rag.Components;

public class ConfigurationManager
{
    private static readonly string ConfigDirectory = GetConfigDirectory();
    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    private static string GetConfigDirectory()
    {
        // Follow OS-specific conventions:
        // - Linux/macOS: ~/.config/pbrag
        // - Windows: %APPDATA%\pbrag
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // On Linux/macOS, ApplicationData returns ~/.config, on Windows it returns AppData\Roaming
        return Path.Combine(baseDir, "pbrag");
    }

    public class Config
    {
        public string? DefaultIndexPath { get; set; }
        public int? DefaultMaxResults { get; set; }
        public string? DefaultChunkingStrategy { get; set; }
    }

    public Config LoadConfig()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return new Config();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to load config file: {ex.Message}");
            return new Config();
        }
    }

    public void SaveConfig(Config config)
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(ConfigDirectory);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save config file: {ex.Message}", ex);
        }
    }

    public void SetValue(string key, string value)
    {
        var config = LoadConfig();

        switch (key.ToLowerInvariant())
        {
            case "index-path":
            case "indexpath":
            case "default-index-path":
                config.DefaultIndexPath = value;
                break;
            case "max-results":
            case "maxresults":
            case "default-max-results":
                if (!int.TryParse(value, out var maxResults))
                {
                    throw new ArgumentException($"Invalid value for max-results: '{value}'. Must be an integer.");
                }
                config.DefaultMaxResults = maxResults;
                break;
            case "chunking-strategy":
            case "chunkingstrategy":
            case "default-chunking-strategy":
                var validStrategies = new[] { "semantic", "markdown", "paragraph" };
                if (!validStrategies.Contains(value.ToLowerInvariant()))
                {
                    throw new ArgumentException($"Invalid chunking strategy: '{value}'. Must be one of: {string.Join(", ", validStrategies)}");
                }
                config.DefaultChunkingStrategy = value.ToLowerInvariant();
                break;
            default:
                throw new ArgumentException($"Unknown configuration key: '{key}'. Valid keys are: index-path, max-results, chunking-strategy");
        }

        SaveConfig(config);
    }

    public string? GetValue(string key)
    {
        var config = LoadConfig();

        return key.ToLowerInvariant() switch
        {
            "index-path" or "indexpath" or "default-index-path" => config.DefaultIndexPath,
            "max-results" or "maxresults" or "default-max-results" => config.DefaultMaxResults?.ToString(),
            "chunking-strategy" or "chunkingstrategy" or "default-chunking-strategy" => config.DefaultChunkingStrategy,
            _ => throw new ArgumentException($"Unknown configuration key: '{key}'. Valid keys are: index-path, max-results, chunking-strategy")
        };
    }

    public void UnsetValue(string key)
    {
        var config = LoadConfig();

        switch (key.ToLowerInvariant())
        {
            case "index-path":
            case "indexpath":
            case "default-index-path":
                config.DefaultIndexPath = null;
                break;
            case "max-results":
            case "maxresults":
            case "default-max-results":
                config.DefaultMaxResults = null;
                break;
            case "chunking-strategy":
            case "chunkingstrategy":
            case "default-chunking-strategy":
                config.DefaultChunkingStrategy = null;
                break;
            default:
                throw new ArgumentException($"Unknown configuration key: '{key}'. Valid keys are: index-path, max-results, chunking-strategy");
        }

        SaveConfig(config);
    }

    public string GetConfigFilePath()
    {
        return ConfigFilePath;
    }

    public Dictionary<string, string?> ListAll()
    {
        var config = LoadConfig();
        return new Dictionary<string, string?>
        {
            { "index-path", config.DefaultIndexPath },
            { "max-results", config.DefaultMaxResults?.ToString() },
            { "chunking-strategy", config.DefaultChunkingStrategy }
        };
    }
}
