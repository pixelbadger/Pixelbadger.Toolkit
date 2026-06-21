using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class MarkovModelServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly MarkovModelService _service;

    public MarkovModelServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _service = new MarkovModelService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task SaveModelAsync_ShouldCreateDirectoryAndModelFile()
    {
        // Arrange
        var model = new Dictionary<string, List<string>>
        {
            ["hello"] = ["world", "there"],
            ["world"] = ["hello"]
        };

        // Act
        await _service.SaveModelAsync(_testDirectory, model);

        // Assert
        Directory.Exists(_testDirectory).Should().BeTrue();
        File.Exists(Path.Combine(_testDirectory, "model.json")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadModelAsync_ShouldReturnSavedModel()
    {
        // Arrange
        var model = new Dictionary<string, List<string>>
        {
            ["hello"] = ["world", "there"],
            ["world"] = ["hello"]
        };
        await _service.SaveModelAsync(_testDirectory, model);

        // Act
        var loaded = await _service.LoadModelAsync(_testDirectory);

        // Assert
        loaded.Should().ContainKey("hello");
        loaded["hello"].Should().BeEquivalentTo(["world", "there"]);
        loaded.Should().ContainKey("world");
        loaded["world"].Should().BeEquivalentTo(["hello"]);
    }

    [Fact]
    public async Task LoadModelAsync_ShouldThrowFileNotFoundException_WhenModelDoesNotExist()
    {
        // Arrange
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var act = async () => await _service.LoadModelAsync(missingDirectory);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*markov train*");
    }

    [Fact]
    public async Task SaveModelAsync_ShouldPreserveDuplicatesInValueLists()
    {
        // Arrange
        var model = new Dictionary<string, List<string>>
        {
            ["the"] = ["cat", "dog", "cat", "cat"]
        };
        await _service.SaveModelAsync(_testDirectory, model);

        // Act
        var loaded = await _service.LoadModelAsync(_testDirectory);

        // Assert
        loaded["the"].Should().HaveCount(4);
        loaded["the"].Where(w => w == "cat").Should().HaveCount(3);
    }
}
