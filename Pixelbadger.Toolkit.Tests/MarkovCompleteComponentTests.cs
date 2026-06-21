using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class MarkovCompleteComponentTests
{
    private readonly Mock<IMarkovModelService> _mockModelService;

    public MarkovCompleteComponentTests()
    {
        _mockModelService = new Mock<IMarkovModelService>();
    }

    private static Dictionary<string, List<string>> SimpleModel() => new()
    {
        ["the"] = ["cat", "dog"],
        ["cat"] = ["sat"],
        ["sat"] = ["on"],
        ["on"] = ["the"],
        ["dog"] = ["ran"]
    };

    [Fact]
    public async Task CompleteAsync_ShouldGenerateWordsFromModel()
    {
        // Arrange — single-path model so result is deterministic regardless of random
        var model = new Dictionary<string, List<string>>
        {
            ["the"] = ["cat"],
            ["cat"] = ["sat"],
            ["sat"] = ["on"]
        };
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(model);
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var result = await component.CompleteAsync("the", "/model", 3);

        // Assert
        result.Should().Be("the cat sat on");
    }

    [Fact]
    public async Task CompleteAsync_ShouldReturnInputText_WhenNoTransitionExists()
    {
        // Arrange
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(SimpleModel());
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var result = await component.CompleteAsync("ran", "/model", 10);

        // Assert
        result.Should().Be("ran");
    }

    [Fact]
    public async Task CompleteAsync_ShouldUseLastWordOfInputText()
    {
        // Arrange
        var model = new Dictionary<string, List<string>>
        {
            ["world"] = ["is"]
        };
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(model);
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var result = await component.CompleteAsync("hello world", "/model", 1);

        // Assert
        result.Should().Be("hello world is");
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrowArgumentException_WhenInputTextIsEmpty()
    {
        // Arrange
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(SimpleModel());
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var act = async () => await component.CompleteAsync("", "/model", 10);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrowArgumentException_WhenInputTextIsWhitespace()
    {
        // Arrange
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(SimpleModel());
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var act = async () => await component.CompleteAsync("   ", "/model", 10);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public async Task CompleteAsync_ShouldStopEarly_WhenChainEnds()
    {
        // Arrange
        var model = new Dictionary<string, List<string>>
        {
            ["start"] = ["middle"],
            ["middle"] = ["end"]
        };
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(model);
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var result = await component.CompleteAsync("start", "/model", 100);

        // Assert
        result.Should().Be("start middle end");
    }

    [Fact]
    public async Task CompleteAsync_ShouldGenerateExactWordCount_WhenChainIsCyclic()
    {
        // Arrange
        var model = new Dictionary<string, List<string>>
        {
            ["a"] = ["b"],
            ["b"] = ["a"]
        };
        _mockModelService.Setup(s => s.LoadModelAsync(It.IsAny<string>()))
            .ReturnsAsync(model);
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        var result = await component.CompleteAsync("a", "/model", 4);

        // Assert
        result.Should().Be("a b a b a");
    }

    [Fact]
    public async Task CompleteAsync_ShouldPassModelDirectoryToService()
    {
        // Arrange
        const string modelDir = "/my/model/dir";
        _mockModelService.Setup(s => s.LoadModelAsync(modelDir))
            .ReturnsAsync(SimpleModel());
        var component = new MarkovCompleteComponent(_mockModelService.Object);

        // Act
        await component.CompleteAsync("the", modelDir, 1);

        // Assert
        _mockModelService.Verify(s => s.LoadModelAsync(modelDir), Times.Once);
    }
}
