using FluentAssertions;
using Pixelbadger.Toolkit.Rag.Components;
using Xunit;

namespace Pixelbadger.Toolkit.Rag.Tests;

public class SemanticChunkerTests
{

    [Fact]
    public async Task ChunkBySemanticSimilarity_ShouldReturnEmptyList_WhenContentIsEmpty()
    {
        // Arrange
        var content = "";
        var mockService = new MockEmbeddingService();

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, mockService);

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkBySemanticSimilarity_ShouldReturnEmptyList_WhenContentIsWhitespace()
    {
        // Arrange
        var content = "   \n\n\t  ";
        var mockService = new MockEmbeddingService();

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, mockService);

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void OpenAIEmbeddingService_ShouldThrowException_WhenApiKeyNotProvided()
    {
        // Arrange - Clear environment variable if set
        var originalKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        try
        {
            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                var service = new OpenAIEmbeddingService();
            });

            exception.Message.Should().Contain("OpenAI API key must be provided");
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
        var mockService = new MockEmbeddingService();

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, mockService);

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
    public async Task ChunkBySemanticSimilarity_ShouldCreateMultipleChunks_WhenContentHasMultipleSentences(string content, int expectedSentenceCount)
    {
        // Arrange
        var mockService = new MockEmbeddingService();

        // Act
        var chunks = await SemanticChunker.ChunkBySemanticSimilarityAsync(content, mockService);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().HaveCountGreaterThan(0);

        // Verify all chunks have content
        foreach (var chunk in chunks)
        {
            chunk.Content.Should().NotBeNullOrWhiteSpace();
            chunk.ChunkNumber.Should().BeGreaterThan(0);
        }

        // Verify chunk numbers are sequential
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkNumber.Should().Be(i + 1);
        }

        // Verify total content is preserved (all sentences included)
        var totalContent = string.Join(" ", chunks.Select(c => c.Content));
        var sentenceCount = totalContent.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
        sentenceCount.Should().Be(expectedSentenceCount);
    }
}
