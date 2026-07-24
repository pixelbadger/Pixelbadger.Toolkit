using System.Text;
using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class BpeTokenizerTests
{
    [Fact]
    public void Build_ShouldStartFromByteBaseVocabulary_WhenNoMergesRequested()
    {
        var tokenizer = BpeTokenizer.Build("hello world", vocabSize: 256);

        tokenizer.VocabSize.Should().Be(256);
        tokenizer.Merges.Should().BeEmpty();
        // With no merges every token is a raw byte.
        tokenizer.Encode("hi").Should().Equal(104, 105);
    }

    [Fact]
    public void Build_ShouldThrow_WhenVocabSizeBelowByteBase()
    {
        var act = () => BpeTokenizer.Build("hello", vocabSize: 255);

        act.Should().Throw<ArgumentException>().WithMessage("*at least 256*");
    }

    [Fact]
    public void Build_ShouldThrow_WhenCorpusIsEmpty()
    {
        var act = () => BpeTokenizer.Build("", vocabSize: 512);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_ShouldMergeMostFrequentPairFirst()
    {
        // "ab" is the most frequent adjacent byte pair (a=97, b=98) across the chunks.
        var tokenizer = BpeTokenizer.Build("ab ab ab ab", vocabSize: 257);

        tokenizer.Merges.Should().HaveCount(1);
        tokenizer.Merges[0].Should().Be((97, 98));
        tokenizer.VocabSize.Should().Be(257);
        // The learned pair now encodes to a single token id (256).
        tokenizer.Encode("ab").Should().Equal(256);
    }

    [Fact]
    public void EncodeDecode_ShouldRoundTripAsciiText()
    {
        var tokenizer = BpeTokenizer.Build("the cat sat on the mat the cat ran", vocabSize: 300);

        var text = "the cat sat on the mat";
        tokenizer.Decode(tokenizer.Encode(text)).Should().Be(text);
    }

    [Fact]
    public void EncodeDecode_ShouldRoundTripUnicode_EvenWhenUnseenInTraining()
    {
        // Byte-level BPE represents any input; unseen characters fall back to raw bytes.
        var tokenizer = BpeTokenizer.Build("plain ascii training corpus", vocabSize: 300);

        var text = "café ☃ \U0001F680";
        tokenizer.Decode(tokenizer.Encode(text)).Should().Be(text);
    }

    [Fact]
    public void Encode_ShouldCompressRepetitiveCorpusBelowByteCount()
    {
        var corpus = string.Concat(Enumerable.Repeat("the quick brown fox jumps. ", 40));
        var tokenizer = BpeTokenizer.Build(corpus, vocabSize: 400);

        int byteCount = Encoding.UTF8.GetByteCount(corpus);
        int tokenCount = tokenizer.Encode(corpus).Length;

        tokenCount.Should().BeLessThan(byteCount, "BPE merges should pack repeated text into fewer tokens");
    }

    [Fact]
    public void Build_ShouldBeDeterministic_ForSameCorpusAndVocabSize()
    {
        var corpus = string.Concat(Enumerable.Repeat("deterministic merges please. ", 20));

        var a = BpeTokenizer.Build(corpus, vocabSize: 320);
        var b = BpeTokenizer.Build(corpus, vocabSize: 320);

        a.VocabSize.Should().Be(b.VocabSize);
        a.Merges.Should().Equal(b.Merges);
        a.Encode(corpus).Should().Equal(b.Encode(corpus));
    }

    [Fact]
    public void ExportThenRestore_ShouldPreserveEncoding()
    {
        var corpus = string.Concat(Enumerable.Repeat("round trip the tokenizer state. ", 20));
        var original = BpeTokenizer.Build(corpus, vocabSize: 360);

        var restored = BpeTokenizer.FromState(original.ExportState());

        restored.VocabSize.Should().Be(original.VocabSize);
        restored.Encode(corpus).Should().Equal(original.Encode(corpus));
    }

    [Fact]
    public void ExportState_ShouldDeclareBpeKindWithMergesOnly()
    {
        var state = BpeTokenizer.Build("state shape check check check", vocabSize: 280).ExportState();

        state.Kind.Should().Be(TokenizerKind.Bpe);
        state.Vocabulary.Should().BeNull();
        state.Merges.Should().NotBeNull();
        state.Merges!.Should().OnlyContain(pair => pair.Length == 2);
    }

    [Fact]
    public void Decode_ShouldThrow_WhenTokenIdOutOfRange()
    {
        var tokenizer = BpeTokenizer.Build("small corpus", vocabSize: 256);

        var act = () => tokenizer.Decode(new[] { 999 });

        act.Should().Throw<ArgumentException>().WithMessage("*outside the vocabulary*");
    }
}
