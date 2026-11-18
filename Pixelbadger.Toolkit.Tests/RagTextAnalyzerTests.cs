using FluentAssertions;
using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Tests;

public class RagTextAnalyzerTests
{
    [Fact]
    public void Tokenize_ShouldDropStopWordsAndPunctuation_WhenNaturalLanguageQuestion()
    {
        RagTextAnalyzer.Tokenize("What's the default for --port?").Should().Equal("default", "port");
    }

    [Fact]
    public void Tokenize_ShouldShareStem_WhenPluralAndHyphenated()
    {
        RagTextAnalyzer.Tokenize("numbers").Should().Equal("number");
        RagTextAnalyzer.Tokenize("line-number").Should().Equal("line", "number");
    }

    [Fact]
    public void Tokenize_ShouldKeepNegations_WhenNoNorNot()
    {
        RagTextAnalyzer.Tokenize("no nor not").Should().Equal("no", "nor", "not");
    }

    [Fact]
    public void Tokenize_ShouldLowerCaseAndStemAndStripPossessive_WhenMixedCase()
    {
        RagTextAnalyzer.Tokenize("The Badger's Running Quickly").Should().Equal("badger", "run", "quickli");
    }

    [Theory]
    [InlineData("how do I")]
    [InlineData("the of and to a")]
    [InlineData("?!.,")]
    [InlineData("")]
    public void Tokenize_ShouldReturnNothing_WhenOnlyStopWordsOrPunctuation(string text)
    {
        RagTextAnalyzer.Tokenize(text).Should().BeEmpty();
    }

    [Fact]
    public void StopWords_ShouldExcludeNegationsAndHold176Entries_WhenListed()
    {
        RagTextAnalyzer.StopWords.Should().NotContain(["no", "nor", "not"]);
        RagTextAnalyzer.StopWords.Should().HaveCount(176).And.OnlyHaveUniqueItems();
        RagTextAnalyzer.StopWords.Should().Contain(["the", "how", "do", "i", "what", "wouldn't"]);
    }
}
