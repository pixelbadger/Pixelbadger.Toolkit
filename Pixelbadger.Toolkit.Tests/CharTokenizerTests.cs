using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class CharTokenizerTests
{
    [Fact]
    public void Build_ShouldCreateSortedDistinctVocabulary()
    {
        var tokenizer = CharTokenizer.Build("banana");

        tokenizer.VocabSize.Should().Be(3);
        tokenizer.Vocabulary.Should().Equal('a', 'b', 'n');
    }

    [Fact]
    public void EncodeDecode_ShouldRoundTrip()
    {
        var tokenizer = CharTokenizer.Build("hello world");

        var ids = tokenizer.Encode("hello world");
        var text = tokenizer.Decode(ids);

        text.Should().Be("hello world");
    }

    [Fact]
    public void Encode_ShouldThrow_WhenCharacterNotInVocabulary()
    {
        var tokenizer = CharTokenizer.Build("abc");

        var act = () => tokenizer.Encode("abz");

        act.Should().Throw<ArgumentException>().WithMessage("*not present*");
    }

    [Fact]
    public void Build_ShouldThrow_WhenCorpusIsEmpty()
    {
        var act = () => CharTokenizer.Build("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromVocabulary_ShouldPreserveOrderingForDeterministicIds()
    {
        var tokenizer = CharTokenizer.FromVocabulary(new[] { 'x', 'y', 'z' });

        tokenizer.Encode("zxy").Should().Equal(2, 0, 1);
    }
}
