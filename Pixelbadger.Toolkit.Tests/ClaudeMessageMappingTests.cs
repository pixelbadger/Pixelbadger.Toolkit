using Anthropic.Models.Messages;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Services;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Pixelbadger.Toolkit.Tests;

public class ClaudeMessageMappingTests
{
    private static readonly byte[] Bytes = [1, 2, 3, 250];

    [Fact]
    public void MapMessages_ShouldExtractSingleSystemMessage_WhenPresent()
    {
        var (system, messages) = ClaudeLlmClientService.MapMessages(
            [new AiChatMessage(ChatRole.System, "sys"), new AiChatMessage(ChatRole.User, "hi")]);

        system.Should().Be("sys");
        messages.Should().ContainSingle();
    }

    [Fact]
    public void MapMessages_ShouldJoinMultipleSystemMessagesInOrder_WhenSeveralPresent()
    {
        var (system, messages) = ClaudeLlmClientService.MapMessages(
        [
            new AiChatMessage(ChatRole.System, "one"),
            new AiChatMessage(ChatRole.System, "two"),
            new AiChatMessage(ChatRole.User, "hi")
        ]);

        system.Should().Be("one\n\ntwo");
        messages.Should().ContainSingle();
    }

    [Fact]
    public void MapMessages_ShouldExtractMidConversationSystemMessages_WhenInterleaved()
    {
        var (system, messages) = ClaudeLlmClientService.MapMessages(
        [
            new AiChatMessage(ChatRole.System, "first"),
            new AiChatMessage(ChatRole.User, "u1"),
            new AiChatMessage(ChatRole.Assistant, "a1"),
            new AiChatMessage(ChatRole.System, "second"),
            new AiChatMessage(ChatRole.User, "u2")
        ]);

        system.Should().Be("first\n\nsecond");
        messages.Select(m => m.Role.Raw()).Should().Equal("user", "assistant", "user");
    }

    [Fact]
    public void MapMessages_ShouldReturnNullSystem_WhenNoSystemMessages()
    {
        var (system, _) = ClaudeLlmClientService.MapMessages([new AiChatMessage(ChatRole.User, "hi")]);

        system.Should().BeNull();
    }

    [Fact]
    public void MapMessages_ShouldMapRolesAndTextBlocks()
    {
        var (_, messages) = ClaudeLlmClientService.MapMessages(
            [new AiChatMessage(ChatRole.User, "usr"), new AiChatMessage(ChatRole.Assistant, "asst")]);

        messages.Should().HaveCount(2);
        messages[0].Role.Value().Should().Be(Role.User);
        messages[1].Role.Value().Should().Be(Role.Assistant);
        Blocks(messages[0]).Should().ContainSingle();
        Blocks(messages[0])[0].TryPickText(out var text0);
        text0!.Text.Should().Be("usr");
        Blocks(messages[1])[0].TryPickText(out var text1);
        text1!.Text.Should().Be("asst");
    }

    [Fact]
    public void MapMessages_ShouldNotFixUpLeadingAssistantTurn()
    {
        var (_, messages) = ClaudeLlmClientService.MapMessages([new AiChatMessage(ChatRole.Assistant, "first")]);

        messages.Should().ContainSingle().Which.Role.Value().Should().Be(Role.Assistant);
    }

