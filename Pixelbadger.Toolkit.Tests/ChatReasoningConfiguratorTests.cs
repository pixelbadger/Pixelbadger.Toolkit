using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class ChatReasoningConfiguratorTests
{
    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public List<JsonDocument> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private const string OpenAiResponse = """
        {"id":"resp_1","object":"response","created_at":1,"status":"completed","model":"gpt-6.1-sol",
         "output":[{"type":"message","id":"msg_1","status":"completed","role":"assistant",
         "content":[{"type":"output_text","text":"ok","annotations":[]}]}],
         "parallel_tool_calls":true,"tool_choice":"auto","tools":[]}
        """;

    private const string ClaudeResponse = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-5",
         "content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":1,"output_tokens":1}}
        """;

    private static IChatClient OpenAi(CapturingHandler handler)
    {
#pragma warning disable OPENAI001
        return new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        }).GetResponsesClient().AsIChatClient("gpt-6.1-sol");
#pragma warning restore OPENAI001
    }

    private static IChatClient Claude(CapturingHandler handler)
        => new AnthropicClient { ApiKey = "test", HttpClient = new HttpClient(handler) }
            .AsIChatClient("claude-sonnet-5-5", 16000);

    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "hello")];

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("HIGH")]
    public async Task OpenAiConfigurator_ShouldSendReasoningEffortOnTheWire_WhenApplied(string effort)
    {
        var handler = new CapturingHandler(OpenAiResponse);
        var options = new ChatOptions();
        new OpenAiChatReasoningConfigurator().Apply(options, effort);

        await OpenAi(handler).GetResponseAsync(Messages, options);

        var root = handler.Requests.Single().RootElement;
        root.GetProperty("reasoning").GetProperty("effort").GetString().Should().Be(effort.ToLowerInvariant());
        root.GetProperty("model").GetString().Should().Be("gpt-6.1-sol");
    }

    [Fact]
    public async Task OpenAi_ShouldSendNoReasoningEffort_WhenConfiguratorNotApplied()
    {
        var handler = new CapturingHandler(OpenAiResponse);

        await OpenAi(handler).GetResponseAsync(Messages, new ChatOptions());

        var root = handler.Requests.Single().RootElement;
        var hasEffort = root.TryGetProperty("reasoning", out var reasoning)
            && reasoning.ValueKind == JsonValueKind.Object && reasoning.TryGetProperty("effort", out _);
        hasEffort.Should().BeFalse();
    }

    [Fact]
    public void OpenAiConfigurator_ShouldThrow_WhenEffortUnsupported()
    {
        var act = () => new OpenAiChatReasoningConfigurator().Apply(new ChatOptions(), "xhigh");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public async Task ClaudeConfigurator_ShouldSendExactNativeEffortOnTheWire_WhenApplied(string effort)
    {
        var handler = new CapturingHandler(ClaudeResponse);
        var options = new ChatOptions();
        new ClaudeChatReasoningConfigurator("claude-sonnet-5-5").Apply(options, effort);

        await Claude(handler).GetResponseAsync(Messages, options);

        var root = handler.Requests.Single().RootElement;
        root.GetProperty("output_config").GetProperty("effort").GetString().Should().Be(effort);
        root.GetProperty("model").GetString().Should().Be("claude-sonnet-5-5");
        root.GetProperty("max_tokens").GetInt32().Should().Be(16000);
        var message = root.GetProperty("messages").EnumerateArray().Single();
        message.GetProperty("role").GetString().Should().Be("user");
        message.GetProperty("content").ToString().Should().Contain("hello");
        root.TryGetProperty("thinking", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ClaudeConfigurator_ShouldKeepToolsAndSendEffort_WhenToolsPresent()
    {
        var handler = new CapturingHandler(ClaudeResponse);
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "x", "do_thing", "does a thing")]
        };
        new ClaudeChatReasoningConfigurator().Apply(options, "max");

        await Claude(handler).GetResponseAsync(Messages, options);

        var root = handler.Requests.Single().RootElement;
        root.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("max");
        root.GetProperty("tools").EnumerateArray().Single().GetProperty("name").GetString().Should().Be("do_thing");
    }

    [Fact]
    public async Task Claude_ShouldSendNoOutputConfigEffort_WhenConfiguratorNotApplied()
    {
        var handler = new CapturingHandler(ClaudeResponse);

        await Claude(handler).GetResponseAsync(Messages, new ChatOptions());

        var root = handler.Requests.Single().RootElement;
        var hasEffort = root.TryGetProperty("output_config", out var config)
            && config.ValueKind == JsonValueKind.Object && config.TryGetProperty("effort", out _);
        hasEffort.Should().BeFalse();
    }

    [Fact]
    public void ClaudeConfigurator_ShouldThrow_WhenEffortUnsupported()
    {
        var act = () => new ClaudeChatReasoningConfigurator().Apply(new ChatOptions(), "ultra");

        act.Should().Throw<ArgumentException>();
    }
}
