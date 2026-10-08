using System.Text.Json;
using Anthropic.Models.Messages;
using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class ClaudeResponseExtractionTests
{
    private static Message Response(string contentJson, string stopReason = "end_turn", long input = 11, long output = 22, string? stopDetails = null)
    {
        var json = $$"""
        {
          "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-sonnet-5-5",
          "content": {{contentJson}},
          "stop_reason": "{{stopReason}}", "stop_sequence": null
          {{(stopDetails is null ? "" : $", \"stop_details\": {stopDetails}")}},
          "usage": { "input_tokens": {{input}}, "output_tokens": {{output}} }
        }
        """;
        return JsonSerializer.Deserialize<Message>(json)!;
    }

    [Fact]
    public void ExtractResult_ShouldConcatenateTextBlocksInOrder()
    {
        var result = ClaudeLlmClientService.ExtractResult(Response(
            """[{"type":"text","text":"Hello, "},{"type":"text","text":"world"}]"""));

        result.Content.Should().Be("Hello, world");
    }

    [Fact]
    public void ExtractResult_ShouldIgnoreThinkingBlocks()
    {
        var result = ClaudeLlmClientService.ExtractResult(Response(
            """[{"type":"thinking","thinking":"hmm","signature":"sig"},{"type":"text","text":"answer"}]"""));

        result.Content.Should().Be("answer");
    }

    [Fact]
    public void ExtractResult_ShouldMapUsage()
    {
        var result = ClaudeLlmClientService.ExtractResult(Response("""[{"type":"text","text":"x"}]""", input: 123, output: 456));

        result.PromptTokens.Should().Be(123);
        result.CompletionTokens.Should().Be(456);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"type":"thinking","thinking":"hmm","signature":"sig"}]""")]
    public void ExtractResult_ShouldThrow_WhenNoTextBlocks(string content)
    {
        var act = () => ClaudeLlmClientService.ExtractResult(Response(content));

        act.Should().Throw<InvalidOperationException>().WithMessage("LLM returned an empty response.");
    }

    [Fact]
    public void ExtractResult_ShouldThrowWithCategory_WhenRefusal()
    {
        var act = () => ClaudeLlmClientService.ExtractResult(Response(
            "[]", "refusal", stopDetails: """{"type":"refusal","category":"cyber","explanation":"no"}"""));

        act.Should().Throw<InvalidOperationException>().WithMessage("Claude declined the request*cyber*");
    }

    [Fact]
    public void ExtractResult_ShouldThrowDeclined_WhenRefusalWithoutDetails()
    {
        var act = () => ClaudeLlmClientService.ExtractResult(Response("""[{"type":"text","text":"partial"}]""", "refusal"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Claude declined the request*");
    }
}
