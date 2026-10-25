using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfToolsTests
{
    private readonly Mock<IIfSession> _session = new(MockBehavior.Strict);
    private readonly IfTools _tools;

    public IfToolsTests() => _tools = new IfTools(_session.Object);

    private static FunctionCallContent Call(string name, params (string Key, object? Value)[] args) =>
        new("c1", name, args.ToDictionary(a => a.Key, a => a.Value));

    private static string Text(IfToolOutcome o) => (string)o.Result.Result!;

    [Fact]
    public void Tools_ShouldDeclareSixToolsWithRequiredArguments()
    {
        var names = _tools.Tools.Select(t => t.Name).ToList();
        names.Should().Equal("send_command", "save_slot", "restore_slot", "list_slots", "update_notes", "finish");
        var send = (AIFunctionDeclaration)_tools.Tools[0];
        send.JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("command", "thought");
    }

    [Fact]
    public async Task InvokeAsync_ShouldPlayAsModel_WhenSendCommand()
    {
        _session.Setup(s => s.PlayAsync(IfActors.Model, "open door", "Hm.", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(true, "The door opens."));

        var o = await _tools.InvokeAsync(Call("send_command", ("command", "open door"), ("thought", "Hm.")), default);

        Text(o).Should().Be("The door opens.");
        o.Description.Should().Be("send_command: open door");
        o.Finished.Should().BeFalse();
        o.Result.CallId.Should().Be("c1");
    }

    [Fact]
    public async Task InvokeAsync_ShouldAcceptJsonElementArguments()
    {
        _session.Setup(s => s.PlayAsync(IfActors.Model, "look", "Dark.", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(true, "ok"));
        var call = Call("send_command",
            ("command", JsonDocument.Parse("\"look\"").RootElement),
            ("thought", JsonDocument.Parse("\"Dark.\"").RootElement));

        var o = await _tools.InvokeAsync(call, default);

        Text(o).Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_ShouldPrefixError_WhenSessionFails()
    {
        _session.Setup(s => s.PlayAsync(IfActors.Model, "quit", "x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(false, "Use the save/restore slot tools."));

        var o = await _tools.InvokeAsync(Call("send_command", ("command", "quit"), ("thought", "x")), default);

        Text(o).Should().Be("Error: Use the save/restore slot tools.");
        o.Finished.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSaveSlotAsModel()
    {
        _session.Setup(s => s.SaveSlotAsync(IfActors.Model, "alley", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(true, "Saved slot 'alley'."));

        var o = await _tools.InvokeAsync(Call("save_slot", ("name", "alley")), default);

        Text(o).Should().Be("Saved slot 'alley'.");
        o.Description.Should().Be("save_slot: alley");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReportSaveSlotError()
    {
        _session.Setup(s => s.SaveSlotAsync(IfActors.Model, "bad name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(false, "Invalid slot name."));

        var o = await _tools.InvokeAsync(Call("save_slot", ("name", "bad name")), default);

        Text(o).Should().Be("Error: Invalid slot name.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRestoreSlotAsModel()
    {
        _session.Setup(s => s.RestoreSlotAsync(IfActors.Model, "alley", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(true, "Alley\nDamp."));

        var o = await _tools.InvokeAsync(Call("restore_slot", ("name", "alley")), default);

        Text(o).Should().Be("Alley\nDamp.");
        o.Description.Should().Be("restore_slot: alley");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReportRestoreSlotError()
    {
        _session.Setup(s => s.RestoreSlotAsync(IfActors.Model, "nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IfActionResult(false, "No such slot."));

        var o = await _tools.InvokeAsync(Call("restore_slot", ("name", "nope")), default);

        Text(o).Should().Be("Error: No such slot.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldListSlots()
    {
        _session.Setup(s => s.ListSlots()).Returns(["a", "b"]);

        var o = await _tools.InvokeAsync(Call("list_slots"), default);

        Text(o).Should().Be("Slots: a, b");
        o.Description.Should().Be("list_slots");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReportNoSlots_WhenEmpty()
    {
        _session.Setup(s => s.ListSlots()).Returns([]);

        var o = await _tools.InvokeAsync(Call("list_slots"), default);

        Text(o).Should().Be("No slots saved.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldUpdateNotes()
    {
        _session.Setup(s => s.UpdateNotes("map")).Returns(new IfActionResult(true, "Notes updated."));

        var o = await _tools.InvokeAsync(Call("update_notes", ("notes", "map")), default);

        Text(o).Should().Be("Notes updated.");
        o.Description.Should().Be("update_notes");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReportNotesError()
    {
        _session.Setup(s => s.UpdateNotes("big")).Returns(new IfActionResult(false, "Notes too long."));

        var o = await _tools.InvokeAsync(Call("update_notes", ("notes", "big")), default);

        Text(o).Should().Be("Error: Notes too long.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRecordFinishAndFlagOutcome()
    {
        _session.Setup(s => s.RecordFinish("Won!"));

        var o = await _tools.InvokeAsync(Call("finish", ("summary", "Won!")), default);

        o.Finished.Should().BeTrue();
        o.Summary.Should().Be("Won!");
        o.Description.Should().Be("finish: Won!");
        _session.Verify(s => s.RecordFinish("Won!"), Times.Once);
    }

    [Theory]
    [InlineData("send_command", "command", "look")]
    [InlineData("save_slot", null, null)]
    [InlineData("restore_slot", null, null)]
    [InlineData("update_notes", null, null)]
    [InlineData("finish", null, null)]
    public async Task InvokeAsync_ShouldReturnErrorWithoutTouchingSession_WhenArgumentMissing(string tool, string? key, string? value)
    {
        var call = key is null ? Call(tool) : Call(tool, (key, value));

        var o = await _tools.InvokeAsync(call, default);

        Text(o).Should().StartWith("Error: ");
        o.Finished.Should().BeFalse();
        _session.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenArgumentNotAString()
    {
        var o = await _tools.InvokeAsync(Call("save_slot", ("name", 42)), default);

        Text(o).Should().Be("Error: Argument 'name' must be a string.");
        _session.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenArgumentIsJsonNull()
    {
        var call = Call("finish", ("summary", JsonDocument.Parse("null").RootElement));

        var o = await _tools.InvokeAsync(call, default);

        Text(o).Should().Be("Error: Missing argument 'summary'.");
        _session.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenArgumentsNull()
    {
        var o = await _tools.InvokeAsync(new FunctionCallContent("c1", "save_slot", null), default);

        Text(o).Should().StartWith("Error: ");
        _session.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenToolUnknown()
    {
        var o = await _tools.InvokeAsync(Call("dance"), default);

        Text(o).Should().Be("Error: Unknown tool 'dance'.");
        _session.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ShouldThrow_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _tools.InvokeAsync(Call("list_slots"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSessionNull()
    {
        var act = () => new IfTools(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
