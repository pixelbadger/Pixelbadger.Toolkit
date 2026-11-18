using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services.Rag;

public sealed class ParagraphTextChunker : ITextChunker
{
    public const int MinChunkSize = 100;
    public const int MaxChunkSize = 100_000;

    private static readonly Regex BlankLineRun = new(@"\n(?:[^\S\n]*\n)+", RegexOptions.Compiled);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        text = BlankLineRun.Replace(text, "\n\n");
        return text.Trim();
    }

    public ChunkingResult Chunk(string rawText, int chunkSize, int chunkOverlap)
    {
        if (chunkSize < MinChunkSize || chunkSize > MaxChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, $"chunk-size must be between {MinChunkSize} and {MaxChunkSize}.");
        if (chunkOverlap < 0 || chunkOverlap > chunkSize / 2)
            throw new ArgumentOutOfRangeException(nameof(chunkOverlap), chunkOverlap, "chunk-overlap must be between 0 and half of chunk-size.");

        var text = Normalize(rawText);
        if (text.Length == 0)
            return new ChunkingResult(text, Array.Empty<TextChunk>());

        // Base chunks leave room for the overlap prefix so that every chunk stays within chunkSize.
        var limit = chunkSize - chunkOverlap;
        var units = new List<(int Start, int End)>();
        foreach (var (ps, pe) in Paragraphs(text))
        {
            if (pe - ps <= limit)
                units.Add((ps, pe));
            else
                SplitOversized(text, ps, pe, limit, units);
        }

        var chunks = new List<TextChunk>();
        var i = 0;
        while (i < units.Count)
        {
            var start = units[i].Start;
            var end = units[i].End;
            i++;
            while (i < units.Count && units[i].End - start <= limit)
            {
                end = units[i].End;
                i++;
            }

            var from = chunks.Count == 0 ? start : OverlapStart(text, start, chunkOverlap);
            chunks.Add(new TextChunk(chunks.Count, from, end, text.Substring(from, end - from)));
        }

        return new ChunkingResult(text, chunks);
    }

    private static IEnumerable<(int Start, int End)> Paragraphs(string text)
    {
        var pos = 0;
        while (pos < text.Length)
        {
            var idx = text.IndexOf("\n\n", pos, StringComparison.Ordinal);
            var rawEnd = idx < 0 ? text.Length : idx;
            var span = Trim(text, pos, rawEnd);
            if (span.End > span.Start)
                yield return span;
            pos = idx < 0 ? text.Length : idx + 2;
        }
    }

    private static (int Start, int End) Trim(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        return (start, end);
    }

    private static void SplitOversized(string text, int start, int end, int limit, List<(int, int)> units)
    {
        var sentences = new List<(int Start, int End)>();
        var paragraph = text.Substring(start, end - start);
        var cursor = 0;
        foreach (Match m in SentenceBreak.Matches(paragraph))
        {
            sentences.Add((start + cursor, start + m.Index));
            cursor = m.Index + m.Length;
        }
        sentences.Add((start + cursor, end));

        // Greedily pack sentences into pieces; sentences that are themselves too long fall back to words.
        var pieces = new List<(int Start, int End)>();
        foreach (var s in sentences)
        {
            if (s.End - s.Start <= limit) pieces.Add(s);
            else SplitWords(text, s.Start, s.End, limit, pieces);
        }

        units.AddRange(pieces);
    }

    private static void SplitWords(string text, int start, int end, int limit, List<(int, int)> pieces)
    {
        var words = new List<(int Start, int End)>();
        var p = start;
        while (p < end)
        {
            while (p < end && char.IsWhiteSpace(text[p])) p++;
            var ws = p;
            while (p < end && !char.IsWhiteSpace(text[p])) p++;
            if (p > ws) words.Add((ws, p));
        }

        var curStart = -1;
        var curEnd = -1;
        foreach (var (ws, we) in words)
        {
            if (we - ws > limit)
            {
                if (curStart >= 0) { pieces.Add((curStart, curEnd)); curStart = -1; }
                HardSplit(text, ws, we, limit, pieces);
                continue;
            }
            if (curStart < 0) { curStart = ws; curEnd = we; }
            else if (we - curStart <= limit) curEnd = we;
            else { pieces.Add((curStart, curEnd)); curStart = ws; curEnd = we; }
        }
        if (curStart >= 0) pieces.Add((curStart, curEnd));
    }

    private static void HardSplit(string text, int start, int end, int limit, List<(int, int)> pieces)
    {
        var p = start;
        while (p < end)
        {
            var q = Math.Min(p + limit, end);
            if (q < end && char.IsLowSurrogate(text[q]) && char.IsHighSurrogate(text[q - 1]) && q - 1 > p)
                q--;
            pieces.Add((p, q));
            p = q;
        }
    }

    /// <summary>Start of the overlap prefix: up to <paramref name="overlap"/> chars back, moved forward to a word boundary.</summary>
    private static int OverlapStart(string text, int start, int overlap)
    {
        if (overlap == 0) return start;
        var c = Math.Max(0, start - overlap);
        while (c < start && c > 0 && !char.IsWhiteSpace(text[c - 1]) && !char.IsWhiteSpace(text[c]))
            c++;
        while (c < start && char.IsWhiteSpace(text[c]))
            c++;
        return c;
    }
}
