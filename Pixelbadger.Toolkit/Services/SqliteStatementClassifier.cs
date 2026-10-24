using System.Text;
using SQLitePCL;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Result of classifying a SQL batch; see <see cref="SqliteStatementClassifier.Classify"/>.</summary>
public sealed record BatchPlan(int Statements, bool HasTransactionControl, bool HasWrite, bool HasTransactionIncompatible);

/// <summary>Splits and classifies SQL batches (statement count, transaction control, writes) for <see cref="SqliteSession"/>.</summary>
public static class SqliteStatementClassifier
{
    private static readonly HashSet<string> TransactionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "COMMIT", "END", "ROLLBACK", "SAVEPOINT", "RELEASE"
    };

    // Splits the batch with sqlite3_prepare_v2 so statements are classified exactly as SQLite parses them.
    // A statement that cannot be prepared (typically it depends on an object created earlier in the
    // same batch) switches to a lexical scan of the remaining text and counts as writing.
    public static BatchPlan Classify(sqlite3 handle, string sql)
    {
        var statements = 0;
        var control = false;
        var write = false;
        var incompatible = false;
        var remaining = sql;

        while (remaining.Length > 0)
        {
            sqlite3_stmt? stmt = null;
            try
            {
                var rc = raw.sqlite3_prepare_v2(handle, remaining, out stmt, out var tail);
                if (rc != raw.SQLITE_OK)
                {
                    var (count, scanControl, scanIncompatible) = ScanLexically(remaining);
                    if (count == 0)
                        break;
                    return new BatchPlan(statements + count, control || scanControl, true, incompatible || scanIncompatible);
                }

                tail ??= "";
                if (stmt is not null && !stmt.IsInvalid)
                {
                    statements++;
                    var consumed = remaining[..(remaining.Length - tail.Length)];
                    var keyword = FirstKeyword(consumed);
                    if (keyword is not null && TransactionKeywords.Contains(keyword))
                        control = true;
                    if (IsTransactionIncompatible(consumed))
                        incompatible = true;
                    if (raw.sqlite3_stmt_readonly(stmt) == 0)
                        write = true;
                }

                if (tail.Length >= remaining.Length)
                    break;
                remaining = tail;
            }
            finally
            {
                if (stmt is not null && !stmt.IsInvalid)
                    raw.sqlite3_finalize(stmt);
            }
        }

        return new BatchPlan(statements, control, write, incompatible);
    }

    internal static (int Count, bool Control, bool Incompatible) ScanLexically(string text)
    {
        var count = 0;
        var control = false;
        var incompatible = false;
        foreach (var piece in SplitStatements(text))
        {
            var keyword = FirstKeyword(piece);
            if (keyword is null)
                continue;
            count++;
            if (TransactionKeywords.Contains(keyword))
                control = true;
            if (IsTransactionIncompatible(piece))
                incompatible = true;
        }
        return (count, control, incompatible);
    }

    // Splits on ';' outside '...', "...", [...], `...` and comments; comments stay out of the pieces.
    internal static IEnumerable<string> SplitStatements(string text)
    {
        var current = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n') i++;
                current.Append(' ');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                current.Append(' ');
            }
            else if (c is '\'' or '"' or '`' or '[')
            {
                var close = c == '[' ? ']' : c;
                current.Append(c);
                i++;
                while (i < text.Length)
                {
                    current.Append(text[i]);
                    if (text[i] == close)
                    {
                        // A doubled quote is an escaped quote, not the end.
                        if (close != ']' && i + 1 < text.Length && text[i + 1] == close)
                        {
                            current.Append(text[i + 1]);
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
            }
            else if (c == ';')
            {
                if (IsOpenTrigger(current.ToString()))
                {
                    current.Append(c);
                    i++;
                    continue;
                }
                yield return current.ToString();
                current.Clear();
                i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }
        yield return current.ToString();
    }

    // True while text is a CREATE [TEMP|TEMPORARY] TRIGGER whose BEGIN ... END body has not closed yet.
    internal static bool IsOpenTrigger(string text)
    {
        var words = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\'' or '"' or '`' or '[')
            {
                var close = c == '[' ? ']' : c;
                i++;
                while (i < text.Length && text[i] != close) i++;
                i++;
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                words.Add(text[start..i]);
            }
            else
                i++;
        }

        bool Is(int index, string word) => index < words.Count && words[index].Equals(word, StringComparison.OrdinalIgnoreCase);
        if (!Is(0, "CREATE"))
            return false;
        var next = Is(1, "TEMP") || Is(1, "TEMPORARY") ? 2 : 1;
        if (!Is(next, "TRIGGER"))
            return false;

        var begun = false;
        var caseDepth = 0;
        for (var w = next + 1; w < words.Count; w++)
        {
            if (!begun)
            {
                begun = Is(w, "BEGIN");
            }
            else if (Is(w, "CASE"))
                caseDepth++;
            else if (Is(w, "END"))
            {
                if (caseDepth == 0)
                    return false;
                caseDepth--;
            }
        }
        return begun;
    }

    // First keyword of a statement after leading whitespace and comments; null when there is none.
    internal static string? FirstKeyword(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == ';')
                i++;
            else if (text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else
                break;
        }

        var start = i;
        while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '_'))
            i++;
        return i > start ? text[start..i] : (start < text.Length ? text[start].ToString() : null);
    }


    // Statements SQLite rejects or silently ignores inside a transaction: VACUUM, ATTACH, DETACH and
    // PRAGMA journal_mode / foreign_keys when assigning a value.
    internal static bool IsTransactionIncompatible(string statement)
    {
        var keyword = FirstKeyword(statement);
        if (keyword is null)
            return false;
        if (keyword.Equals("VACUUM", StringComparison.OrdinalIgnoreCase)
            || keyword.Equals("ATTACH", StringComparison.OrdinalIgnoreCase)
            || keyword.Equals("DETACH", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!keyword.Equals("PRAGMA", StringComparison.OrdinalIgnoreCase))
            return false;

        var words = statement.Replace(";", " ");
        var idx = words.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) + keyword.Length;
        var rest = words[idx..];
        var m = System.Text.RegularExpressions.Regex.Match(rest,
            @"^\s*(?:(?:\w+|""[^""]*""|\[[^\]]*\]|`[^`]*`)\s*\.\s*)?(journal_mode|foreign_keys)\s*[=(]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success;
    }
}
