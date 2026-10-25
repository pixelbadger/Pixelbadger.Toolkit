using FluentAssertions;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfPromptBuilderTests
{
    private static readonly DateTimeOffset T = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IfTranscriptEvent E(string actor, string kind, string text, string? output = null) =>
        new(T, actor, kind, text, output);

    private static string UserText(IReadOnlyList<IfTranscriptEvent> events, string notes = "", string? instr = null) =>
        IfPromptBuilder.Build(events, notes, instr)[1].Text;

    [Fact]
    public void SystemPrompt_ShouldCoverRules()
    {
        var p = IfPromptBuilder.SystemPrompt(null);
        p.Should().Contain("send_command").And.Contain("thought").And.Contain("save_slot")
            .And.Contain("restore_slot").And.Contain("update_notes").And.Contain("finish(summary)")
            .And.Contain("1-5 tool calls").And.Contain("human");
        p.Should().NotContain("Additional instructions");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SystemPrompt_ShouldNotAppend_WhenInstructionsBlank(string? instr)
    {
        IfPromptBuilder.SystemPrompt(instr).Should().Be(IfPromptBuilder.SystemPrompt(null));
    }

    [Fact]
    public void SystemPrompt_ShouldAppendInstructions()
    {
        IfPromptBuilder.SystemPrompt("Be gothic").Should()
            .EndWith("\n\nAdditional instructions from the user:\nBe gothic");
    }

    [Fact]
    public void Build_ShouldReturnSystemThenUserMessage()
    {
        var msgs = IfPromptBuilder.Build([], "", "X");

        msgs.Should().HaveCount(2);
        msgs[0].Role.Should().Be(ChatRole.System);
        msgs[0].Text.Should().Be(IfPromptBuilder.SystemPrompt("X"));
        msgs[1].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public void Build_ShouldShowPlaceholders_WhenEmpty()
    {
        UserText([]).Should().Be(
            "## Your notes\n(none yet)\n\n## Recent transcript (oldest first)\n(no events yet)\n\nContinue playing.");
    }

    [Fact]
    public void Build_ShouldIncludeNotes()
    {
        var t = UserText([], "Map: north of alley");
        t.Should().StartWith("## Your notes\nMap: north of alley\n\n## Recent transcript");
    }

    [Fact]
    public void Build_ShouldFormatEachEventKind()
    {
        var events = new[]
        {
            E(IfActors.System, IfEventKinds.Command, "(new game)", "Opening."),
            E(IfActors.Model, IfEventKinds.Thought, "So dark."),
            E(IfActors.Model, IfEventKinds.Command, "look", "A room."),
            E(IfActors.Model, IfEventKinds.Save, "s1"),
            E(IfActors.Model, IfEventKinds.Restore, "s1", "Alley."),
            E(IfActors.System, IfEventKinds.Error, "boom"),
            E(IfActors.Model, IfEventKinds.Finish, "done"),
        };

        var t = UserText(events);

        t.Should().Contain(
            "[system] > (new game)\nOpening.\n" +
            "[model thought] So dark.\n" +
            "[model] > look\nA room.\n" +
            "[model save] s1\n" +
            "[model restore] s1\nAlley.\n" +
            "[error] boom\n" +
            "[finish] done\n");
    }

    [Fact]
    public void Build_ShouldOmitOutputLine_WhenOutputNull()
    {
        UserText([E(IfActors.Model, IfEventKinds.Command, "wait")]).Should().Contain("[model] > wait\n\n");
    }

    [Fact]
    public void Build_ShouldLimitToWindowAndReportOmitted()
    {
        var events = Enumerable.Range(0, 55)
            .Select(i => E(IfActors.Model, IfEventKinds.Thought, $"t{i}")).ToList();

        var t = UserText(events);

        t.Should().Contain("(15 earlier events omitted)");
        t.Should().NotContain("t14\n").And.Contain("[model thought] t15\n").And.Contain("[model thought] t54\n");
        t.Split("[model thought]").Length.Should().Be(IfPromptBuilder.EventWindow + 1);
    }

    [Fact]
    public void Build_ShouldNotReportOmitted_WhenExactlyWindow()
    {
        var events = Enumerable.Range(0, 40)
            .Select(i => E(IfActors.Model, IfEventKinds.Thought, $"t{i}")).ToList();

        UserText(events).Should().NotContain("earlier events omitted");
    }

    [Fact]
    public void Build_ShouldOmitHumanSection_WhenNoHumanEventsAfterLastModelEvent()
    {
        var events = new[]
        {
            E(IfActors.Human, IfEventKinds.Note, "old"),
            E(IfActors.Model, IfEventKinds.Command, "look", "x"),
            E(IfActors.System, IfEventKinds.Error, "api down"),
        };

        UserText(events).Should().NotContain("While you were halted");
    }

    [Fact]
    public void Build_ShouldListHumanEventsAfterLastModelEvent()
    {
        var events = new[]
        {
            E(IfActors.Human, IfEventKinds.Note, "before"),
            E(IfActors.Model, IfEventKinds.Command, "look", "x"),
            E(IfActors.Human, IfEventKinds.Command, "take lamp", "Taken."),
            E(IfActors.Human, IfEventKinds.Note, "use the lamp"),
        };

        var t = UserText(events);

        var idx = t.IndexOf("## While you were halted, the human:\n", StringComparison.Ordinal);
        idx.Should().BeGreaterThan(0);
        var section = t[idx..];
        section.Should().Be("## While you were halted, the human:\n[human] > take lamp\nTaken.\n[human note] use the lamp\n\nContinue playing.");
    }

    [Fact]
    public void Build_ShouldListAllHumanEvents_EvenBeyondWindow()
    {
        var events = new List<IfTranscriptEvent> { E(IfActors.Model, IfEventKinds.Thought, "m") };
        for (var i = 0; i < 50; i++) events.Add(E(IfActors.Human, IfEventKinds.Note, $"n{i}"));

        var t = UserText(events);
        var section = t[t.IndexOf("## While you were halted", StringComparison.Ordinal)..];

        for (var i = 0; i < 50; i++) section.Should().Contain($"[human note] n{i}\n");
    }

    [Fact]
    public void Build_ShouldTreatAllHumanEventsAsNew_WhenNoModelEvents()
    {
        var events = new[]
        {
            E(IfActors.System, IfEventKinds.Command, "(new game)", "Hi."),
            E(IfActors.Human, IfEventKinds.Note, "go north"),
        };

        UserText(events).Should().Contain("## While you were halted, the human:\n[human note] go north\n");
    }

    [Fact]
    public void Build_ShouldEndWithContinuePlaying()
    {
        UserText([E(IfActors.Model, IfEventKinds.Thought, "a")]).Should().EndWith("Continue playing.");
    }

    [Fact]
    public void Build_ShouldListHumanEventsFromIndex_WhenHumanSinceIndexGiven()
    {
        var events = new List<IfTranscriptEvent>
        {
            E(IfActors.Human, IfEventKinds.Note, "seen"),
            E(IfActors.Model, IfEventKinds.Command, "look", "A room."),
            E(IfActors.Human, IfEventKinds.Note, "also seen"),
            E(IfActors.Human, IfEventKinds.Note, "fresh"),
        };

        var t = IfPromptBuilder.Build(events, "", null, 3)[1].Text;

        t.Should().Contain("## While you were halted, the human:\n[human note] fresh\n\nContinue playing.");
        t.Should().NotContain("[human note] also seen\n\nContinue");
    }

    [Fact]
    public void Build_ShouldOmitHumanSection_WhenHumanSinceIndexIsPastAllHumanEvents()
    {
        var events = new List<IfTranscriptEvent>
        {
            E(IfActors.Model, IfEventKinds.Command, "look", "A room."),
            E(IfActors.Human, IfEventKinds.Note, "old"),
        };

        IfPromptBuilder.Build(events, "", null, events.Count)[1].Text.Should().NotContain("While you were halted");
    }

    [Fact]
    public void Build_ShouldUseLastModelEventRule_WhenHumanSinceIndexIsNull()
    {
        var events = new List<IfTranscriptEvent>
        {
            E(IfActors.Human, IfEventKinds.Note, "before"),
            E(IfActors.Model, IfEventKinds.Command, "look", "A room."),
            E(IfActors.Human, IfEventKinds.Note, "after"),
        };

        var t = IfPromptBuilder.Build(events, "", null, null)[1].Text;

        t.Should().Contain("## While you were halted, the human:\n[human note] after\n\nContinue playing.");
    }
}
