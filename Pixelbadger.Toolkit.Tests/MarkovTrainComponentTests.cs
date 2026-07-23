using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class MarkovTrainComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IMarkovModelService> _mockModelService;
    private readonly MarkovTrainComponent _component;

    public MarkovTrainComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _mockModelService = new Mock<IMarkovModelService>();
        _component = new MarkovTrainComponent(_mockModelService.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task TrainAsync_ShouldSaveModelAndReturnUniqueWordCount()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "the cat sat on the mat");
        _mockModelService.Setup(s => s.SaveModelAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, List<string>>>()))
            .Returns(Task.CompletedTask);

        // Act
        var uniqueWords = await _component.TrainAsync(sourceFile, _testDirectory);

        // Assert
        uniqueWords.Should().Be(4); // the, cat, sat, on (mat has no successor, so it is never a key)
        _mockModelService.Verify(s => s.SaveModelAsync(_testDirectory, It.IsAny<Dictionary<string, List<string>>>()), Times.Once);
    }

    [Fact]
    public async Task TrainAsync_ShouldThrowFileNotFoundException_WhenSourceFileDoesNotExist()
    {
        // Arrange
        var missingFile = Path.Combine(_testDirectory, "missing.txt");

        // Act
        var act = async () => await _component.TrainAsync(missingFile, _testDirectory);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"*'{missingFile}'*");
    }

    [Fact]
    public void BuildModel_ShouldBuildCorrectTransitions()
    {
        // Arrange
        var text = "the cat sat on the mat";

        // Act
        var model = MarkovTrainComponent.BuildModel(text);

        // Assert
        model.Should().ContainKey("the");
        model["the"].Should().BeEquivalentTo(["cat", "mat"]);
        model.Should().ContainKey("cat");
        model["cat"].Should().BeEquivalentTo(["sat"]);
        model.Should().ContainKey("sat");
        model["sat"].Should().BeEquivalentTo(["on"]);
        model.Should().ContainKey("on");
        model["on"].Should().BeEquivalentTo(["the"]);
        model.Should().NotContainKey("mat");
    }

    [Fact]
    public void BuildModel_ShouldPreserveFrequencyInTransitions()
    {
        // Arrange
        var text = "the cat the dog the cat";

        // Act
        var model = MarkovTrainComponent.BuildModel(text);

        // Assert
        model["the"].Should().HaveCount(3);
        model["the"].Where(w => w == "cat").Should().HaveCount(2);
        model["the"].Where(w => w == "dog").Should().HaveCount(1);
    }

    [Fact]
    public void BuildModel_ShouldHandleSingleWord()
    {
        // Arrange
        var text = "hello";

        // Act
        var model = MarkovTrainComponent.BuildModel(text);

        // Assert
        model.Should().BeEmpty();
    }

    [Fact]
    public void BuildModel_ShouldHandleEmptyText()
    {
        // Arrange
        var text = "";

        // Act
        var model = MarkovTrainComponent.BuildModel(text);

        // Assert
        model.Should().BeEmpty();
    }

    [Fact]
    public void BuildModel_ShouldSplitOnMultipleWhitespaceTypes()
    {
        // Arrange
        var text = "hello\tworld\nfoo\r\nbar";

        // Act
        var model = MarkovTrainComponent.BuildModel(text);

        // Assert
        model.Should().ContainKey("hello");
        model["hello"].Should().BeEquivalentTo(["world"]);
        model.Should().ContainKey("world");
        model["world"].Should().BeEquivalentTo(["foo"]);
        model.Should().ContainKey("foo");
        model["foo"].Should().BeEquivalentTo(["bar"]);
    }
}
