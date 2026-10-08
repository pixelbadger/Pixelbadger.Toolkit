using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Services;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Pixelbadger.Toolkit.Tests;

public class OpenAiMessageMappingTests
{
    [Fact]
    public void MapMessages_ShouldMapRolesToOpenAiMessageTypes()
    {
        var mapped = OpenAiLlmClientService.MapMessages(
        [
            new AiChatMessage(ChatRole.System, "sys"),
            new AiChatMessage(ChatRole.User, "usr"),
            new AiChatMessage(ChatRole.Assistant, "asst")
        ]);

        mapped.Should().HaveCount(3);
        mapped[0].Should().BeOfType<SystemChatMessage>();
        mapped[1].Should().BeOfType<UserChatMessage>();
        mapped[2].Should().BeOfType<AssistantChatMessage>();
        mapped[0].Content.Should().ContainSingle().Which.Text.Should().Be("sys");
        mapped[1].Content.Should().ContainSingle().Which.Text.Should().Be("usr");
        mapped[2].Content.Should().ContainSingle().Which.Text.Should().Be("asst");
    }

    [Fact]
    public void MapMessages_ShouldMapImageDataContentToImagePart()
    {
        byte[] bytes = [1, 2, 3, 4];
        var message = new AiChatMessage(ChatRole.User, [new DataContent(bytes, "image/png")]);

        var mapped = OpenAiLlmClientService.MapMessages([message]);

        mapped.Should().ContainSingle().Which.Should().BeOfType<UserChatMessage>();
        var part = mapped[0].Content.Should().ContainSingle().Subject;
        part.Kind.Should().Be(ChatMessageContentPartKind.Image);
        part.ImageBytesMediaType.Should().Be("image/png");
        part.ImageBytes.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public void MapMessages_ShouldMapMixedTextAndImageParts_InOrder()
    {
        var message = new AiChatMessage(ChatRole.User,
        [
            new TextContent("look"),
            new DataContent(new byte[] { 9 }, "image/jpeg")
        ]);

        var mapped = OpenAiLlmClientService.MapMessages([message]);

        mapped[0].Content.Should().HaveCount(2);
        mapped[0].Content[0].Kind.Should().Be(ChatMessageContentPartKind.Text);
        mapped[0].Content[0].Text.Should().Be("look");
        mapped[0].Content[1].Kind.Should().Be(ChatMessageContentPartKind.Image);
    }

    [Fact]
    public void MapMessages_ShouldThrowNotSupported_WhenRoleUnsupported()
    {
        var act = () => OpenAiLlmClientService.MapMessages([new AiChatMessage(ChatRole.Tool, "x")]);

        act.Should().Throw<NotSupportedException>().WithMessage("*'tool'*");
    }

    [Fact]
    public void MapMessages_ShouldThrowNotSupported_WhenDataContentIsNotImage()
    {
        var message = new AiChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "application/pdf")]);

        var act = () => OpenAiLlmClientService.MapMessages([message]);

        act.Should().Throw<NotSupportedException>().WithMessage("*application/pdf*");
    }

    [Fact]
    public void MapMessages_ShouldThrowNotSupported_WhenContentTypeUnsupported()
    {
        var message = new AiChatMessage(ChatRole.User, [new FunctionCallContent("id", "fn")]);

        var act = () => OpenAiLlmClientService.MapMessages([message]);

        act.Should().Throw<NotSupportedException>().WithMessage("*FunctionCallContent*");
    }
}
