using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfAgentLoopTests
{
    private readonly Mock<IIfSession> _session = new();
    private readonly Mock<IChatClient> _client = new();
    private readonly Mock<IChatReasoningConfigurator> _reasoning = new();
    private List<ChatMessage>? _sentMessages;
    private ChatOptions? _sentOptions;

    public IfAgentLoopTests()
    {
        _session.Setup(s => s.Events).Returns([]);
        _session.Setup(s => s.ReadNotes()).Returns("");
    }

    private void Respond(params AIContent[] contents) => RespondMessage(new ChatMessage(ChatRole.Assistant, contents));

    private void RespondMessage(ChatMessage message)
    {
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, o, _) => { _sentMessages = m.ToList(); _sentOptions = o; })
            .ReturnsAsync(new ChatResponse(message));
    }

    private IfAgentLoop Loop(string? instr = null, string? effort = null) =>
        new(() => _client.Object, _reasoning.Object, _session.Object, instr, effort);

    private static FunctionCallContent Call(string id, string name, params (string, object?)[] args) =>
        new(id, name, args.ToDictionary(a => a.Item1, a => a.Item2));

    [Fact]
    public async Task StepAsync_ShouldBuildPromptFromSessionState()
    {
        var ev = new IfTranscriptEvent(DateTimeOffset.UnixEpoch, IfActors.Model, IfEventKinds.Thought, "earlier");
        _session.Setup(s => s.Events).Returns([ev]);
        _session.Setup(s => s.ReadNotes()).Returns("my notes");
        Respond(new TextContent("hmm"));

        await Loop("Be brave").StepAsync(default);

        _sentMessages.Should().HaveCount(2);
        _sentMessages![0].Text.Should().Be(IfPromptBuilder.SystemPrompt("Be brave"));
        _sentMessages[1].Text.Should().Contain("my notes").And.Contain("[model thought] earlier");
    }

    [Fact]
    public async Task StepAsync_ShouldOfferToolsWithAutoMode_AndSkipReasoning_WhenEffortNull()
    {
        Respond(new TextContent("x"));

        await Loop().StepAsync(default);

        _sentOptions!.Tools!.Select(t => t.Name).Should().Contain(["send_command", "finish"]);
        _sentOptions.ToolMode.Should().Be(ChatToolMode.Auto);
        _reasoning.Verify(r => r.Apply(It.IsAny<ChatOptions>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StepAsync_ShouldApplyReasoning_WhenEffortGiven()
    {
        Respond(new TextContent("x"));

        await Loop(effort: "high").StepAsync(default);

        _reasoning.Verify(r => r.Apply(It.IsAny<ChatOptions>(), "high"), Times.Once);
    }

    [Fact]
    public async Task StepAsync_ShouldRunToolCallsInOrder_AndCountThem()
    {
        var order = new List<string>();
        _session.Setup(s => s.PlayAsync(IfActors.Model, "look", "t1", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("play")).ReturnsAsync(new IfActionResult(true, "out"));
        _session.Setup(s => s.SaveSlotAsync(IfActors.Model, "a", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(new IfActionResult(true, "saved"));
        _session.Setup(s => s.UpdateNotes("n")).Callback(() => order.Add("notes")).Returns(new IfActionResult(true, "ok"));
        Respond(
            Call("1", "send_command", ("command", "look"), ("thought", "t1")),
            Call("2", "save_slot", ("name", "a")),
            Call("3", "update_notes", ("notes", "n")));

        var r = await Loop().StepAsync(default);

        order.Should().Equal("play", "save", "notes");
        r.Should().Be(new IfStepResult(false, null, 3));
    }

    [Fact]
    public async Task StepAsync_ShouldReturnFinished_AndStillRunRemainingCalls()
    {
        _session.Setup(s => s.UpdateNotes("late")).Returns(new IfActionResult(true, "ok"));
        Respond(
            Call("1", "finish", ("summary", "All done")),
            Call("2", "update_notes", ("notes", "late")));

        var r = await Loop().StepAsync(default);

        r.Should().Be(new IfStepResult(true, "All done", 2));
        _session.Verify(s => s.RecordFinish("All done"), Times.Once);
        _session.Verify(s => s.UpdateNotes("late"), Times.Once);
    }

    [Fact]
    public async Task StepAsync_ShouldContinueAfterToolError()
    {
        _session.Setup(s => s.ListSlots()).Returns(["z"]);
        Respond(Call("1", "bogus"), Call("2", "list_slots"));

        var r = await Loop().StepAsync(default);

        r.Should().Be(new IfStepResult(false, null, 2));
        _session.Verify(s => s.ListSlots(), Times.Once);
    }

    [Fact]
    public async Task StepAsync_ShouldRecordTrimmedThought_WhenNoToolCalls()
    {
        Respond(new TextContent("  I wonder.  "));

        var r = await Loop().StepAsync(default);

        r.Should().Be(new IfStepResult(false, null, 0));
        _session.Verify(s => s.RecordThought("I wonder."), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task StepAsync_ShouldNotRecordThought_WhenTextBlank(string text)
    {
        Respond(new TextContent(text));

        var r = await Loop().StepAsync(default);

        r.ToolCalls.Should().Be(0);
        _session.Verify(s => s.RecordThought(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StepAsync_ShouldCallModelOnlyOnce()
    {
        Respond(Call("1", "list_slots"));
        _session.Setup(s => s.ListSlots()).Returns([]);

        await Loop().StepAsync(default);

        _client.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StepAsync_ShouldPropagateFactoryException()
    {
        var loop = new IfAgentLoop(() => throw new InvalidOperationException("no key"), _reasoning.Object, _session.Object, null, null);

        var act = () => loop.StepAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no key");
    }

    [Fact]
    public async Task StepAsync_ShouldPropagateModelException()
    {
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401"));

        var act = () => Loop().StepAsync(default);

        await act.Should().ThrowAsync<HttpRequestException>();
        _session.Verify(s => s.RecordThought(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StepAsync_ShouldPassCancellationToModel_AndPropagateCancel()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(async (_, _, ct) =>
            {
                seen = ct;
                await Task.Delay(Timeout.Infinite, ct);
                return new ChatResponse();
            });

        var task = Loop().StepAsync(cts.Token);
        cts.Cancel();

        await task.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task StepAsync_ShouldRunToolsToCompletion_EvenIfModelTokenCancelledLater()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        _session.Setup(s => s.PlayAsync(IfActors.Model, "look", "t", It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, _, _, ct) => { seen = ct; cts.Cancel(); })
            .ReturnsAsync(new IfActionResult(true, "out"));
        Respond(Call("1", "send_command", ("command", "look"), ("thought", "t")));

        var r = await Loop().StepAsync(cts.Token);

        r.ToolCalls.Should().Be(1);
        seen.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task StepAsync_ShouldFindCallsInAnyResponseMessage()
    {
        _session.Setup(s => s.ListSlots()).Returns([]);
        var response = new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [new TextContent("hi")]),
            new ChatMessage(ChatRole.Assistant, [Call("1", "list_slots")]),
        ]);
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var r = await Loop().StepAsync(default);

        r.ToolCalls.Should().Be(1);
        _session.Verify(s => s.RecordThought(It.IsAny<string>()), Times.Never);
    }

    private string LastUserText() => _sentMessages![1].Text;

    [Fact]
    public async Task StepAsync_ShouldShowListSlotsAndErrorResultsInNextStep()
    {
        _session.Setup(s => s.ListSlots()).Returns(["a", "b"]);
        _session.Setup(s => s.PlayAsync(IfActors.Model, "save", "t", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(false, "Use the save/restore slot tools instead of in-game save, restore, quit or restart."));
        Respond(Call("1", "list_slots"), Call("2", "send_command", ("command", "save"), ("thought", "t")));
        var loop = Loop();
        await loop.StepAsync(default);

        Respond(new TextContent("x"));
        await loop.StepAsync(default);

        LastUserText().Should().EndWith(
            "## Results of your last tool calls not shown in the transcript:\n" +
            "list_slots → Slots: a, b\n" +
            "send_command: save → Error: Use the save/restore slot tools instead of in-game save, restore, quit or restart.\n\n" +
            "Continue playing.");
        LastUserText().Split("Continue playing.").Length.Should().Be(2);
    }

    [Fact]
    public async Task StepAsync_ShouldNotShowSuccessfulSendCommandResult()
    {
        _session.Setup(s => s.PlayAsync(IfActors.Model, "look", "t", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(true, "A room."));
        Respond(Call("1", "send_command", ("command", "look"), ("thought", "t")));
        var loop = Loop();
        await loop.StepAsync(default);

        Respond(new TextContent("x"));
        await loop.StepAsync(default);

        LastUserText().Should().NotContain("Results of your last tool calls").And.EndWith("Continue playing.");
    }

    [Fact]
    public async Task StepAsync_ShouldUseFeedbackOnlyOnce()
    {
        _session.Setup(s => s.ListSlots()).Returns([]);
        Respond(Call("1", "list_slots"));
        var loop = Loop();
        await loop.StepAsync(default);
        Respond(new TextContent("x"));
        await loop.StepAsync(default);
        LastUserText().Should().Contain("list_slots → No slots saved.");

        await loop.StepAsync(default);

        LastUserText().Should().NotContain("Results of your last tool calls");
    }

    [Fact]
    public async Task StepAsync_ShouldKeepFeedback_WhenModelCallThrows()
    {
        _session.Setup(s => s.ListSlots()).Returns(["a"]);
        Respond(Call("1", "list_slots"));
        var loop = Loop();
        await loop.StepAsync(default);
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429"));
        await loop.Invoking(l => l.StepAsync(default)).Should().ThrowAsync<HttpRequestException>();

        Respond(new TextContent("x"));
        await loop.StepAsync(default);

        LastUserText().Should().Contain("list_slots → Slots: a");
    }

    [Fact]
    public async Task StepAsync_ShouldReplaceFeedback_WithLatestStepOnly()
    {
        _session.Setup(s => s.ListSlots()).Returns(["a"]);
        Respond(Call("1", "list_slots"));
        var loop = Loop();
        await loop.StepAsync(default);
        Respond(Call("2", "bogus"));
        await loop.StepAsync(default);
        LastUserText().Should().Contain("list_slots → Slots: a");

        Respond(new TextContent("x"));
        await loop.StepAsync(default);

        LastUserText().Should().Contain("bogus → error: Unknown tool 'bogus'.").And.NotContain("Slots: a");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDependenciesNull()
    {
        var f = () => _client.Object;
        ((Action)(() => new IfAgentLoop(null!, _reasoning.Object, _session.Object, null, null))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IfAgentLoop(f, null!, _session.Object, null, null))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IfAgentLoop(f, _reasoning.Object, null!, null, null))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task StepAsync_ShouldListHumanEventsOnlyOnce_WhenStepsAppendNoModelEvent()
    {
        var events = new List<IfTranscriptEvent>
        {
            new(DateTimeOffset.UnixEpoch, IfActors.Model, IfEventKinds.Command, "look", "A room."),
        };
        _session.Setup(s => s.Events).Returns(events);
        _session.Setup(s => s.UpdateNotes(It.IsAny<string>())).Returns(new IfActionResult(true, "ok"));
        var prompts = new List<string>();
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => prompts.Add(m.ToList()[1].Text))
            .ReturnsAsync(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c", "update_notes", ("notes", "n"))])));
        var loop = Loop();

        await loop.StepAsync(default);
        events.Add(new(DateTimeOffset.UnixEpoch, IfActors.Human, IfEventKinds.Note, "try the bin"));
        await loop.StepAsync(default);
        await loop.StepAsync(default);
        events.Add(new(DateTimeOffset.UnixEpoch, IfActors.Human, IfEventKinds.Command, "open bin", "Opened."));
        await loop.StepAsync(default);

        prompts[0].Should().NotContain("While you were halted");
        prompts[1].Should().Contain("## While you were halted, the human:\n[human note] try the bin\n");
        prompts[2].Should().NotContain("While you were halted");
        prompts[3].Should().Contain("## While you were halted, the human:\n[human] > open bin\nOpened.\n")
            .And.NotContain("[human note] try the bin\n\nContinue");
    }

    [Fact]
    public async Task StepAsync_ShouldDiscardResponseAndRunNoTools_WhenCancelledWhileModelResponds()
    {
        using var cts = new CancellationTokenSource();
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c", "send_command", ("command", "look"), ("thought", "t"))])));

        var act = () => Loop().StepAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _session.Verify(s => s.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
