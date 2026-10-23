using System.Text;

namespace Pixelbadger.Toolkit.Components;

// Pure multi-line text model behind the SQLite TUI query editor; no rendering or terminal knowledge.
public sealed class QueryEditorBuffer
{
    private const int TabWidth = 4;

    private readonly List<string> _lines = [string.Empty];

    public string Text => string.Join("\n", _lines);

    public IReadOnlyList<string> Lines => _lines;

    public int CursorLine { get; private set; }

    public int CursorColumn { get; private set; }

    public void Insert(char c)
    {
        if (c is '\r' or '\n') { NewLine(); return; }
        if (c == '\t') { InsertRun(new string(' ', TabWidth)); return; }
        if (char.IsControl(c)) return;
        InsertRun(c.ToString());
    }

    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var run = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                FlushRun(run);
                NewLine();
            }
            else if (c == '\t') run.Append(' ', TabWidth);
            else if (!char.IsControl(c)) run.Append(c);
        }
        FlushRun(run);
    }

    public void NewLine()
    {
        var line = _lines[CursorLine];
        _lines[CursorLine] = line[..CursorColumn];
        _lines.Insert(CursorLine + 1, line[CursorColumn..]);
        CursorLine++;
        CursorColumn = 0;
    }

    public void Backspace()
    {
        if (CursorColumn > 0)
        {
            _lines[CursorLine] = _lines[CursorLine].Remove(CursorColumn - 1, 1);
            CursorColumn--;
        }
        else if (CursorLine > 0)
        {
            int previousLength = _lines[CursorLine - 1].Length;
            _lines[CursorLine - 1] += _lines[CursorLine];
            _lines.RemoveAt(CursorLine);
            CursorLine--;
            CursorColumn = previousLength;
        }
    }

    public void Delete()
    {
        if (CursorColumn < _lines[CursorLine].Length)
            _lines[CursorLine] = _lines[CursorLine].Remove(CursorColumn, 1);
        else if (CursorLine < _lines.Count - 1)
        {
            _lines[CursorLine] += _lines[CursorLine + 1];
            _lines.RemoveAt(CursorLine + 1);
        }
    }

    public void MoveLeft()
    {
        if (CursorColumn > 0) CursorColumn--;
        else if (CursorLine > 0)
        {
            CursorLine--;
            CursorColumn = _lines[CursorLine].Length;
        }
    }

    public void MoveRight()
    {
        if (CursorColumn < _lines[CursorLine].Length) CursorColumn++;
        else if (CursorLine < _lines.Count - 1)
        {
            CursorLine++;
            CursorColumn = 0;
        }
    }

    public void MoveUp() => MoveVertical(-1);

    public void MoveDown() => MoveVertical(1);

    public void Home() => CursorColumn = 0;

    public void End() => CursorColumn = _lines[CursorLine].Length;

    public void PageUp(int rows) => MoveVertical(-Math.Max(1, rows));

    public void PageDown(int rows) => MoveVertical(Math.Max(1, rows));

    public void Clear()
    {
        _lines.Clear();
        _lines.Add(string.Empty);
        CursorLine = 0;
        CursorColumn = 0;
    }

    private void MoveVertical(int delta)
    {
        CursorLine = Math.Clamp(CursorLine + delta, 0, _lines.Count - 1);
        CursorColumn = Math.Min(CursorColumn, _lines[CursorLine].Length);
    }

    private void FlushRun(StringBuilder run)
    {
        if (run.Length == 0) return;
        InsertRun(run.ToString());
        run.Clear();
    }

    private void InsertRun(string s)
    {
        _lines[CursorLine] = _lines[CursorLine].Insert(CursorColumn, s);
        CursorColumn += s.Length;
    }
}
