using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class QueryEditorBufferTests
{
    private static QueryEditorBuffer With(string text)
    {
        var buffer = new QueryEditorBuffer();
        buffer.Insert(text);
        return buffer;
    }

    [Fact]
    public void NewBuffer_ShouldHaveSingleEmptyLine_WhenCreated()
    {
        var buffer = new QueryEditorBuffer();

        buffer.Text.Should().BeEmpty();
        buffer.Lines.Should().Equal("");
        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void InsertChar_ShouldAppendAndAdvanceCursor_WhenPrintable()
    {
        var buffer = new QueryEditorBuffer();
        buffer.Insert('a');
        buffer.Insert('b');

        buffer.Text.Should().Be("ab");
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void InsertChar_ShouldInsertInMiddle_WhenCursorMoved()
    {
        var buffer = With("ac");
        buffer.MoveLeft();
        buffer.Insert('b');

        buffer.Text.Should().Be("abc");
        buffer.CursorColumn.Should().Be(2);
    }

    [Theory]
    [InlineData('\n')]
    [InlineData('\r')]
    public void InsertChar_ShouldStartNewLine_WhenNewlineChar(char c)
    {
        var buffer = With("ab");
        buffer.Insert(c);

        buffer.Lines.Should().Equal("ab", "");
        buffer.CursorLine.Should().Be(1);
    }

    [Fact]
    public void InsertChar_ShouldInsertFourSpaces_WhenTab()
    {
        var buffer = new QueryEditorBuffer();
        buffer.Insert('\t');

        buffer.Text.Should().Be("    ");
        buffer.CursorColumn.Should().Be(4);
    }

    [Fact]
    public void InsertChar_ShouldIgnore_WhenOtherControlChar()
    {
        var buffer = new QueryEditorBuffer();
        buffer.Insert('\u0001');

        buffer.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    public void InsertString_ShouldNormaliseNewlines_WhenAnyLineEndingUsed(string text)
    {
        var buffer = With(text);

        buffer.Lines.Should().Equal("a", "b");
        buffer.CursorLine.Should().Be(1);
        buffer.CursorColumn.Should().Be(1);
    }

    [Fact]
    public void InsertString_ShouldTreatCrLfAsOneNewline_WhenPasted()
    {
        var buffer = With("a\r\n\r\nb");

        buffer.Lines.Should().Equal("a", "", "b");
    }

    [Fact]
    public void InsertString_ShouldExpandTabs_WhenContainingTab()
    {
        var buffer = With("a\tb");

        buffer.Text.Should().Be("a    b");
    }

    [Fact]
    public void InsertString_ShouldSplitLine_WhenInsertedInMiddle()
    {
        var buffer = With("ad");
        buffer.MoveLeft();
        buffer.Insert("b\nc");

        buffer.Lines.Should().Equal("ab", "cd");
        buffer.CursorLine.Should().Be(1);
        buffer.CursorColumn.Should().Be(1);
    }

    [Fact]
    public void InsertString_ShouldDoNothing_WhenEmpty()
    {
        var buffer = With("x");
        buffer.Insert("");

        buffer.Text.Should().Be("x");
    }

    [Fact]
    public void NewLine_ShouldSplitAtCursor_WhenInMiddleOfLine()
    {
        var buffer = With("abcd");
        buffer.MoveLeft();
        buffer.MoveLeft();
        buffer.NewLine();

        buffer.Lines.Should().Equal("ab", "cd");
        buffer.CursorLine.Should().Be(1);
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void Backspace_ShouldRemovePreviousChar_WhenColumnPositive()
    {
        var buffer = With("abc");
        buffer.Backspace();

        buffer.Text.Should().Be("ab");
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void Backspace_ShouldJoinWithPreviousLine_WhenAtLineStart()
    {
        var buffer = With("ab\ncd");
        buffer.Home();
        buffer.Backspace();

        buffer.Lines.Should().Equal("abcd");
        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void Backspace_ShouldDoNothing_WhenAtBufferStart()
    {
        var buffer = With("ab");
        buffer.MoveLeft();
        buffer.MoveLeft();
        buffer.Backspace();

        buffer.Text.Should().Be("ab");
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void Delete_ShouldRemoveCharAtCursor_WhenNotAtLineEnd()
    {
        var buffer = With("abc");
        buffer.Home();
        buffer.Delete();

        buffer.Text.Should().Be("bc");
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void Delete_ShouldJoinNextLine_WhenAtLineEnd()
    {
        var buffer = With("ab\ncd");
        buffer.MoveUp();
        buffer.End();
        buffer.Delete();

        buffer.Lines.Should().Equal("abcd");
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void Delete_ShouldDoNothing_WhenAtBufferEnd()
    {
        var buffer = With("ab");
        buffer.Delete();

        buffer.Text.Should().Be("ab");
    }

    [Fact]
    public void MoveLeft_ShouldWrapToPreviousLineEnd_WhenAtLineStart()
    {
        var buffer = With("abc\nd");
        buffer.Home();
        buffer.MoveLeft();

        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(3);
    }

    [Fact]
    public void MoveLeft_ShouldStay_WhenAtBufferStart()
    {
        var buffer = new QueryEditorBuffer();
        buffer.MoveLeft();

        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void MoveRight_ShouldWrapToNextLineStart_WhenAtLineEnd()
    {
        var buffer = With("ab\ncd");
        buffer.MoveUp();
        buffer.End();
        buffer.MoveRight();

        buffer.CursorLine.Should().Be(1);
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void MoveRight_ShouldStay_WhenAtBufferEnd()
    {
        var buffer = With("ab");
        buffer.MoveRight();

        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void MoveUp_ShouldClampColumn_WhenPreviousLineShorter()
    {
        var buffer = With("ab\ncdefg");
        buffer.MoveUp();

        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void MoveDown_ShouldClampColumn_WhenNextLineShorter()
    {
        var buffer = With("abcdef\ngh");
        buffer.MoveUp();
        buffer.End();
        buffer.MoveDown();

        buffer.CursorLine.Should().Be(1);
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void MoveUpAndDown_ShouldStay_WhenAtBufferEdges()
    {
        var buffer = With("ab");
        buffer.MoveUp();
        buffer.MoveDown();

        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(2);
    }

    [Fact]
    public void HomeAndEnd_ShouldMoveToLineEdges_WhenCalled()
    {
        var buffer = With("abc");
        buffer.Home();
        buffer.CursorColumn.Should().Be(0);
        buffer.End();
        buffer.CursorColumn.Should().Be(3);
    }

    [Fact]
    public void PageUp_ShouldMoveByRowsAndClamp_WhenCalled()
    {
        var buffer = With("a\nb\nc\nd\ne");
        buffer.PageUp(2);
        buffer.CursorLine.Should().Be(2);
        buffer.PageUp(10);
        buffer.CursorLine.Should().Be(0);
    }

    [Fact]
    public void PageDown_ShouldMoveByRowsAndClamp_WhenCalled()
    {
        var buffer = With("a\nb\nc\nd\ne");
        buffer.PageUp(10);
        buffer.PageDown(3);
        buffer.CursorLine.Should().Be(3);
        buffer.PageDown(10);
        buffer.CursorLine.Should().Be(4);
    }

    [Fact]
    public void Clear_ShouldResetTextAndCursor_WhenCalled()
    {
        var buffer = With("ab\ncd");
        buffer.Clear();

        buffer.Text.Should().BeEmpty();
        buffer.Lines.Should().Equal("");
        buffer.CursorLine.Should().Be(0);
        buffer.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void Text_ShouldJoinLinesWithLineFeed_WhenMultipleLines()
    {
        var buffer = With("a\r\nb\nc");

        buffer.Text.Should().Be("a\nb\nc");
    }
}
