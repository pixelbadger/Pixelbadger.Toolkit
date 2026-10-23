using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.Components;

public enum TuiFocus { Tree, Editor, Results }

public enum TuiPrompt { None, ConfirmQuit, ConfirmDiscard }

// State machine and renderer for the interactive SQLite TUI; all database work goes through ISqliteSession.
public sealed class SqliteTuiComponent
{
    public const int MinWidth = 80;
    public const int MinHeight = 24;
    private const int TreeWidth = 32;
    private const int MaxCellLength = 40;
    private const string TooSmallMessage = "Terminal too small (need at least 80x24)";
    private const string DiscardPromptText = "Discard unsaved changes? (y/n)";
    private const string QuitPromptText = "Unsaved changes: [S]ave, [D]iscard, [C]ancel";
    private const string KeyHints = "Tab focus · F5 run · Ctrl+S save · F8 discard · Ctrl+Q quit";
    private const string EnterAlternateScreen = "\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H";
    private const string LeaveAlternateScreen = "\x1b[0m\x1b[?25h\x1b[?1049l";

    private sealed record TreeRow(int ObjectIndex, int ColumnIndex);

    private readonly ISqliteSession _session;
    private readonly HashSet<(string Name, SchemaObjectKind Kind)> _expanded = [];
    private IReadOnlyList<SchemaObject> _schema = [];
    private List<TreeRow> _rows = [];
    private int _selected;
    private int _treeTop;
    private int _editorTop;
    private int _editorLeft;
    private int _width = MinWidth;
    private int _height = MinHeight;
    private bool _statusIsError;

    public SqliteTuiComponent(ISqliteSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ReloadSchema();
    }

    public TuiFocus Focus { get; private set; } = TuiFocus.Editor;

    public QueryEditorBuffer Editor { get; } = new();

    public string? StatusMessage { get; private set; }

    public TuiPrompt Prompt { get; private set; } = TuiPrompt.None;

    public QueryResult? LastResult { get; private set; }

    public int ResultRowOffset { get; private set; }

    public int ResultColumnOffset { get; private set; }

    // ---------------------------------------------------------------- input

    public bool HandleKey(ConsoleKeyInfo key)
    {
        StatusMessage = null;
        _statusIsError = false;

        if (Prompt != TuiPrompt.None) return HandlePromptKey(key);

        if (IsCtrl(key, ConsoleKey.Q, '\u0011') || IsCtrl(key, ConsoleKey.C, '\u0003'))
        {
            if (!_session.HasUnsavedChanges) return false;
            Prompt = TuiPrompt.ConfirmQuit;
            return true;
        }
        if (key.Key == ConsoleKey.Tab)
        {
            CycleFocus((key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1);
            return true;
        }
        if (key.Key == ConsoleKey.F5 || IsCtrl(key, ConsoleKey.E, '\u0005'))
        {
            Execute();
            return true;
        }
        if (IsCtrl(key, ConsoleKey.S, '\u0013'))
        {
            var result = _session.Save();
            SetStatus(result);
            ReloadSchema();
            return true;
        }
        if (key.Key == ConsoleKey.F8)
        {
            if (_session.HasUnsavedChanges) Prompt = TuiPrompt.ConfirmDiscard;
            else StatusMessage = "No changes to discard";
            return true;
        }

        switch (Focus)
        {
            case TuiFocus.Tree: HandleTreeKey(key); break;
            case TuiFocus.Editor: HandleEditorKey(key); break;
            default: HandleResultsKey(key); break;
        }
        return true;
    }

    private bool HandlePromptKey(ConsoleKeyInfo key)
    {
        char c = char.ToLowerInvariant(key.KeyChar);
        bool escape = key.Key == ConsoleKey.Escape;

        if (Prompt == TuiPrompt.ConfirmDiscard)
        {
            if (c == 'y')
            {
                SetStatus(_session.Discard());
                ReloadSchema();
                Prompt = TuiPrompt.None;
            }
            else if (c == 'n' || escape) Prompt = TuiPrompt.None;
            return true;
        }

        if (c == 's')
        {
            var result = _session.Save();
            if (result.Success) return false;
            Prompt = TuiPrompt.None;
            SetStatus(result);
            ReloadSchema();
        }
        else if (c == 'd')
        {
            var result = _session.Discard();
            if (result.Success) return false;
            Prompt = TuiPrompt.None;
            SetStatus(result);
            ReloadSchema();
        }
        else if (c == 'c' || escape) Prompt = TuiPrompt.None;
        return true;
    }

    private void HandleEditorKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter: Editor.NewLine(); return;
            case ConsoleKey.Backspace: Editor.Backspace(); return;
            case ConsoleKey.Delete: Editor.Delete(); return;
            case ConsoleKey.LeftArrow: Editor.MoveLeft(); return;
            case ConsoleKey.RightArrow: Editor.MoveRight(); return;
            case ConsoleKey.UpArrow: Editor.MoveUp(); return;
            case ConsoleKey.DownArrow: Editor.MoveDown(); return;
            case ConsoleKey.Home: Editor.Home(); return;
            case ConsoleKey.End: Editor.End(); return;
            case ConsoleKey.PageUp: Editor.PageUp(GetLayout().EditorRows); return;
            case ConsoleKey.PageDown: Editor.PageDown(GetLayout().EditorRows); return;
        }

