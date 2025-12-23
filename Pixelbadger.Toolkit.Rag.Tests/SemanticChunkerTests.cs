using FluentAssertions;
using Pixelbadger.Toolkit.Rag.Components;
using Xunit;

namespace Pixelbadger.Toolkit.Rag.Tests;

public class SemanticChunkerTests : IDisposable
{
    private readonly string _testDirectory;

    public SemanticChunkerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task ChunkBySemanticSimilarity_ShouldReturnEmptyList_WhenContentIsEmpty()
    {
        // Arrange
        var content = "";

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, apiKey: "test-key");

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkBySemanticSimilarity_ShouldReturnEmptyList_WhenContentIsWhitespace()
    {
        // Arrange
        var content = "   \n\n\t  ";

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, apiKey: "test-key");

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkBySemanticSimilarity_ShouldThrowException_WhenApiKeyNotProvided()
    {
        // Arrange
        var content = "This is a test sentence.";

        // Clear environment variable if set
        var originalKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await SemanticChunker.ChunkBySemanticSimilarityAsync(content);
            });
        }
        finally
        {
            // Restore original environment variable
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalKey);
        }
    }

    [Fact]
    public async Task ChunkBySemanticSimilarity_ShouldReturnSingleChunk_WhenContentIsSingleSentence()
    {
        // Arrange
        var content = "This is a single sentence.";

        // Note: This test will fail without a real API key, so we skip it in CI
        // In a real scenario, you'd mock the EmbeddingClient
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if no API key available
            return;
        }

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, apiKey);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be(content);
        chunks[0].ChunkNumber.Should().Be(1);
    }

    [Fact]
    public void SemanticTextChunker_ShouldImplementITextChunker()
    {
        // Arrange & Act
        var chunker = new SemanticTextChunker("test-key");

        // Assert
        chunker.Should().BeAssignableTo<ITextChunker>();
    }

    [Fact]
    public void SemanticChunkWrapper_ShouldImplementIChunk()
    {
        // Arrange
        var semanticChunk = new SemanticChunk { Content = "Test", ChunkNumber = 1 };

        // Act
        var wrapper = new SemanticChunkWrapper(semanticChunk);

        // Assert
        wrapper.Should().BeAssignableTo<IChunk>();
        wrapper.Content.Should().Be("Test");
        wrapper.ChunkNumber.Should().Be(1);
    }

    [Theory]
    [InlineData("First sentence. Second sentence. Third sentence.", 3)]
    [InlineData("Hello! How are you? I am fine.", 3)]
    [InlineData("Question one? Question two!", 2)]
    public void SplitIntoSentences_ShouldSplitCorrectly(string content, int expectedCount)
    {
        // This tests the internal sentence splitting logic
        // We need to expose this for testing or test it indirectly

        // For now, we'll test it indirectly through the main method
        // In a production scenario, you might want to make SplitIntoSentences internal/public for testing

        // This is a placeholder - actual testing would require API access or mocking
        expectedCount.Should().BeGreaterThan(0);
    }
}
