using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class StringTokenPatternsTests
{
    [Theory]
    [InlineData("hello", new[] { "hello" })]
    [InlineData("Hello World", new[] { "Hello", "World" })]
    [InlineData("one two three", new[] { "one", "two", "three" })]
    public void WordRegex_ShouldMatchWords_WhenAlphabeticInputProvided(string input, string[] expectedMatches)
    {
        var matches = StringTokenPatterns.WordRegex.Matches(input);

        matches.Select(m => m.Value).Should().BeEquivalentTo(expectedMatches, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("456 789")]
    [InlineData("100")]
    public void WordRegex_ShouldNotMatchNumbers_WhenNumericInputProvided(string input)
    {
        var matches = StringTokenPatterns.WordRegex.Matches(input);

        matches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("can't", new[] { "can't" })]
    [InlineData("I'm fine", new[] { "I'm", "fine" })]
    [InlineData("don't won't", new[] { "don't", "won't" })]
    public void WordRegex_ShouldMatchContractionsAsOneToken_WhenApostrophePresent(string input, string[] expectedMatches)
    {
        var matches = StringTokenPatterns.WordRegex.Matches(input);

        matches.Select(m => m.Value).Should().BeEquivalentTo(expectedMatches, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("hello, world!", new[] { "hello", "world" })]
    [InlineData("end.", new[] { "end" })]
    [InlineData("(brackets)", new[] { "brackets" })]
    public void WordRegex_ShouldIgnorePunctuation_WhenMixedInputProvided(string input, string[] expectedMatches)
    {
        var matches = StringTokenPatterns.WordRegex.Matches(input);

        matches.Select(m => m.Value).Should().BeEquivalentTo(expectedMatches, o => o.WithStrictOrdering());
    }

    [Fact]
    public void WordRegex_ShouldReturnNoMatches_WhenInputIsEmpty()
    {
        var matches = StringTokenPatterns.WordRegex.Matches(string.Empty);

        matches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("word123", new[] { "word" })]
    [InlineData("123word", new[] { "word" })]
    public void WordRegex_ShouldMatchOnlyAlphabeticPart_WhenAlphanumericInputProvided(string input, string[] expectedMatches)
    {
        var matches = StringTokenPatterns.WordRegex.Matches(input);

        matches.Select(m => m.Value).Should().BeEquivalentTo(expectedMatches, o => o.WithStrictOrdering());
    }
}