        if (key.KeyChar is '\r' or '\n') Editor.NewLine();
        else if (key.KeyChar >= ' ' && !char.IsControl(key.KeyChar) && (key.Modifiers & ConsoleModifiers.Control) == 0)
            Editor.Insert(key.KeyChar);
    }

    private void HandleTreeKey(ConsoleKeyInfo key)
    {
        if (_rows.Count == 0) return;
        var row = _rows[_selected];
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: _selected = Math.Max(0, _selected - 1); break;
            case ConsoleKey.DownArrow: _selected = Math.Min(_rows.Count - 1, _selected + 1); break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.Enter:
                if (row.ColumnIndex < 0 && _expanded.Add(ObjectKey(_schema[row.ObjectIndex])))
                    RebuildRows(row);
                break;
            case ConsoleKey.LeftArrow:
                if (row.ColumnIndex >= 0)
                    _selected = _rows.FindIndex(r => r.ObjectIndex == row.ObjectIndex && r.ColumnIndex < 0);
                else if (_expanded.Remove(ObjectKey(_schema[row.ObjectIndex])))
                    RebuildRows(row);
                break;
        }
    }

    private void HandleResultsKey(ConsoleKeyInfo key)
    {
        int rowCount = LastResult?.Rows.Count ?? 0;
        int columnCount = LastResult?.Columns.Count ?? 0;
        int page = GetLayout().ResultRows;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: ResultRowOffset--; break;
            case ConsoleKey.DownArrow: ResultRowOffset++; break;
            case ConsoleKey.PageUp: ResultRowOffset -= page; break;
            case ConsoleKey.PageDown: ResultRowOffset += page; break;
            case ConsoleKey.LeftArrow: ResultColumnOffset--; break;
            case ConsoleKey.RightArrow: ResultColumnOffset++; break;
            case ConsoleKey.Home: ResultRowOffset = 0; ResultColumnOffset = 0; break;
        }
        ResultRowOffset = Math.Clamp(ResultRowOffset, 0, Math.Max(0, rowCount - page));
        ResultColumnOffset = Math.Clamp(ResultColumnOffset, 0, Math.Max(0, columnCount - 1));
    }

    private void CycleFocus(int direction) =>
        Focus = (TuiFocus)(((int)Focus + direction + 3) % 3);

    private void Execute()
    {
        LastResult = _session.Execute(Editor.Text);
        ResultRowOffset = 0;
        ResultColumnOffset = 0;
        ReloadSchema();
    }

    private void SetStatus(SessionActionResult result)
    {
        StatusMessage = result.Message;
        _statusIsError = !result.Success;
    }

    private static bool IsCtrl(ConsoleKeyInfo key, ConsoleKey consoleKey, char control) =>
        key.KeyChar == control || (key.Key == consoleKey && (key.Modifiers & ConsoleModifiers.Control) != 0);

    // ---------------------------------------------------------------- tree model

    private static (string, SchemaObjectKind) ObjectKey(SchemaObject o) => (o.Name, o.Kind);

    private void ReloadSchema()
    {
        TreeRow? previous = _selected < _rows.Count ? _rows[_selected] : null;
        var previousObject = previous is not null && previous.ObjectIndex < _schema.Count ? _schema[previous.ObjectIndex] : null;
        string? previousColumn = previousObject is not null && previous!.ColumnIndex >= 0 && previous.ColumnIndex < previousObject.Columns.Count
            ? previousObject.Columns[previous.ColumnIndex].Name
            : null;
        int previousIndex = _selected;

        IReadOnlyList<SchemaObject> schema;
        try
        {
            schema = _session.GetSchema();
        }
        catch (SqliteException ex)
        {
            // A transient failure (e.g. another process holds a lock) must not end the TUI: keep the previous tree.
            ShowSchemaRefreshFailure(ex);
            return;
        }

        _schema = schema;
        _expanded.IntersectWith(_schema.Select(ObjectKey));
        _rows = BuildRows();

        int found = -1;
        if (previousObject is not null)
        {
            int objectIndex = IndexOfObject(previousObject);
            if (objectIndex >= 0)
            {
                found = _rows.FindIndex(r => r.ObjectIndex == objectIndex && r.ColumnIndex < 0);
                if (previousColumn is not null)
                {
                    int columnIndex = _schema[objectIndex].Columns.ToList().FindIndex(c => c.Name == previousColumn);
                    int columnRow = columnIndex < 0 ? -1 : _rows.FindIndex(r => r.ObjectIndex == objectIndex && r.ColumnIndex == columnIndex);
                    if (columnRow >= 0) found = columnRow;
                }
            }
        }
        _selected = found >= 0 ? found : Math.Clamp(previousIndex, 0, Math.Max(0, _rows.Count - 1));
    }

    // The action's own error wins; the schema error only replaces an empty or success status line.
    private void ShowSchemaRefreshFailure(SqliteException ex)
    {
        if (StatusMessage is not null && _statusIsError) return;
        StatusMessage = $"Could not refresh tables: {ex.Message}";
        _statusIsError = true;
    }

    private int IndexOfObject(SchemaObject o)
    {
        for (int i = 0; i < _schema.Count; i++)
            if (_schema[i].Name == o.Name && _schema[i].Kind == o.Kind) return i;
        return -1;
    }

    // Rebuilds visible rows after an expand/collapse, keeping the same row selected.
    private void RebuildRows(TreeRow selection)
    {
        _rows = BuildRows();
        int index = _rows.FindIndex(r => r.ObjectIndex == selection.ObjectIndex && r.ColumnIndex == selection.ColumnIndex);
        _selected = index >= 0 ? index : 0;
    }

    private List<TreeRow> BuildRows()
    {
        var rows = new List<TreeRow>();
        for (int i = 0; i < _schema.Count; i++)
        {
            rows.Add(new TreeRow(i, -1));
            if (!_expanded.Contains(ObjectKey(_schema[i]))) continue;
            for (int c = 0; c < _schema[i].Columns.Count; c++)
                rows.Add(new TreeRow(i, c));
        }
        return rows;
    }

    // ---------------------------------------------------------------- run loop

    public int Run(IAnsiConsole console, ITerminalInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(input);

        var writer = console.Profile.Out.Writer;
        bool? previousTreatControlC = null;
        try
        {
            try { previousTreatControlC = Console.TreatControlCAsInput; } catch (Exception) { }
            try { Console.TreatControlCAsInput = true; } catch (Exception) { }

            writer.Write(EnterAlternateScreen);
            writer.Flush();

            var size = input.Size;
            Draw(console, writer, size);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!input.KeyAvailable)
                {
                    var current = input.Size;
                    if (current != size)
                    {
                        size = current;
                        writer.Write("\x1b[2J");
                        Draw(console, writer, size);
                    }
                    else cancellationToken.WaitHandle.WaitOne(30);
                    continue;
                }

                if (!HandleKey(input.ReadKey())) break;
                size = input.Size;
                Draw(console, writer, size);
            }
            return 0;
        }
        finally
        {
            writer.Write(LeaveAlternateScreen);
            writer.Flush();
            if (previousTreatControlC is bool previous)
            {
                try { Console.TreatControlCAsInput = previous; } catch (Exception) { }
            }
        }
    }

    private void Draw(IAnsiConsole console, TextWriter writer, (int Width, int Height) size)
    {
        writer.Write("\x1b[H");
        console.Write(Render(size.Width, size.Height));
        writer.Flush();
    }

    // ---------------------------------------------------------------- rendering

    private sealed record Layout(int BodyHeight, int QueryHeight, int ResultsHeight, int RightWidth)
    {
        public int EditorRows => Math.Max(1, QueryHeight - 2);
        public int EditorColumns => Math.Max(1, RightWidth - 4);
        public int ResultRows => Math.Max(1, ResultsHeight - 2 - 1 - 4);
        public int TreeRows => Math.Max(1, BodyHeight - 2 - 1);
    }

    private Layout GetLayout() => ComputeLayout(_width, _height);

    private static Layout ComputeLayout(int width, int height)
    {
        int body = Math.Max(1, height - 1);
        int query = Math.Max(6, (int)Math.Round(body * 0.4));
        return new Layout(body, query, body - query, Math.Max(1, width - TreeWidth));
    }

    public IRenderable Render(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        if (width < MinWidth || height < MinHeight) return RenderTooSmall(_width, _height);

        var layout = ComputeLayout(width, height);
        var grid = new Grid { Width = width };
        grid.AddColumn(new GridColumn { Width = TreeWidth, NoWrap = true, Padding = new Padding(0, 0, 0, 0) });
        grid.AddColumn(new GridColumn { Width = layout.RightWidth, NoWrap = true, Padding = new Padding(0, 0, 0, 0) });
        grid.AddRow(
            RenderTree(layout),
            new Rows(RenderEditor(layout), RenderResults(layout)));

        return new TrimmedRenderable(new Rows(grid, RenderStatus(width)));
    }

    // Rows terminates its last child with a line break; a full-height frame must not end in one or the terminal scrolls.
    private sealed class TrimmedRenderable(IRenderable inner) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var segments = inner.Render(options, maxWidth).ToList();
            int end = segments.Count;
            while (end > 0 && segments[end - 1].IsLineBreak) end--;
            return segments.Take(end);
        }
    }

    private static IRenderable RenderTooSmall(int width, int height)
    {
        var lines = new string[height];
        Array.Fill(lines, string.Empty);
        string text = Clip(TooSmallMessage, width);
        int pad = Math.Max(0, (width - text.Length) / 2);
        lines[(height - 1) / 2] = new string(' ', pad) + Markup.Escape(text);
        return new Markup(string.Join("\n", lines));
    }

    private static Color BorderFor(bool focused) => focused ? Color.Yellow : Color.Grey;

    private static Panel MakePanel(IRenderable content, string header, bool focused, int width, int height) =>
        new(content)
        {
            Header = new PanelHeader(header),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(BorderFor(focused)),
            Width = width,
            Height = height
        };

    private static IRenderable Content(IEnumerable<string> markupLines) =>
        new Markup(string.Join("\n", markupLines)) { Overflow = Overflow.Crop };

    private IRenderable RenderTree(Layout layout)
    {
        bool focused = Focus == TuiFocus.Tree;
        int inner = TreeWidth - 4;
        int viewport = layout.TreeRows;

        if (_selected < _treeTop) _treeTop = _selected;
        if (_selected >= _treeTop + viewport) _treeTop = _selected - viewport + 1;
        _treeTop = Math.Clamp(_treeTop, 0, Math.Max(0, _rows.Count - 1));

        var lines = new List<string> { "[bold]" + Markup.Escape(Clip(Path.GetFileName(_session.DatabasePath), inner)) + "[/]" };
        for (int i = _treeTop; i < _rows.Count && i < _treeTop + viewport; i++)
        {
            string text = Clip(TreeLabel(_rows[i]), inner);
            string escaped = Markup.Escape(text);
            lines.Add(i != _selected ? escaped : focused ? $"[invert]{escaped}[/]" : $"[dim underline]{escaped}[/]");
        }

        return MakePanel(Content(lines), "Tables", focused, TreeWidth, layout.BodyHeight);
    }

    private string TreeLabel(TreeRow row)
    {
        var obj = _schema[row.ObjectIndex];
        bool lastObject = row.ObjectIndex == _schema.Count - 1;
        if (row.ColumnIndex < 0)
        {
            string name = obj.Kind == SchemaObjectKind.View ? $"{obj.Name} (view)" : obj.Name;
            string marker = _expanded.Contains(ObjectKey(obj)) ? "▾ " : "▸ ";
            return (lastObject ? "└── " : "├── ") + marker + name;
        }

        var column = obj.Columns[row.ColumnIndex];
        var sb = new StringBuilder(lastObject ? "    " : "│   ");
        sb.Append(row.ColumnIndex == obj.Columns.Count - 1 ? "└── " : "├── ");
        sb.Append(column.Name);
        if (column.Type.Length > 0) sb.Append(' ').Append(column.Type);
        if (column.IsPrimaryKey) sb.Append(" PK");
        if (column.IsNotNull) sb.Append(" NOT NULL");
        return sb.ToString();
    }

    private IRenderable RenderEditor(Layout layout)
    {
        bool focused = Focus == TuiFocus.Editor;
        int rows = layout.EditorRows;
        int columns = layout.EditorColumns;

        if (Editor.CursorLine < _editorTop) _editorTop = Editor.CursorLine;
        if (Editor.CursorLine >= _editorTop + rows) _editorTop = Editor.CursorLine - rows + 1;
        if (Editor.CursorColumn < _editorLeft) _editorLeft = Editor.CursorColumn;
        if (Editor.CursorColumn >= _editorLeft + columns) _editorLeft = Editor.CursorColumn - columns + 1;

        var lines = new List<string>(rows);
        for (int r = 0; r < rows; r++)
        {
            int lineIndex = _editorTop + r;
            string line = lineIndex < Editor.Lines.Count ? Editor.Lines[lineIndex] : string.Empty;
            string visible = _editorLeft < line.Length ? line[_editorLeft..Math.Min(line.Length, _editorLeft + columns)] : string.Empty;

            if (focused && lineIndex == Editor.CursorLine)
            {
                int c = Editor.CursorColumn - _editorLeft;
                string before = visible[..Math.Min(c, visible.Length)];
                string cursor = c < visible.Length ? visible[c].ToString() : " ";
                string after = c + 1 < visible.Length ? visible[(c + 1)..] : string.Empty;
                lines.Add(Markup.Escape(before) + "[invert]" + Markup.Escape(cursor) + "[/]" + Markup.Escape(after));
            }
            else lines.Add(Markup.Escape(visible));
        }

        return MakePanel(Content(lines), "Query", focused, layout.RightWidth, layout.QueryHeight);
    }

    private IRenderable RenderResults(Layout layout)
    {
        bool focused = Focus == TuiFocus.Results;
        int inner = layout.RightWidth - 4;
        var items = new List<IRenderable> { new Markup(ResultMessage(inner)) { Overflow = Overflow.Crop } };

        if (LastResult is { Outcome: QueryOutcome.ResultSet } result && result.Columns.Count > 0)
            items.Add(BuildTable(result, layout.ResultRows, inner));

        return MakePanel(new Rows(items), "Results", focused, layout.RightWidth, layout.ResultsHeight);
    }

    private string ResultMessage(int width)
    {
        if (LastResult is null) return string.Empty;
        var r = LastResult;
        long ms = (long)r.Elapsed.TotalMilliseconds;
        switch (r.Outcome)
        {
            case QueryOutcome.ResultSet:
                string text = r.Truncated
                    ? $"Showing first {r.Rows.Count} rows (more available) ({ms} ms)"
                    : $"{r.Rows.Count} {(r.Rows.Count == 1 ? "row" : "rows")} ({ms} ms)";
                return Markup.Escape(Clip(text, width));
            case QueryOutcome.RowsAffected:
                return Markup.Escape(Clip($"{r.RowsAffected} {(r.RowsAffected == 1 ? "row" : "rows")} affected ({ms} ms)", width));
            case QueryOutcome.Error:
                return "[red]" + Markup.Escape(Clip(OneLine(r.Message ?? "Error"), width)) + "[/]";
            default:
                return "[yellow]" + Markup.Escape(Clip(OneLine(r.Message ?? string.Empty), width)) + "[/]";
        }
    }

    private IRenderable BuildTable(QueryResult result, int visibleRows, int available)
    {
        int rowCount = result.Rows.Count;
        ResultRowOffset = Math.Clamp(ResultRowOffset, 0, Math.Max(0, rowCount - visibleRows));
        ResultColumnOffset = Math.Clamp(ResultColumnOffset, 0, result.Columns.Count - 1);

        var pageRows = result.Rows.Skip(ResultRowOffset).Take(visibleRows).ToList();
        int used = 1;
        var widths = new List<int>();
        var table = new Table().Border(TableBorder.Rounded);
        for (int c = ResultColumnOffset; c < result.Columns.Count; c++)
        {
            int width = Clip(result.Columns[c], MaxCellLength).Length;
            foreach (var row in pageRows)
                width = Math.Max(width, CellText(row[c]).Length);
            width = Math.Max(1, Math.Min(width, available - 4));
            if (widths.Count > 0 && used + width + 3 > available) break;
            used += width + 3;
            widths.Add(width);
            table.AddColumn(new TableColumn(new Markup("[bold]" + Markup.Escape(Clip(result.Columns[c], MaxCellLength)) + "[/]"))
            {
                NoWrap = true,
                Width = width
            });
        }

        foreach (var row in pageRows)
        {
            var cells = new List<IRenderable>(widths.Count);
            for (int i = 0; i < widths.Count; i++)
            {
                object? value = row[ResultColumnOffset + i];
                cells.Add(new Markup(value is null ? "[dim]NULL[/]" : Markup.Escape(Clip(CellText(value), widths[i]))) { Overflow = Overflow.Crop });
            }
            table.AddRow(cells);
        }
        return table;
    }

    private static string CellText(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => $"<blob {bytes.Length} bytes>",
        double d => Clip(Convert.ToString(d, CultureInfo.InvariantCulture)!, MaxCellLength),
        IFormattable f => Clip(f.ToString(null, CultureInfo.InvariantCulture), MaxCellLength),
        _ => Clip(OneLine(value.ToString() ?? string.Empty), MaxCellLength)
    };

    private IRenderable RenderStatus(int width)
    {
        var parts = new List<(string Text, string? Style)>();
        if (Prompt != TuiPrompt.None)
            parts.Add((Prompt == TuiPrompt.ConfirmQuit ? QuitPromptText : DiscardPromptText, "bold yellow"));
        else
        {
            parts.Add((Path.GetFileName(_session.DatabasePath), "bold"));
            if (_session.IsReadOnly) parts.Add(("READ-ONLY", "red"));
            parts.Add(_session.HasUnsavedChanges ? ("● UNSAVED CHANGES", "yellow") : ("saved", "dim"));
            if (!string.IsNullOrEmpty(StatusMessage)) parts.Add((OneLine(StatusMessage), _statusIsError ? "red" : null));
            parts.Add((KeyHints, "dim"));
        }

        var sb = new StringBuilder();
        int remaining = width;
        for (int i = 0; i < parts.Count && remaining > 0; i++)
        {
            if (i > 0)
            {
                string separator = " │ ";
                string sep = Clip(separator, remaining);
                sb.Append(sep);
                remaining -= sep.Length;
                if (remaining <= 0) break;
            }
            string text = Clip(parts[i].Text, remaining);
            remaining -= text.Length;
            string escaped = Markup.Escape(text);
            sb.Append(parts[i].Style is null ? escaped : $"[{parts[i].Style}]{escaped}[/]");
        }
        sb.Append(' ', Math.Max(0, remaining));
        return new Markup(sb.ToString()) { Overflow = Overflow.Crop };
    }

    private static string OneLine(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    private static string Clip(string s, int width)
    {
        if (width <= 0) return string.Empty;
        if (s.Length <= width) return s;
        return width == 1 ? "…" : s[..(width - 1)] + "…";
    }
}
