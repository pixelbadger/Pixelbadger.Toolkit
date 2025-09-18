using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class CorpospeakComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IOpenAiClientService> _mockOpenAiService;
    private readonly CorpospeakComponent _corpospeakComponent;

    public CorpospeakComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        _mockOpenAiService = new Mock<IOpenAiClientService>();
        _corpospeakComponent = new CorpospeakComponent(_mockOpenAiService.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldReturnRewrittenText_ForValidInput()
    {
        // Arrange
        var source = "API performance is great";
        var audience = "csuite";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Our API infrastructure delivers exceptional performance metrics, driving significant operational efficiency and competitive advantage.";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
    }

    [Theory]
    [InlineData("csuite")]
    [InlineData("c-suite")]
    [InlineData("executive")]
    [InlineData("leadership")]
    [InlineData("engineering")]
    [InlineData("technical")]
    [InlineData("dev")]
    [InlineData("developers")]
    [InlineData("product")]
    [InlineData("pm")]
    [InlineData("product-management")]
    [InlineData("sales")]
    [InlineData("business-development")]
    [InlineData("revenue")]
    [InlineData("marketing")]
    [InlineData("growth")]
    [InlineData("demand-gen")]
    [InlineData("operations")]
    [InlineData("ops")]
    [InlineData("infrastructure")]
    [InlineData("finance")]
    [InlineData("financial")]
    [InlineData("accounting")]
    [InlineData("legal")]
    [InlineData("compliance")]
    [InlineData("risk")]
    [InlineData("hr")]
    [InlineData("human-resources")]
    [InlineData("people")]
    [InlineData("customer-success")]
    [InlineData("support")]
    [InlineData("cs")]
    public async Task CorpospeakAsync_ShouldAcceptValidAudience(string audience)
    {
        // Arrange
        var source = "Test message";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Rewritten test message";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldThrowArgumentException_ForInvalidAudience()
    {
        // Arrange
        var source = "Test message";
        var invalidAudience = "invalid-audience";
        var userMessages = Array.Empty<string>();

        // Act & Assert
        var act = async () => await _corpospeakComponent.CorpospeakAsync(source, invalidAudience, userMessages);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid audience 'invalid-audience'. Valid audiences: *");
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldHandleCaseInsensitiveAudience()
    {
        // Arrange
        var source = "Test message";
        var audience = "CSUITE"; // uppercase
        var userMessages = Array.Empty<string>();
        var expectedResult = "Executive-level rewritten message";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldHandleAudienceWithWhitespace()
    {
        // Arrange
        var source = "Test message";
        var audience = "  engineering  "; // with whitespace
        var userMessages = Array.Empty<string>();
        var expectedResult = "Technical rewritten message";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldReadSourceFromFile_WhenFileExists()
    {
        // Arrange
        var sourceFilePath = Path.Combine(_testDirectory, "source.txt");
        var fileContent = "This is content from a file.";
        await File.WriteAllTextAsync(sourceFilePath, fileContent);

        var audience = "engineering";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Technical version of file content";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(sourceFilePath, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldTreatSourceAsText_WhenFileDoesNotExist()
    {
        // Arrange
        var source = "This is just plain text, not a file path";
        var audience = "marketing";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Marketing-friendly rewritten text";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldReadUserMessagesFromFiles_WhenFilesExist()
    {
        // Arrange
        var userMessageFile1 = Path.Combine(_testDirectory, "user1.txt");
        var userMessageFile2 = Path.Combine(_testDirectory, "user2.txt");
        await File.WriteAllTextAsync(userMessageFile1, "User message from file 1");
        await File.WriteAllTextAsync(userMessageFile2, "User message from file 2");

        var source = "Test source";
        var audience = "sales";
        var userMessages = new[] { userMessageFile1, userMessageFile2 };
        var expectedResult = "Sales-optimized message";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldTreatUserMessagesAsText_WhenFilesDoNotExist()
    {
        // Arrange
        var source = "Test source";
        var audience = "product";
        var userMessages = new[] { "Direct user message 1", "Direct user message 2" };
        var expectedResult = "Product-focused message";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldBuildCorrectMessages_WithoutUserMessages()
    {
        // Arrange
        var source = "API performance is excellent";
        var audience = "csuite";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Executive summary";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedResult);

        // Act
        await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages.Should().HaveCount(1);
        capturedMessages![0].ToString().Should().Contain("User");
        capturedMessages[0].Content[0].Text.Should().Contain(source);
        capturedMessages[0].Content[0].Text.Should().Contain("C-suite executives");
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldBuildCorrectMessages_WithUserMessages()
    {
        // Arrange
        var source = "Feature released successfully";
        var audience = "engineering";
        var userMessages = new[] { "Hey team", "Great work everyone" };
        var expectedResult = "Technical update";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedResult);

        // Act
        await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages.Should().HaveCount(5); // 2 user messages + 2 assistant responses + 1 final prompt

        // Verify pattern: user, assistant, user, assistant, user (final prompt)
        capturedMessages![0].ToString().Should().Contain("User");
        capturedMessages[0].Content[0].Text.Should().Be("Hey team");

        capturedMessages[1].ToString().Should().Contain("Assistant");
        capturedMessages[1].Content[0].Text.Should().Be("I understand your writing style.");

        capturedMessages[2].ToString().Should().Contain("User");
        capturedMessages[2].Content[0].Text.Should().Be("Great work everyone");

        capturedMessages[3].ToString().Should().Contain("Assistant");
        capturedMessages[3].Content[0].Text.Should().Be("I understand your writing style.");

        capturedMessages[4].ToString().Should().Contain("User");
        capturedMessages[4].Content[0].Text.Should().Contain(source);
    }

    [Theory]
    [InlineData("csuite", "C-suite executives", "strategic", "business impact")]
    [InlineData("engineering", "Engineering teams", "technical precision", "implementation details")]
    [InlineData("product", "Product managers", "user impact", "feature priorities")]
    [InlineData("sales", "Sales teams", "value propositions", "competitive differentiators")]
    [InlineData("marketing", "Marketing professionals", "messaging", "brand positioning")]
    [InlineData("operations", "Operations teams", "scalability", "operational excellence")]
    [InlineData("finance", "Finance teams", "cost implications", "financial metrics")]
    [InlineData("legal", "Legal and compliance", "risk assessment", "compliance considerations")]
    [InlineData("hr", "HR and people", "impact on employees", "organizational dynamics")]
    [InlineData("customer-success", "Customer success", "customer impact", "customer experience")]
    public async Task CorpospeakAsync_ShouldGenerateCorrectAudienceContext(string audience, string expectedAudienceType, string expectedKeyword1, string expectedKeyword2)
    {
        // Arrange
        var source = "Test message for audience";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Audience-specific result";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedResult);

        // Act
        await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        capturedMessages.Should().NotBeNull();
        var promptText = capturedMessages![0].Content[0].Text;

        promptText.Should().Contain(expectedAudienceType);
        promptText.Should().Contain(expectedKeyword1);
        promptText.Should().Contain(expectedKeyword2);
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldIncludeIdiolectInstructions_WhenUserMessagesProvided()
    {
        // Arrange
        var source = "Test message";
        var audience = "engineering";
        var userMessages = new[] { "Sample user message" };
        var expectedResult = "Result with idiolect";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedResult);

        // Act
        await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        capturedMessages.Should().NotBeNull();
        var finalPrompt = capturedMessages!.Last().Content[0].Text;

        finalPrompt.Should().Contain("adapt the writing style");
        finalPrompt.Should().Contain("idiolect");
        finalPrompt.Should().Contain("demonstrated in the previous messages");
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldNotIncludeIdiolectInstructions_WhenNoUserMessages()
    {
        // Arrange
        var source = "Test message";
        var audience = "engineering";
        var userMessages = Array.Empty<string>();
        var expectedResult = "Result without idiolect";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedResult);

        // Act
        await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        capturedMessages.Should().NotBeNull();
        var promptText = capturedMessages![0].Content[0].Text;

        promptText.Should().NotContain("idiolect");
        promptText.Should().NotContain("previous messages");
        promptText.Should().NotContain("demonstrated communication style");
    }

    [Fact]
    public async Task CorpospeakAsync_ShouldVerifyServiceInteraction()
    {
        // Arrange
        var source = "New feature deployed";
        var audience = "product";
        var userMessages = new[] { "Team update" };
        var expectedResult = "Product-focused announcement";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

        // Assert
        result.Should().Be(expectedResult);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(
            It.Is<IEnumerable<ChatMessage>>(messages =>
                messages.Count() == 3)), Times.Once); // user message + assistant + final prompt
    }
}