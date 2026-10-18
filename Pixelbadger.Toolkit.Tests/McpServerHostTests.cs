using System.Text.Json;
using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class McpServerHostTests
{
    private readonly Mock<IMcpToolExecutor> _executor = new();
    private readonly StringWriter _log = new();
    private readonly McpServerHost _host;
    private readonly McpToolDefinition _tool = new("t_x", "desc", ["t", "x"], JsonDocument.Parse("{}").RootElement.Clone(), []);

    public McpServerHostTests() => _host = new McpServerHost(_executor.Object, _log);

    private IReadOnlyDictionary<string, McpToolDefinition> Tools => new Dictionary<string, McpToolDefinition> { [_tool.Name] = _tool };

    [Fact]
    public async Task HandleCallAsync_ShouldLogExitOneAndReturnError_WhenToolIsUnknown()
    {
        var result = await _host.HandleCallAsync(Tools, "nope", null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        _log.ToString().Should().MatchRegex(@"^tool nope exit 1 \d+ms\r?\n$");
        _executor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleCallAsync_ShouldLogExecutorExitCode_WhenToolRuns()
    {
        _executor.Setup(e => e.ExecuteAsync(_tool, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult("x\nexit code: 3", true, 3));

        await _host.HandleCallAsync(Tools, "t_x", null, CancellationToken.None);

        _log.ToString().Should().MatchRegex(@"^tool t_x exit 3 \d+ms\r?\n$");
    }

    [Fact]
    public async Task HandleCallAsync_ShouldLogExitOneAndRethrow_WhenClientCancels()
    {
        using var cts = new CancellationTokenSource();
        _executor.Setup(e => e.ExecuteAsync(_tool, null, It.IsAny<CancellationToken>()))
            .Returns(async (McpToolDefinition _, IReadOnlyDictionary<string, JsonElement>? _, CancellationToken ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return new McpToolResult("", false, 0);
            });

        var act = () => _host.HandleCallAsync(Tools, "t_x", null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _log.ToString().Should().MatchRegex(@"^tool t_x exit 1 \d+ms\r?\n$");
    }
}