    [Theory]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("image/png", "image/png")]
    [InlineData("image/gif", "image/gif")]
    [InlineData("image/webp", "image/webp")]
    [InlineData("IMAGE/PNG", "image/png")]
    public void MapMessages_ShouldMapImageToBase64Block_WhenSupportedMediaType(string mediaType, string expected)
    {
        var (_, messages) = ClaudeLlmClientService.MapMessages(
            [new AiChatMessage(ChatRole.User, [new DataContent(Bytes, mediaType)])]);

        var block = Blocks(messages[0]).Should().ContainSingle().Subject;
        block.TryPickImage(out var image).Should().BeTrue();
        image!.Source.TryPickBase64Image(out var source).Should().BeTrue();
        source!.Data.Should().Be(Convert.ToBase64String(Bytes));
        source.MediaType.Raw().Should().Be(expected);
    }

    [Fact]
    public void MapMessages_ShouldPreserveOrder_WhenTextAndImageMixed()
    {
        var (_, messages) = ClaudeLlmClientService.MapMessages(
        [
            new AiChatMessage(ChatRole.User,
            [
                new TextContent("before"),
                new DataContent(Bytes, "image/png"),
                new TextContent("after")
            ])
        ]);

        var blocks = Blocks(messages[0]);
        blocks.Should().HaveCount(3);
        blocks[0].TryPickText(out var a).Should().BeTrue();
        a!.Text.Should().Be("before");
        blocks[1].TryPickImage(out _).Should().BeTrue();
        blocks[2].TryPickText(out var c).Should().BeTrue();
        c!.Text.Should().Be("after");
    }

    [Theory]
    [InlineData("image/bmp")]
    [InlineData("image/svg+xml")]
    [InlineData("application/pdf")]
    [InlineData("audio/mpeg")]
    public void MapMessages_ShouldThrowNotSupported_WhenMediaTypeUnsupported(string mediaType)
    {
        var act = () => ClaudeLlmClientService.MapMessages(
            [new AiChatMessage(ChatRole.User, [new DataContent(Bytes, mediaType)])]);

        act.Should().Throw<NotSupportedException>().WithMessage($"*'{mediaType}'*Claude*");
    }

    [Fact]
    public void MapMessages_ShouldThrowNotSupported_WhenContentTypeUnsupported()
    {
        var act = () => ClaudeLlmClientService.MapMessages(
            [new AiChatMessage(ChatRole.User, [new FunctionCallContent("id", "fn")])]);

        act.Should().Throw<NotSupportedException>().WithMessage("*FunctionCallContent*Claude*");
    }

    [Fact]
    public void MapMessages_ShouldThrowNotSupported_WhenRoleUnsupported()
    {
        var act = () => ClaudeLlmClientService.MapMessages([new AiChatMessage(ChatRole.Tool, "x")]);

        act.Should().Throw<NotSupportedException>().WithMessage("*'tool'*Claude*");
    }

    [Fact]
    public void MapMessages_ShouldThrowNotSupported_WhenSystemMessageHasNonText()
    {
        var act = () => ClaudeLlmClientService.MapMessages(
            [new AiChatMessage(ChatRole.System, [new DataContent(Bytes, "image/png")])]);

        act.Should().Throw<NotSupportedException>().WithMessage("*System*Claude*");
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("max", "max")]
    [InlineData("XHIGH", "xhigh")]
    public void MapEffort_ShouldMapToSdkEnum(string input, string expectedRaw)
    {
        ClaudeLlmClientService.MapEffort(input)!.Value.ToString().Should().BeEquivalentTo(expectedRaw);
    }

    [Fact]
    public void MapEffort_ShouldReturnNull_WhenNull()
    {
        ClaudeLlmClientService.MapEffort(null).Should().BeNull();
    }

    [Fact]
    public void MapEffort_ShouldThrow_WhenUnknown()
    {
        var act = () => ClaudeLlmClientService.MapEffort("ultra");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildRequest_ShouldSetModelMaxTokensEffortAndNoThinking()
    {
        var request = ClaudeLlmClientService.BuildRequest("m", [new AiChatMessage(ChatRole.System, "s"), new AiChatMessage(ChatRole.User, "u")], "max");

        request.Model.Raw().Should().Be("m");
        request.MaxTokens.Should().Be(16000);
        request.Thinking.Should().BeNull();
        request.OutputConfig!.Effort!.Value().ToString().Should().BeEquivalentTo("max");
        request.System.Should().NotBeNull();
        request.Messages.Should().ContainSingle();
    }

    [Fact]
    public void BuildRequest_ShouldOmitEffortAndSystem_WhenNotProvided()
    {
        var request = ClaudeLlmClientService.BuildRequest("m", [new AiChatMessage(ChatRole.User, "u")], null);

        request.OutputConfig.Should().BeNull();
        request.System.Should().BeNull();
        request.Thinking.Should().BeNull();
    }

    private static IReadOnlyList<ContentBlockParam> Blocks(MessageParam message)
    {
        message.Content.TryPickContentBlockParams(out var blocks).Should().BeTrue();
        return blocks!;
    }
}
