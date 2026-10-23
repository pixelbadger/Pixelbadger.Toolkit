using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class SqliteTuiComponentTests
{
    private const int W = 120;
    private const int H = 40;

    private readonly Mock<ISqliteSession> _session = new();
    private bool _dirty;
    private IReadOnlyList<SchemaObject> _schema;

    public SqliteTuiComponentTests()
    {
        _schema =
        [
            new SchemaObject("users", SchemaObjectKind.Table,
            [
                new SchemaColumn("id", "INTEGER", true, false),
                new SchemaColumn("name", "TEXT", false, true),
                new SchemaColumn("misc", "", false, false)
            ]),
            new SchemaObject("orders", SchemaObjectKind.Table, [new SchemaColumn("oid", "INTEGER", true, true)]),
            new SchemaObject("v_active", SchemaObjectKind.View, [new SchemaColumn("name", "TEXT", false, false)])
        ];
        _session.SetupGet(s => s.DatabasePath).Returns("/tmp/data/shop.db");
        _session.SetupGet(s => s.IsReadOnly).Returns(false);
        _session.SetupGet(s => s.HasUnsavedChanges).Returns(() => _dirty);
        _session.Setup(s => s.GetSchema()).Returns(() => _schema);
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(["a"], [[1L]]));
        _session.Setup(s => s.Save()).Returns(new SessionActionResult(true, "Changes saved"));
        _session.Setup(s => s.Discard()).Returns(new SessionActionResult(true, "Changes discarded"));
    }

    // ---------------------------------------------------------------- helpers

    private SqliteTuiComponent Create() => new(_session.Object);

    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0', bool shift = false, bool ctrl = false) =>
        new(ch, key, shift, false, ctrl);

    private static ConsoleKeyInfo Chr(char c) => new(c, ConsoleKey.A, false, false, false);

    private static ConsoleKeyInfo CtrlS => Key(ConsoleKey.S, ctrl: true);

    private static ConsoleKeyInfo CtrlQ => Key(ConsoleKey.Q, ctrl: true);

    private static void Type(SqliteTuiComponent c, string text)
    {
        foreach (var ch in text) c.HandleKey(Chr(ch));
    }

    private static QueryResult Rows(string[] columns, object?[][] rows, bool truncated = false, double ms = 12) =>
        new(QueryOutcome.ResultSet, columns, rows.Select(r => (IReadOnlyList<object?>)r).ToList(), truncated, 0,
            TimeSpan.FromMilliseconds(ms), null);

    private static QueryResult Other(QueryOutcome outcome, int affected = 0, string? message = null) =>
        new(outcome, [], [], false, affected, TimeSpan.FromMilliseconds(3), message);

    private static string RenderText(SqliteTuiComponent c, int width = W, int height = H)
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        console.Write(c.Render(width, height));
        return writer.ToString().Replace("\r", "");
    }

    private static string[] RenderLines(SqliteTuiComponent c, int width = W, int height = H) =>
        RenderText(c, width, height).Split('\n');

    private void Run(SqliteTuiComponent c, params ConsoleKeyInfo[] keys)
    {
        foreach (var k in keys) c.HandleKey(k);
    }

    private void Expand(SqliteTuiComponent c, int downs = 0)
    {
        c.HandleKey(Key(ConsoleKey.Tab, shift: true)); // Editor -> Tree
        for (int i = 0; i < downs; i++) c.HandleKey(Key(ConsoleKey.DownArrow));
        c.HandleKey(Key(ConsoleKey.RightArrow));
    }

    // ---------------------------------------------------------------- initial state & rendering

    [Fact]
    public void Constructor_ShouldStartInEditorWithEmptyState_WhenCreated()
    {
        var c = Create();

        c.Focus.Should().Be(TuiFocus.Editor);
        c.Editor.Text.Should().BeEmpty();
        c.Prompt.Should().Be(TuiPrompt.None);
        c.StatusMessage.Should().BeNull();
        c.LastResult.Should().BeNull();
    }

    [Fact]
    public void Render_ShouldProduceExactlyHeightLines_WhenSizeIsLargeEnough()
    {
        foreach (var (w, h) in new[] { (80, 24), (120, 40), (140, 40), (100, 31) })
            RenderLines(Create(), w, h).Should().HaveCount(h, $"{w}x{h}");
    }

    [Fact]
    public void Render_ShouldNotExceedWidth_WhenSizeIsLargeEnough()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));
        foreach (var line in RenderLines(c, 80, 24))
            line.Length.Should().BeLessThanOrEqualTo(80);
    }

    [Fact]
    public void Render_ShouldShowPanelTitlesAndRootName_WhenRendered()
    {
        var text = RenderText(Create());

        text.Should().Contain("Tables").And.Contain("Query").And.Contain("Results").And.Contain("shop.db");
    }

    [Fact]
    public void Render_ShouldListTablesAndViewsWithViewSuffix_WhenCollapsed()
    {
        var text = RenderText(Create());

        text.Should().Contain("users").And.Contain("orders").And.Contain("v_active (view)");
        text.Should().NotContain("id INTEGER PK");
    }

    [Fact]
    public void Render_ShouldShowColumnsWithMarkers_WhenObjectExpanded()
    {
        var c = Create();
        Expand(c);

        var text = RenderText(c);

        text.Should().Contain("id INTEGER PK");
        text.Should().Contain("name TEXT NOT NULL");
        text.Should().Contain("misc");
        text.Should().NotContain("oid INTEGER");
    }

    [Fact]
    public void Render_ShouldOmitEmptyType_WhenColumnTypeIsEmpty()
    {
        var c = Create();
        Expand(c);

        RenderLines(c).Should().Contain(l => l.StartsWith("│ │   └── misc") && l.Substring(0, 32).TrimEnd('│', ' ').EndsWith("misc"));
    }

    [Fact]
    public void Render_ShouldEscapeMarkup_WhenNamesContainBrackets()
    {
        _schema = [new SchemaObject("we[ird]", SchemaObjectKind.Table, [new SchemaColumn("c[x]", "T", false, false)])];
        var c = Create();
        Expand(c);

        var text = RenderText(c);

        text.Should().Contain("we[ird]").And.Contain("c[x] T");
    }

    [Fact]
    public void Render_ShouldShowUnsavedChanges_WhenSessionDirty()
    {
        _dirty = true;

        RenderLines(Create()).Last().Should().Contain("● UNSAVED CHANGES").And.NotContain("saved ");
    }

    [Fact]
    public void Render_ShouldShowSaved_WhenSessionClean()
    {
        var status = RenderLines(Create()).Last();

        status.Should().Contain("saved").And.NotContain("UNSAVED").And.Contain("shop.db");
        status.Should().Contain("Tab focus · F5 run · Ctrl+S save · F8 discard · Ctrl+Q quit");
    }

    [Fact]
    public void Render_ShouldShowReadOnly_WhenSessionReadOnly()
    {
        _session.SetupGet(s => s.IsReadOnly).Returns(true);

        RenderLines(Create()).Last().Should().Contain("READ-ONLY");
    }

    [Fact]
    public void Render_ShouldNotShowReadOnly_WhenSessionWritable()
    {
        RenderText(Create()).Should().NotContain("READ-ONLY");
    }

    [Fact]
    public void Render_ShouldTruncateStatusToWidth_WhenHintsDoNotFit()
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(CtrlS);

        var status = RenderLines(c, 80, 24).Last();

        status.Should().StartWith("shop.db │ ● UNSAVED CHANGES │ Changes saved │ Tab focus");
        status.Length.Should().Be(80);
        status.TrimEnd().Should().EndWith("…");
    }

    [Theory]
    [InlineData(79, 24)]
    [InlineData(80, 23)]
    [InlineData(40, 10)]
    public void Render_ShouldShowTooSmallMessage_WhenBelowMinimum(int w, int h)
    {
        var lines = RenderLines(Create(), w, h);

        lines.Should().HaveCount(h);
        string.Join("\n", lines).Should().Contain(w >= 41 ? "Terminal too small (need at least 80x24)" : "Terminal too small");
        string.Join("\n", lines).Should().NotContain("Tables");
    }

    [Fact]
    public void Render_ShouldCentreTooSmallMessage_WhenBelowMinimum()
    {
        var lines = RenderLines(Create(), 60, 11);

        var index = Array.FindIndex(lines, l => l.Contains("Terminal too small (need at least 80x24)"));
        index.Should().Be(5);
        lines[index].IndexOf('T').Should().Be((60 - "Terminal too small (need at least 80x24)".Length) / 2);
    }

    [Fact]
    public void Render_ShouldShowEditorTextAndCursorCell_WhenTyping()
    {
        var c = Create();
        Type(c, "SELECT 1;");
        c.HandleKey(Key(ConsoleKey.Enter, '\r'));
        Type(c, "-- second");

        var text = RenderText(c);

        text.Should().Contain("SELECT 1;").And.Contain("-- second");
        c.Editor.Lines.Should().Equal("SELECT 1;", "-- second");
    }

    [Fact]
    public void Render_ShouldScrollEditorVertically_WhenCursorBelowViewport()
    {
        var c = Create();
        for (int i = 0; i < 30; i++)
        {
            Type(c, $"line{i:D2}");
            c.HandleKey(Key(ConsoleKey.Enter, '\r'));
        }
        Type(c, "last");

        var text = RenderText(c, 80, 24);

        text.Should().Contain("last").And.NotContain("line00");
    }

    [Fact]
    public void Render_ShouldScrollEditorHorizontally_WhenCursorBeyondWidth()
    {
        var c = Create();
        Type(c, new string('a', 120) + "ZEND");

        var lines = RenderLines(c, 80, 24);

        string.Join("\n", lines).Should().Contain("ZEND");
        lines.Should().OnlyContain(l => l.Length <= 80);
    }

    [Fact]
    public void Render_ShouldEscapeMarkup_WhenEditorContainsBrackets()
    {
        var c = Create();
        Type(c, "SELECT '[red]x[/]';");

        RenderText(c).Should().Contain("SELECT '[red]x[/]';");
    }

    // ---------------------------------------------------------------- results rendering

    [Fact]
    public void Render_ShouldShowRowCountAndHeaders_WhenResultSet()
    {
        _session.Setup(s => s.Execute("SELECT")).Returns(Rows(["id", "name"], [[1L, "ann"], [2L, "bob"]], ms: 7));
        var c = Create();
        Type(c, "SELECT");
        c.HandleKey(Key(ConsoleKey.F5));

        var text = RenderText(c);

        text.Should().Contain("2 rows (7 ms)").And.Contain("id").And.Contain("name").And.Contain("ann").And.Contain("bob");
    }

    [Fact]
    public void Render_ShouldUseSingularRow_WhenOneRow()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain("1 row (12 ms)");
    }

    [Fact]
    public void Render_ShouldFormatNullBlobAndNumbers_WhenResultHasSpecialValues()
    {
        _session.Setup(s => s.Execute(It.IsAny<string>()))
            .Returns(Rows(["n", "b", "d", "i"], [[null, new byte[] { 1, 2, 3 }, 1.5, 42L]]));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        var text = RenderText(c);

        text.Should().Contain("NULL").And.Contain("<blob 3 bytes>").And.Contain("1.5").And.Contain("42");
    }

    [Fact]
    public void Render_ShouldFormatDoubleWithInvariantCulture_WhenCultureUsesComma()
    {
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(["d"], [[2.5]]));
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var c = Create();
            c.HandleKey(Key(ConsoleKey.F5));

            RenderText(c).Should().Contain("2.5").And.NotContain("2,5");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Render_ShouldTruncateCellsTo40Chars_WhenValueLong()
    {
        string value = new string('x', 39) + "YZ";
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(["t"], [[value]]));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        var text = RenderText(c);

        text.Should().Contain(new string('x', 39) + "…");
        text.Should().NotContain("YZ");
    }

    [Fact]
    public void Render_ShouldEscapeMarkup_WhenCellContainsBrackets()
    {
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(["[c]"], [["[bold]hi[/]"]]));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain("[bold]hi[/]").And.Contain("[c]");
    }

    [Fact]
    public void Render_ShouldShowTruncationMessage_WhenResultTruncated()
    {
        var rows = Enumerable.Range(0, 1000).Select(i => new object?[] { (long)i }).ToArray();
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(["n"], rows, truncated: true, ms: 5));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain("Showing first 1000 rows (more available) (5 ms)");
    }

    [Theory]
    [InlineData(1, "1 row affected (3 ms)")]
    [InlineData(5, "5 rows affected (3 ms)")]
    [InlineData(0, "0 rows affected (3 ms)")]
    public void Render_ShouldShowAffectedMessage_WhenRowsAffected(int n, string expected)
    {
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Other(QueryOutcome.RowsAffected, n));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain(expected);
    }

    [Fact]
    public void Render_ShouldShowErrorMessage_WhenExecuteFails()
    {
        _session.Setup(s => s.Execute(It.IsAny<string>()))
            .Returns(Other(QueryOutcome.Error, message: "SQLite Error 1: 'no such table: nope'."));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain("SQLite Error 1: 'no such table: nope'.");
    }

    [Theory]
    [InlineData(QueryOutcome.Refused, "Transactions are managed by the TUI: Ctrl+S saves, F8 discards")]
    [InlineData(QueryOutcome.NothingToExecute, "Nothing to execute")]
    public void Render_ShouldShowMessage_WhenRefusedOrNothingToExecute(QueryOutcome outcome, string message)
    {
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Other(outcome, message: message));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain(message);
    }

    [Fact]
    public void Render_ShouldKeepExactHeight_WhenResultHasManyRowsAndColumns()
    {
        var columns = Enumerable.Range(0, 30).Select(i => $"column_{i}").ToArray();
        var rows = Enumerable.Range(0, 200).Select(i => columns.Select(_ => (object?)new string('v', 60)).ToArray()).ToArray();
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(columns, rows));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        foreach (var (w, h) in new[] { (80, 24), (120, 40) })
        {
            var lines = RenderLines(c, w, h);
            lines.Should().HaveCount(h);
            lines.Should().OnlyContain(l => l.Length <= w);
        }
    }

    [Fact]
    public void Render_ShouldKeepExactHeight_WhenErrorMessageIsMultiline()
    {
        _session.Setup(s => s.Execute(It.IsAny<string>()))
            .Returns(Other(QueryOutcome.Error, message: "line one\nline two " + new string('z', 300)));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));

        RenderLines(c, 80, 24).Should().HaveCount(24);
    }

    // ---------------------------------------------------------------- focus

    [Fact]
    public void HandleKey_ShouldCycleFocusForward_WhenTabPressed()
    {
        var c = Create();
        var seen = new List<TuiFocus>();
        for (int i = 0; i < 3; i++) { c.HandleKey(Key(ConsoleKey.Tab, '\t')); seen.Add(c.Focus); }

        seen.Should().Equal(TuiFocus.Results, TuiFocus.Tree, TuiFocus.Editor);
    }

    [Fact]
    public void HandleKey_ShouldCycleFocusBackward_WhenShiftTabPressed()
    {
        var c = Create();
        var seen = new List<TuiFocus>();
        for (int i = 0; i < 3; i++) { c.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true)); seen.Add(c.Focus); }

        seen.Should().Equal(TuiFocus.Tree, TuiFocus.Results, TuiFocus.Editor);
    }

    [Fact]
    public void HandleKey_ShouldKeepTabOutOfEditorText_WhenTabPressed()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.Tab, '\t'));

        c.Editor.Text.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- editor keys

    [Fact]
    public void HandleKey_ShouldEditText_WhenEditorFocused()
    {
        var c = Create();
        Type(c, "abc");
        c.HandleKey(Key(ConsoleKey.Backspace, '\b'));
        c.HandleKey(Key(ConsoleKey.Enter, '\r'));
        Type(c, "xy");
        c.HandleKey(Key(ConsoleKey.LeftArrow));
        c.HandleKey(Key(ConsoleKey.Delete));
        c.HandleKey(Key(ConsoleKey.Home));
        c.HandleKey(Key(ConsoleKey.UpArrow));
        c.HandleKey(Key(ConsoleKey.End));
        c.HandleKey(Key(ConsoleKey.RightArrow));
        c.HandleKey(Key(ConsoleKey.DownArrow));

        c.Editor.Lines.Should().Equal("ab", "x");
        c.Editor.CursorLine.Should().Be(1);
        c.Editor.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void HandleKey_ShouldMoveByVisibleRows_WhenPageKeysPressedInEditor()
    {
        var c = Create();
        for (int i = 0; i < 40; i++) { Type(c, "r"); c.HandleKey(Key(ConsoleKey.Enter, '\r')); }
        RenderText(c, 80, 24); // body 23, query panel 9 high => 7 visible rows

        c.HandleKey(Key(ConsoleKey.PageUp));
        c.Editor.CursorLine.Should().Be(40 - 7);
        c.HandleKey(Key(ConsoleKey.PageDown));
        c.Editor.CursorLine.Should().Be(40);
    }

    [Fact]
    public void HandleKey_ShouldInsertPastedCharacters_WhenPrintable()
    {
        var c = Create();
        Type(c, "é ü '\"");

        c.Editor.Text.Should().Be("é ü '\"");
    }

    [Fact]
    public void HandleKey_ShouldIgnoreControlCharacters_WhenEditorFocused()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F1, '\u0001'));
        c.HandleKey(Key(ConsoleKey.Escape, '\u001b'));

        c.Editor.Text.Should().BeEmpty();
    }

    [Fact]
    public void HandleKey_ShouldNotEditText_WhenEditorNotFocused()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.Tab, '\t'));
        Type(c, "abc");

        c.Editor.Text.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- execute / save / discard

    [Fact]
    public void HandleKey_ShouldExecuteEditorText_WhenF5Pressed()
    {
        var c = Create();
        Type(c, "SELECT 1;");
        c.HandleKey(Key(ConsoleKey.Enter, '\r'));
        Type(c, "SELECT 2;");

        c.HandleKey(Key(ConsoleKey.F5)).Should().BeTrue();

        _session.Verify(s => s.Execute("SELECT 1;\nSELECT 2;"), Times.Once);
        c.LastResult.Should().NotBeNull();
        c.LastResult!.Outcome.Should().Be(QueryOutcome.ResultSet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandleKey_ShouldExecuteEditorText_WhenCtrlEPressed(bool useKeyChar)
    {
        var c = Create();
        Type(c, "SELECT 5");

        c.HandleKey(useKeyChar ? Key(ConsoleKey.E, '\u0005') : Key(ConsoleKey.E, ctrl: true));

        _session.Verify(s => s.Execute("SELECT 5"), Times.Once);
    }

    [Fact]
    public void HandleKey_ShouldExecuteFromAnyFocus_WhenF5Pressed()
    {
        var c = Create();
        Type(c, "SELECT 9");
        c.HandleKey(Key(ConsoleKey.Tab, '\t'));

        c.HandleKey(Key(ConsoleKey.F5));

        _session.Verify(s => s.Execute("SELECT 9"), Times.Once);
    }

    [Fact]
    public void HandleKey_ShouldReloadSchemaAfterExecute_WhenSchemaChanged()
    {
        var c = Create();
        _schema = [new SchemaObject("fresh", SchemaObjectKind.Table, [])];

        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().Contain("fresh").And.NotContain("orders");
    }

    [Fact]
    public void HandleKey_ShouldReplaceLastResult_WhenExecutedAgain()
    {
        _session.SetupSequence(s => s.Execute(It.IsAny<string>()))
            .Returns(Rows(["a"], [[1L]]))
            .Returns(Other(QueryOutcome.Error, message: "boom"));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));
        c.HandleKey(Key(ConsoleKey.F5));

        c.LastResult!.Outcome.Should().Be(QueryOutcome.Error);
        RenderText(c).Should().Contain("boom");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandleKey_ShouldSave_WhenCtrlSPressed(bool useKeyChar)
    {
        _dirty = true;
        var c = Create();

        c.HandleKey(useKeyChar ? Key(ConsoleKey.S, '\u0013') : CtrlS).Should().BeTrue();

        _session.Verify(s => s.Save(), Times.Once);
        c.StatusMessage.Should().Be("Changes saved");
        RenderLines(c).Last().Should().Contain("Changes saved");
    }

    [Fact]
    public void HandleKey_ShouldShowFailure_WhenSaveFails()
    {
        _dirty = true;
        _session.Setup(s => s.Save()).Returns(new SessionActionResult(false, "SQLite Error 5: 'database is locked'."));
        var c = Create();

        c.HandleKey(CtrlS);

        c.StatusMessage.Should().Be("SQLite Error 5: 'database is locked'.");
        RenderLines(c).Last().Should().Contain("database is locked");
    }

    [Fact]
    public void HandleKey_ShouldShowNoChanges_WhenSavingCleanSession()
    {
        _session.Setup(s => s.Save()).Returns(new SessionActionResult(true, "No changes to save"));
        var c = Create();

        c.HandleKey(CtrlS);

        c.StatusMessage.Should().Be("No changes to save");
    }

    [Fact]
    public void HandleKey_ShouldPromptForDiscard_WhenF8PressedWhileDirty()
    {
        _dirty = true;
        var c = Create();

        c.HandleKey(Key(ConsoleKey.F8));

        c.Prompt.Should().Be(TuiPrompt.ConfirmDiscard);
        RenderLines(c).Last().Should().Contain("Discard unsaved changes? (y/n)").And.NotContain("Ctrl+Q quit");
        _session.Verify(s => s.Discard(), Times.Never);
    }

    [Fact]
    public void HandleKey_ShouldReportNothingToDiscard_WhenF8PressedWhileClean()
    {
        var c = Create();

        c.HandleKey(Key(ConsoleKey.F8));

        c.Prompt.Should().Be(TuiPrompt.None);
        c.StatusMessage.Should().Be("No changes to discard");
        _session.Verify(s => s.Discard(), Times.Never);
    }

    [Theory]
    [InlineData('y')]
    [InlineData('Y')]
    public void HandleKey_ShouldDiscardAndReload_WhenDiscardConfirmed(char answer)
    {
        _dirty = true;
        _session.Setup(s => s.Discard()).Returns(() => { _dirty = false; return new SessionActionResult(true, "Changes discarded"); });
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F8));
        _schema = [new SchemaObject("after", SchemaObjectKind.Table, [])];

        c.HandleKey(Chr(answer)).Should().BeTrue();

        c.Prompt.Should().Be(TuiPrompt.None);
        c.StatusMessage.Should().Be("Changes discarded");
        _session.Verify(s => s.Discard(), Times.Once);
        RenderText(c).Should().Contain("after");
    }

    [Theory]
    [InlineData('n')]
    [InlineData('N')]
    public void HandleKey_ShouldKeepChanges_WhenDiscardDeclined(char answer)
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F8));

        c.HandleKey(Chr(answer));

        c.Prompt.Should().Be(TuiPrompt.None);
        _session.Verify(s => s.Discard(), Times.Never);
    }

    [Fact]
    public void HandleKey_ShouldCancelDiscardPrompt_WhenEscapePressed()
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F8));

        c.HandleKey(Key(ConsoleKey.Escape, '\u001b'));

        c.Prompt.Should().Be(TuiPrompt.None);
        _session.Verify(s => s.Discard(), Times.Never);
    }

    [Fact]
    public void HandleKey_ShouldSwallowOtherKeys_WhenDiscardPromptActive()
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F8));

        Run(c, Chr('x'), Key(ConsoleKey.F5), CtrlS, Key(ConsoleKey.Tab, '\t'), Key(ConsoleKey.F8), CtrlQ);

        c.Prompt.Should().Be(TuiPrompt.ConfirmDiscard);
        c.Editor.Text.Should().BeEmpty();
        c.Focus.Should().Be(TuiFocus.Editor);
        _session.Verify(s => s.Execute(It.IsAny<string>()), Times.Never);
        _session.Verify(s => s.Save(), Times.Never);
    }

    // ---------------------------------------------------------------- quit

    [Theory]
    [InlineData('\u0011', ConsoleKey.Q, false)]
    [InlineData('\u0003', ConsoleKey.C, false)]
    [InlineData('\0', ConsoleKey.Q, true)]
    [InlineData('\0', ConsoleKey.C, true)]
    public void HandleKey_ShouldReturnFalse_WhenQuitWhileClean(char ch, ConsoleKey key, bool ctrl)
    {
        var c = Create();

        c.HandleKey(Key(key, ch, ctrl: ctrl)).Should().BeFalse();
    }

    [Theory]
    [InlineData('\u0011', ConsoleKey.Q)]
    [InlineData('\u0003', ConsoleKey.C)]
    public void HandleKey_ShouldPromptToSave_WhenQuitWhileDirty(char ch, ConsoleKey key)
    {
        _dirty = true;
        var c = Create();

        c.HandleKey(Key(key, ch)).Should().BeTrue();

        c.Prompt.Should().Be(TuiPrompt.ConfirmQuit);
        RenderLines(c).Last().Should().Contain("Unsaved changes: [S]ave, [D]iscard, [C]ancel");
    }

    [Theory]
    [InlineData('s')]
    [InlineData('S')]
    public void HandleKey_ShouldSaveAndExit_WhenSaveChosenAndSucceeds(char answer)
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(CtrlQ);

        c.HandleKey(Chr(answer)).Should().BeFalse();

        _session.Verify(s => s.Save(), Times.Once);
    }

    [Fact]
    public void HandleKey_ShouldStayAndShowError_WhenSaveChosenAndFails()
    {
        _dirty = true;
        _session.Setup(s => s.Save()).Returns(new SessionActionResult(false, "SQLite Error 5: 'database is locked'."));
        var c = Create();
        c.HandleKey(CtrlQ);

        c.HandleKey(Chr('s')).Should().BeTrue();

        c.Prompt.Should().Be(TuiPrompt.None);
        c.StatusMessage.Should().Be("SQLite Error 5: 'database is locked'.");
    }

    [Theory]
    [InlineData('d')]
    [InlineData('D')]
    public void HandleKey_ShouldDiscardAndExit_WhenDiscardChosen(char answer)
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(CtrlQ);

        c.HandleKey(Chr(answer)).Should().BeFalse();

        _session.Verify(s => s.Discard(), Times.Once);
        _session.Verify(s => s.Save(), Times.Never);
    }

    [Theory]
    [InlineData('c')]
    [InlineData('C')]
    public void HandleKey_ShouldReturnToTui_WhenQuitCancelled(char answer)
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(CtrlQ);

        c.HandleKey(Chr(answer)).Should().BeTrue();

        c.Prompt.Should().Be(TuiPrompt.None);
        _session.Verify(s => s.Save(), Times.Never);
        _session.Verify(s => s.Discard(), Times.Never);
    }

    [Fact]
    public void HandleKey_ShouldReturnToTui_WhenQuitPromptEscaped()
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(CtrlQ);

        c.HandleKey(Key(ConsoleKey.Escape, '\u001b')).Should().BeTrue();

        c.Prompt.Should().Be(TuiPrompt.None);
    }

    [Fact]
    public void HandleKey_ShouldSwallowOtherKeys_WhenQuitPromptActive()
    {
        _dirty = true;
        var c = Create();
        c.HandleKey(CtrlQ);

        Run(c, Chr('x'), Key(ConsoleKey.F5), CtrlS, Key(ConsoleKey.Tab, '\t'), Chr('y'), Chr('n'));

        c.Prompt.Should().Be(TuiPrompt.ConfirmQuit);
        c.Editor.Text.Should().BeEmpty();
        _session.Verify(s => s.Execute(It.IsAny<string>()), Times.Never);
        _session.Verify(s => s.Save(), Times.Never);
        _session.Verify(s => s.Discard(), Times.Never);
    }

    [Fact]
    public void HandleKey_ShouldNotDisposeSession_WhenQuitting()
    {
        var c = Create();
        c.HandleKey(CtrlQ);

        _session.Verify(s => s.Dispose(), Times.Never);
    }

    // ---------------------------------------------------------------- tree

    private string[] TreeLinesOf(SqliteTuiComponent c) =>
        RenderLines(c).Select(l => l.Length > 32 ? l[..32] : l).ToArray();

    [Fact]
    public void HandleKey_ShouldExpandSelectedObject_WhenRightPressedInTree()
    {
        var c = Create();
        Expand(c, downs: 1); // orders is first alphabetically? schema order is as supplied: users, orders, view

        var text = string.Join("\n", TreeLinesOf(c));
        text.Should().Contain("oid INTEGER PK NOT").And.NotContain("name TEXT");
    }

    [Fact]
    public void HandleKey_ShouldExpandSelectedObject_WhenEnterPressedInTree()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.Tab, shift: true));
        c.HandleKey(Key(ConsoleKey.Enter, '\r'));

        RenderText(c).Should().Contain("id INTEGER PK");
        c.Editor.Text.Should().BeEmpty();
    }

    [Fact]
    public void HandleKey_ShouldCollapseObject_WhenLeftPressedOnExpandedObject()
    {
        var c = Create();
        Expand(c);
        c.HandleKey(Key(ConsoleKey.LeftArrow));

        RenderText(c).Should().NotContain("id INTEGER PK");
    }

    [Fact]
    public void HandleKey_ShouldMoveToParent_WhenLeftPressedOnColumn()
    {
        var c = Create();
        Expand(c);
        c.HandleKey(Key(ConsoleKey.DownArrow)); // id
        c.HandleKey(Key(ConsoleKey.DownArrow)); // name
        c.HandleKey(Key(ConsoleKey.LeftArrow)); // -> users

        RenderText(c).Should().Contain("id INTEGER PK"); // still expanded
        c.HandleKey(Key(ConsoleKey.LeftArrow)); // collapse users
        RenderText(c).Should().NotContain("id INTEGER PK");
    }

    [Fact]
    public void HandleKey_ShouldTraverseColumnsAndObjects_WhenUpDownPressed()
    {
        var c = Create();
        Expand(c);
        c.HandleKey(Key(ConsoleKey.UpArrow));
        c.HandleKey(Key(ConsoleKey.UpArrow)); // clamp at top
        for (int i = 0; i < 20; i++) c.HandleKey(Key(ConsoleKey.DownArrow)); // clamp at bottom

        Action act = () => RenderText(c);
        act.Should().NotThrow();
    }

    [Fact]
    public void Render_ShouldHighlightSelectionStrongerWhenFocused_WhenTreeFocusChanges()
    {
        string Highlighted(SqliteTuiComponent c)
        {
            var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Interactive = InteractionSupport.No,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
                Out = new AnsiConsoleOutput(writer)
            });
            console.Profile.Width = 4096;
            console.Write(c.Render(W, H));
            return writer.ToString();
        }

        var c = Create();
        var unfocused = Highlighted(c);
        c.HandleKey(Key(ConsoleKey.Tab, shift: true));
        var focused = Highlighted(c);

        unfocused.Should().Contain("\u001b[2;4m├── ▸ users").And.NotContain("\u001b[7m├── ▸ users");
        focused.Should().Contain("\u001b[7m├── ▸ users").And.NotContain("\u001b[2;4m├── ▸ users");
    }

    [Fact]
    public void HandleKey_ShouldKeepExpansionAndSelection_WhenSchemaReloadedAfterExecute()
    {
        var c = Create();
        Expand(c, downs: 1); // expand orders, selection on orders
        c.HandleKey(Key(ConsoleKey.DownArrow)); // oid column
        _schema = [new SchemaObject("aaa", SchemaObjectKind.Table, []), .. _schema];

        c.HandleKey(Key(ConsoleKey.F5)); // reload; orders shifted down one row, still expanded
        c.HandleKey(Key(ConsoleKey.LeftArrow)); // selection must still be on oid column => moves to parent orders

        var text = string.Join("\n", TreeLinesOf(c));
        text.Should().Contain("aaa").And.Contain("orders").And.Contain("▾ orders");
        c.HandleKey(Key(ConsoleKey.LeftArrow)); // now collapses orders
        string.Join("\n", TreeLinesOf(c)).Should().Contain("▸ orders").And.NotContain("oid");
    }

    [Fact]
    public void HandleKey_ShouldDropExpansion_WhenObjectDisappearsAfterReload()
    {
        var c = Create();
        Expand(c);
        _schema = _schema.Where(o => o.Name != "users").ToList();

        c.HandleKey(Key(ConsoleKey.F5));

        RenderText(c).Should().NotContain("id INTEGER PK").And.NotContain("users");
    }

    [Fact]
    public void HandleKey_ShouldClampSelection_WhenSchemaBecomesEmpty()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.Tab, shift: true));
        c.HandleKey(Key(ConsoleKey.DownArrow));
        _schema = [];
        c.HandleKey(Key(ConsoleKey.F5));

        Action act = () =>
        {
            c.HandleKey(Key(ConsoleKey.DownArrow));
            c.HandleKey(Key(ConsoleKey.RightArrow));
            c.HandleKey(Key(ConsoleKey.LeftArrow));
            RenderText(c);
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Render_ShouldScrollTree_WhenSelectionBelowViewport()
    {
        _schema = Enumerable.Range(0, 60).Select(i => new SchemaObject($"tbl{i:D2}", SchemaObjectKind.Table, [])).ToList();
        var c = Create();
        c.HandleKey(Key(ConsoleKey.Tab, shift: true));
        for (int i = 0; i < 55; i++) c.HandleKey(Key(ConsoleKey.DownArrow));

        var text = RenderText(c, 80, 24);

        text.Should().Contain("tbl55").And.NotContain("tbl00");
        RenderLines(c, 80, 24).Should().HaveCount(24);
    }

    // ---------------------------------------------------------------- results navigation

    private SqliteTuiComponent WithBigResult(int rows = 100, int cols = 12)
    {
        var columns = Enumerable.Range(0, cols).Select(i => $"c{i}").ToArray();
        var data = Enumerable.Range(0, rows).Select(r => columns.Select((_, i) => (object?)$"r{r}x{i}").ToArray()).ToArray();
        _session.Setup(s => s.Execute(It.IsAny<string>())).Returns(Rows(columns, data));
        var c = Create();
        c.HandleKey(Key(ConsoleKey.F5));
        c.HandleKey(Key(ConsoleKey.Tab, '\t')); // Results
        RenderText(c, 80, 24);
        return c;
    }

    [Fact]
    public void HandleKey_ShouldScrollRows_WhenUpDownPressedInResults()
    {
        var c = WithBigResult();

        c.HandleKey(Key(ConsoleKey.DownArrow));
        c.HandleKey(Key(ConsoleKey.DownArrow));
        c.ResultRowOffset.Should().Be(2);
        RenderText(c, 80, 24).Should().Contain("r2x0").And.NotContain("r0x0");
        c.HandleKey(Key(ConsoleKey.UpArrow));
        c.ResultRowOffset.Should().Be(1);
        c.HandleKey(Key(ConsoleKey.UpArrow));
        c.HandleKey(Key(ConsoleKey.UpArrow));
        c.ResultRowOffset.Should().Be(0);
    }

    [Fact]
    public void HandleKey_ShouldPageRows_WhenPageKeysPressedInResults()
    {
        var c = WithBigResult();

        c.HandleKey(Key(ConsoleKey.PageDown));
        c.ResultRowOffset.Should().Be(7); // 14-high panel => 7 visible rows
        c.HandleKey(Key(ConsoleKey.PageUp));
        c.ResultRowOffset.Should().Be(0);
        for (int i = 0; i < 50; i++) c.HandleKey(Key(ConsoleKey.PageDown));
        c.ResultRowOffset.Should().Be(100 - 7);
    }

    [Fact]
    public void HandleKey_ShouldScrollColumns_WhenLeftRightPressedInResults()
    {
        var c = WithBigResult();

        c.HandleKey(Key(ConsoleKey.RightArrow));
        c.HandleKey(Key(ConsoleKey.RightArrow));
        c.ResultColumnOffset.Should().Be(2);
        var text = RenderText(c, 80, 24);
        text.Should().Contain("r0x2").And.NotContain("r0x0");
        c.HandleKey(Key(ConsoleKey.LeftArrow));
        c.ResultColumnOffset.Should().Be(1);
        for (int i = 0; i < 30; i++) c.HandleKey(Key(ConsoleKey.RightArrow));
        c.ResultColumnOffset.Should().Be(11);
    }

    [Fact]
    public void HandleKey_ShouldResetScroll_WhenHomePressedInResults()
    {
        var c = WithBigResult();
        Run(c, Key(ConsoleKey.PageDown), Key(ConsoleKey.RightArrow));

        c.HandleKey(Key(ConsoleKey.Home));

        c.ResultRowOffset.Should().Be(0);
        c.ResultColumnOffset.Should().Be(0);
    }

    [Fact]
    public void HandleKey_ShouldNotScrollOrThrow_WhenResultsEmpty()
    {
        var c = Create();
        c.HandleKey(Key(ConsoleKey.Tab, '\t'));

        Run(c, Key(ConsoleKey.DownArrow), Key(ConsoleKey.PageDown), Key(ConsoleKey.RightArrow), Key(ConsoleKey.Home));

        c.ResultRowOffset.Should().Be(0);
        c.ResultColumnOffset.Should().Be(0);
    }

    [Fact]
    public void HandleKey_ShouldResetScroll_WhenNewResultExecuted()
    {
        var c = WithBigResult();
        Run(c, Key(ConsoleKey.PageDown), Key(ConsoleKey.RightArrow));

        c.HandleKey(Key(ConsoleKey.F5));

        c.ResultRowOffset.Should().Be(0);
        c.ResultColumnOffset.Should().Be(0);
    }

    // ---------------------------------------------------------------- Run loop

    private sealed class FakeInput : ITerminalInput
    {
        private readonly Queue<ConsoleKeyInfo> _keys;
        private readonly Func<(int, int)> _size;
        public int ReadCount { get; private set; }
        public Exception? ThrowOnRead { get; init; }

        public FakeInput(IEnumerable<ConsoleKeyInfo> keys, Func<(int, int)>? size = null)
        {
            _keys = new Queue<ConsoleKeyInfo>(keys);
            _size = size ?? (() => (100, 30));
        }

        public bool KeyAvailable => ThrowOnRead is not null || _keys.Count > 0;

        public ConsoleKeyInfo ReadKey()
        {
            ReadCount++;
            if (ThrowOnRead is not null) throw ThrowOnRead;
            return _keys.Dequeue();
        }

        public (int Width, int Height) Size => _size();
    }

    private static (IAnsiConsole Console, StringWriter Out) CaptureConsole()
    {
        var writer = new StringWriter();
        return (ToolkitConsole.CreateCaptureConsole(writer), writer);
    }

    private const string Enter = "\u001b[?1049h";
    private const string Leave = "\u001b[?1049l";

    [Fact]
    public void Run_ShouldReturnZeroAndRestoreTerminal_WhenQuitKeyReceived()
    {
        var (console, output) = CaptureConsole();
        var input = new FakeInput([Chr('h'), Chr('i'), CtrlQ]);
        var c = Create();

        c.Run(console, input, CancellationToken.None).Should().Be(0);

        var text = output.ToString();
        c.Editor.Text.Should().Be("hi");
        input.ReadCount.Should().Be(3);
        text.Should().StartWith(Enter + "\u001b[?25l");
        text.Should().Contain("\u001b[H");
        text.Should().EndWith("\u001b[0m\u001b[?25h" + Leave);
        text.Should().Contain("Tables").And.Contain("hi");
    }

    [Fact]
    public void Run_ShouldRedrawAfterEveryKey_WhenKeysHandled()
    {
        var (console, output) = CaptureConsole();
        var c = Create();

        c.Run(console, new FakeInput([Chr('a'), Chr('b'), CtrlQ]), CancellationToken.None);

        // cursor-home occurs in the alternate-screen preamble, the initial frame and one frame per non-final key
        output.ToString().Split("\u001b[H").Length.Should().Be(1 + 1 + 1 + 2);
    }

    [Fact]
    public void Run_ShouldRedrawAtNewSize_WhenSizeChangesWhileIdle()
    {
        var (console, output) = CaptureConsole();
        int polls = 0;
        var keys = new Queue<ConsoleKeyInfo>();
        var size = new Func<(int, int)>(() =>
        {
            polls++;
            if (polls > 3 && keys.Count == 0) keys.Enqueue(CtrlQ);
            return polls > 2 ? (90, 26) : (100, 30);
        });
        var input = new DynamicInput(keys, size);

        Create().Run(console, input, CancellationToken.None).Should().Be(0);

        var text = output.ToString();
        text.Should().Contain("\u001b[2J");
        text.Split("\u001b[H").Length.Should().BeGreaterThan(2);
    }

    private sealed class DynamicInput(Queue<ConsoleKeyInfo> keys, Func<(int, int)> size) : ITerminalInput
    {
        public bool KeyAvailable => keys.Count > 0;
        public ConsoleKeyInfo ReadKey() => keys.Dequeue();
        public (int Width, int Height) Size => size();
    }

    [Fact]
    public void Run_ShouldReturnZeroWithoutDisposingSession_WhenCancelled()
    {
        var (console, output) = CaptureConsole();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Create().Run(console, new FakeInput([]), cts.Token).Should().Be(0);

        output.ToString().Should().StartWith(Enter).And.EndWith(Leave);
        _session.Verify(s => s.Dispose(), Times.Never);
    }

    [Fact]
    public void Run_ShouldStopWhenCancelledWhileIdle_WhenNoKeysArrive()
    {
        var (console, output) = CaptureConsole();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Create().Run(console, new FakeInput([]), cts.Token).Should().Be(0);

        output.ToString().Should().EndWith(Leave);
    }

    [Fact]
    public void Run_ShouldRestoreTerminalAndPropagate_WhenKeyReadThrows()
    {
        var (console, output) = CaptureConsole();
        var input = new FakeInput([]) { ThrowOnRead = new InvalidOperationException("input broke") };

        var act = () => Create().Run(console, input, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>().WithMessage("input broke");
        output.ToString().Should().StartWith(Enter).And.EndWith("\u001b[0m\u001b[?25h" + Leave);
    }

    [Fact]
    public void Run_ShouldRestoreTerminalAndPropagate_WhenSessionThrowsDuringExecute()
    {
        _session.Setup(s => s.Execute(It.IsAny<string>())).Throws(new IOException("disk gone"));
        var (console, output) = CaptureConsole();

        var act = () => Create().Run(console, new FakeInput([Key(ConsoleKey.F5)]), CancellationToken.None);

        act.Should().Throw<IOException>();
        output.ToString().Should().EndWith("\u001b[0m\u001b[?25h" + Leave);
    }

    [Fact]
    public void Run_ShouldRestoreTerminalAndPropagate_WhenInitialRenderThrows()
    {
        var (console, output) = CaptureConsole();
        var input = new FakeInput([], () => throw new InvalidOperationException("no size"));

        var act = () => Create().Run(console, input, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>();
        output.ToString().Should().EndWith(Leave);
    }

    [Fact]
    public void Run_ShouldRestoreTreatControlCAsInput_WhenFinished()
    {
        bool before;
        try { before = Console.TreatControlCAsInput; }
        catch (IOException) { return; } // no console attached: nothing to restore
        var (console, _) = CaptureConsole();

        Create().Run(console, new FakeInput([CtrlQ]), CancellationToken.None);

        Console.TreatControlCAsInput.Should().Be(before);
    }

    [Fact]
    public void Run_ShouldShowTooSmallMessage_WhenInputSizeBelowMinimum()
    {
        var (console, output) = CaptureConsole();

        Create().Run(console, new FakeInput([CtrlQ], () => (60, 12)), CancellationToken.None);

        output.ToString().Should().Contain("Terminal too small (need at least 80x24)");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSessionNull()
    {
        var act = () => new SqliteTuiComponent(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
