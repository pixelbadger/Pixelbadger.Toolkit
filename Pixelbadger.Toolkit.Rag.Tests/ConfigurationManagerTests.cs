using FluentAssertions;
using Pixelbadger.Toolkit.Rag.Components;

namespace Pixelbadger.Toolkit.Rag.Tests;

public class ConfigurationManagerTests : IDisposable
{
    private readonly string _originalAppData;
    private readonly string _testDirectory;
    private readonly ConfigurationManager _configManager;

    public ConfigurationManagerTests()
    {
        // Create a temporary directory for test config
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        // Override ApplicationData directory to test directory
        _originalAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Environment.SetEnvironmentVariable("APPDATA", _testDirectory); // Windows
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _testDirectory); // Linux/macOS

        _configManager = new ConfigurationManager();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);

        // Restore original environment
        Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
    }

    [Fact]
    public void LoadConfig_ShouldReturnEmptyConfig_WhenConfigFileDoesNotExist()
    {
        var config = _configManager.LoadConfig();

        config.Should().NotBeNull();
        config.DefaultIndexPath.Should().BeNull();
        config.DefaultMaxResults.Should().BeNull();
        config.DefaultChunkingStrategy.Should().BeNull();
    }

    [Fact]
    public void SaveConfig_ShouldCreateConfigFile_WhenConfigDoesNotExist()
    {
        var config = new ConfigurationManager.Config
        {
            DefaultIndexPath = "./test-index",
            DefaultMaxResults = 15,
            DefaultChunkingStrategy = "markdown"
        };

        _configManager.SaveConfig(config);

        var configPath = _configManager.GetConfigFilePath();
        File.Exists(configPath).Should().BeTrue();
    }

    [Fact]
    public void SaveAndLoadConfig_ShouldPersistValues_WhenValidConfigProvided()
    {
        var config = new ConfigurationManager.Config
        {
            DefaultIndexPath = "./my-index",
            DefaultMaxResults = 20,
            DefaultChunkingStrategy = "paragraph"
        };

        _configManager.SaveConfig(config);
        var loadedConfig = _configManager.LoadConfig();

        loadedConfig.DefaultIndexPath.Should().Be("./my-index");
        loadedConfig.DefaultMaxResults.Should().Be(20);
        loadedConfig.DefaultChunkingStrategy.Should().Be("paragraph");
    }

    [Theory]
    [InlineData("index-path", "./test-path")]
    [InlineData("indexpath", "./another-path")]
    [InlineData("default-index-path", "./default-path")]
    public void SetValue_ShouldSetIndexPath_WhenValidKeyAndValueProvided(string key, string value)
    {
        _configManager.SetValue(key, value);

        var config = _configManager.LoadConfig();
        config.DefaultIndexPath.Should().Be(value);
    }

    [Theory]
    [InlineData("max-results", "25", 25)]
    [InlineData("maxresults", "10", 10)]
    [InlineData("default-max-results", "100", 100)]
    public void SetValue_ShouldSetMaxResults_WhenValidKeyAndValueProvided(string key, string value, int expected)
    {
        _configManager.SetValue(key, value);

        var config = _configManager.LoadConfig();
        config.DefaultMaxResults.Should().Be(expected);
    }

    [Theory]
    [InlineData("max-results", "not-a-number")]
    [InlineData("maxresults", "abc")]
    public void SetValue_ShouldThrowArgumentException_WhenInvalidMaxResultsProvided(string key, string value)
    {
        var act = () => _configManager.SetValue(key, value);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Invalid value for max-results: '{value}'. Must be an integer.");
    }

    [Theory]
    [InlineData("chunking-strategy", "semantic")]
    [InlineData("chunkingstrategy", "markdown")]
    [InlineData("default-chunking-strategy", "paragraph")]
    public void SetValue_ShouldSetChunkingStrategy_WhenValidKeyAndValueProvided(string key, string value)
    {
        _configManager.SetValue(key, value);

        var config = _configManager.LoadConfig();
        config.DefaultChunkingStrategy.Should().Be(value.ToLowerInvariant());
    }

    [Theory]
    [InlineData("chunking-strategy", "invalid")]
    [InlineData("chunkingstrategy", "unknown")]
    public void SetValue_ShouldThrowArgumentException_WhenInvalidChunkingStrategyProvided(string key, string value)
    {
        var act = () => _configManager.SetValue(key, value);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Invalid chunking strategy: '{value}'. Must be one of: semantic, markdown, paragraph");
    }

    [Fact]
    public void SetValue_ShouldThrowArgumentException_WhenUnknownKeyProvided()
    {
        var act = () => _configManager.SetValue("unknown-key", "value");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown configuration key: 'unknown-key'. Valid keys are: index-path, max-results, chunking-strategy");
    }

    [Fact]
    public void GetValue_ShouldReturnValue_WhenConfigValueIsSet()
    {
        _configManager.SetValue("index-path", "./my-index");
        _configManager.SetValue("max-results", "15");
        _configManager.SetValue("chunking-strategy", "markdown");

        _configManager.GetValue("index-path").Should().Be("./my-index");
        _configManager.GetValue("max-results").Should().Be("15");
        _configManager.GetValue("chunking-strategy").Should().Be("markdown");
    }

    [Fact]
    public void GetValue_ShouldReturnNull_WhenConfigValueIsNotSet()
    {
        var value = _configManager.GetValue("index-path");

        value.Should().BeNull();
    }

    [Fact]
    public void GetValue_ShouldThrowArgumentException_WhenUnknownKeyProvided()
    {
        var act = () => _configManager.GetValue("unknown-key");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown configuration key: 'unknown-key'. Valid keys are: index-path, max-results, chunking-strategy");
    }

    [Fact]
    public void UnsetValue_ShouldRemoveValue_WhenConfigValueIsSet()
    {
        _configManager.SetValue("index-path", "./test-index");
        _configManager.UnsetValue("index-path");

        var config = _configManager.LoadConfig();
        config.DefaultIndexPath.Should().BeNull();
    }

    [Fact]
    public void UnsetValue_ShouldThrowArgumentException_WhenUnknownKeyProvided()
    {
        var act = () => _configManager.UnsetValue("unknown-key");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown configuration key: 'unknown-key'. Valid keys are: index-path, max-results, chunking-strategy");
    }

    [Fact]
    public void ListAll_ShouldReturnAllConfigValues_WhenConfigIsSet()
    {
        _configManager.SetValue("index-path", "./my-index");
        _configManager.SetValue("max-results", "20");
        _configManager.SetValue("chunking-strategy", "semantic");

        var values = _configManager.ListAll();

        values.Should().ContainKey("index-path").WhoseValue.Should().Be("./my-index");
        values.Should().ContainKey("max-results").WhoseValue.Should().Be("20");
        values.Should().ContainKey("chunking-strategy").WhoseValue.Should().Be("semantic");
    }

    [Fact]
    public void ListAll_ShouldReturnNullValues_WhenConfigIsEmpty()
    {
        var values = _configManager.ListAll();

        values.Should().ContainKey("index-path").WhoseValue.Should().BeNull();
        values.Should().ContainKey("max-results").WhoseValue.Should().BeNull();
        values.Should().ContainKey("chunking-strategy").WhoseValue.Should().BeNull();
    }

    [Fact]
    public void GetConfigFilePath_ShouldReturnValidPath()
    {
        var path = _configManager.GetConfigFilePath();

        path.Should().NotBeNullOrWhiteSpace();
        path.Should().EndWith("config.json");
        path.Should().Contain("pbrag");
    }
}
