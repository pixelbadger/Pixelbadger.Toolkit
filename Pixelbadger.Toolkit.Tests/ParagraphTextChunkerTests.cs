using System.Text;
using FluentAssertions;
using Pixelbadger.Toolkit.Services.Rag;
using Xunit;

namespace Pixelbadger.Toolkit.Tests;

public class ParagraphTextChunkerTests
{
    private readonly ParagraphTextChunker _chunker = new();

    private static string Words(int count, string prefix = "word")
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++) sb.Append(prefix).Append(i).Append(i % 7 == 6 ? ". " : " ");
        return sb.ToString().Trim();
    }

    private void AssertInvariants(string raw, int size, int overlap)
    {
        var result = _chunker.Chunk(raw, size, overlap);
        var text = result.NormalizedText;
        text.Should().Be(ParagraphTextChunker.Normalize(raw));

        var covered = new int[text.Length];
        for (var i = 0; i < result.Chunks.Count; i++)
        {
            var c = result.Chunks[i];
            c.Index.Should().Be(i);
            c.Content.Length.Should().BeLessThanOrEqualTo(size);
            c.Content.Should().Be(text.Substring(c.Start, c.End - c.Start));
            c.Content.Should().NotBeEmpty();
            char.IsWhiteSpace(c.Content[0]).Should().BeFalse();
            char.IsWhiteSpace(c.Content[^1]).Should().BeFalse();
            if (i > 0)
            {
                c.End.Should().BeGreaterThan(result.Chunks[i - 1].End, "chunks advance");
            }
            for (var p = c.Start; p < c.End; p++) covered[p]++;
        }

        for (var p = 0; p < text.Length; p++)
        {
            if (!char.IsWhiteSpace(text[p]))
                covered[p].Should().BeGreaterThan(0, $"char at {p} must be covered");
            if (overlap == 0)
                covered[p].Should().BeLessThanOrEqualTo(1, $"char at {p} covered once without overlap");
        }
        if (result.Chunks.Count > 0)
            result.Chunks[0].Start.Should().Be(0);
        if (result.Chunks.Count > 0)
            result.Chunks[^1].End.Should().Be(text.Length);
    }

    public static IEnumerable<object[]> Cases()
    {
        var paragraphs = string.Join("\n\n", Enumerable.Range(0, 30).Select(i => $"Paragraph {i} " + Words(12, "p" + i)));
        var oversized = Words(400);
        var noSentences = string.Join(' ', Enumerable.Range(0, 300).Select(i => "tok" + i));
        var longToken = "start " + new string('x', 450) + " end";
        var crlf = "alpha beta\r\n\r\ngamma delta\r\n\r\n\r\n   \r\nepsilon\rzeta\r\r\reta";
        var unicode = string.Join("\n\n", Enumerable.Range(0, 20).Select(i => "Ünïcödé 日本語のテキスト 😀🎉 " + Words(10, "ß" + i)));
        var emoji = string.Concat(Enumerable.Repeat("😀", 300));
        var mixed = paragraphs + "\n\n" + oversized + "\n\n" + longToken + "\n\n" + unicode + "\n\n" + noSentences;
        foreach (var text in new[] { paragraphs, oversized, noSentences, longToken, crlf, unicode, emoji, mixed })
            foreach (var (size, overlap) in new[] { (100, 0), (100, 50), (100, 13), (250, 0), (250, 100), (1000, 0), (1000, 500), (5000, 77) })
                yield return new object[] { text, size, overlap };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Chunk_ShouldSatisfyInvariants_WhenAnyInputProvided(string raw, int size, int overlap)
    {
        AssertInvariants(raw, size, overlap);
    }

    [Fact]
    public void Chunk_ShouldPackWholeParagraphs_WhenTheyFit()
    {
        var text = "aaa aaa\n\nbbb bbb\n\nccc ccc";
        var r = _chunker.Chunk(text, 100, 0);
        r.Chunks.Should().ContainSingle().Which.Content.Should().Be(text);

        var big = string.Join("\n\n", new[] { new string('a', 60), new string('b', 60), new string('c', 30) });
        var r2 = _chunker.Chunk(big, 100, 0);
        r2.Chunks.Select(c => c.Content).Should().Equal(new string('a', 60), new string('b', 60) + "\n\n" + new string('c', 30));
    }

    [Fact]
    public void Chunk_ShouldSplitAtSentences_WhenParagraphTooLong()
    {
        var s1 = new string('a', 70) + ".";
        var s2 = new string('b', 70) + "!";
        var s3 = new string('c', 70) + "?";
        var r = _chunker.Chunk($"{s1} {s2} {s3}", 100, 0);
        r.Chunks.Select(c => c.Content).Should().Equal(s1, s2, s3);
    }

    [Fact]
    public void Chunk_ShouldFallBackToWords_WhenSentenceTooLong()
    {
        var r = _chunker.Chunk(string.Join(' ', Enumerable.Repeat("abcdefghi", 40)), 100, 0);
        r.Chunks.Count.Should().BeGreaterThan(3);
        r.Chunks.Should().OnlyContain(c => c.Content.Split(' ').All(w => w == "abcdefghi"));
    }

    [Fact]
    public void Chunk_ShouldHardSplit_WhenSingleTokenExceedsChunkSize()
    {
        var token = new string('x', 450);
        var r = _chunker.Chunk(token, 100, 0);
        r.Chunks.Select(c => c.Content.Length).Should().Equal(100, 100, 100, 100, 50);
        string.Concat(r.Chunks.Select(c => c.Content)).Should().Be(token);
    }

    [Fact]
    public void Chunk_ShouldNotSplitSurrogatePairs_WhenHardSplitting()
    {
        var r = _chunker.Chunk(string.Concat(Enumerable.Repeat("😀", 301)), 101, 0);
        foreach (var c in r.Chunks)
        {
            char.IsLowSurrogate(c.Content[0]).Should().BeFalse();
            char.IsHighSurrogate(c.Content[^1]).Should().BeFalse();
        }
    }

    [Fact]
    public void Chunk_ShouldPrefixOverlapAtWordBoundary_WhenOverlapGiven()
    {
        var text = Words(200);
        var r = _chunker.Chunk(text, 200, 50);
        r.Chunks.Count.Should().BeGreaterThan(2);
        for (var i = 1; i < r.Chunks.Count; i++)
        {
            var prev = r.Chunks[i - 1];
            var cur = r.Chunks[i];
            cur.Start.Should().BeLessThan(prev.End, "overlap present");
            (prev.End - cur.Start).Should().BeLessThanOrEqualTo(50);
            // starts on a word boundary
            (cur.Start == 0 || char.IsWhiteSpace(r.NormalizedText[cur.Start - 1])).Should().BeTrue();
        }
    }

    [Fact]
    public void Normalize_ShouldUnifyLineEndingsAndCollapseBlankRuns_WhenMessyInput()
    {
        ParagraphTextChunker.Normalize("\r\n a\r\nb\r\r\n  \t\r\n\n\nc \n\n")
            .Should().Be("a\nb\n\nc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\r\n\t ")]
    public void Chunk_ShouldReturnNoChunks_WhenTextHasNoContent(string raw)
    {
        var r = _chunker.Chunk(raw, 100, 0);
        r.Chunks.Should().BeEmpty();
        r.NormalizedText.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_ShouldHandleBoundarySizes_WhenMinAndMax()
    {
        var text = Words(500);
        AssertInvariants(text, 100, 50);
        AssertInvariants(text, 100000, 50000);
        _chunker.Chunk(text, 100000, 0).Chunks.Should().ContainSingle();
        var exact = new string('a', 100);
        _chunker.Chunk(exact, 100, 0).Chunks.Should().ContainSingle();
        _chunker.Chunk(exact + "b", 100, 0).Chunks.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(99, 0, "chunkSize")]
    [InlineData(100001, 0, "chunkSize")]
    [InlineData(100, -1, "chunkOverlap")]
    [InlineData(100, 51, "chunkOverlap")]
    [InlineData(101, 51, "chunkOverlap")]
    public void Chunk_ShouldThrow_WhenSizeOrOverlapOutOfRange(int size, int overlap, string param)
    {
        var act = () => _chunker.Chunk("text", size, overlap);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(param);
    }

    [Fact]
    public void Chunk_ShouldAcceptHalfSizeOverlap_WhenSizeIsOdd()
    {
        _chunker.Chunk(Words(100), 101, 50).Chunks.Should().NotBeEmpty();
    }
}
