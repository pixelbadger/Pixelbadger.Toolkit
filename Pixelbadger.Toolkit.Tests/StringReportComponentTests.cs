using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class StringReportComponentTests : IDisposable
{
    private readonly StringReportComponent _component;
    private readonly string _testDirectory;

    public StringReportComponentTests()
    {
        _component = new StringReportComponent();
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnZeroedResult_WhenInputIsWhitespace()
    {
        var result = _component.AnalyzeText("   \n\t  ");

        result.Characters.Should().Be(0);
        result.Words.Should().Be(0);
        result.Sentences.Should().Be(0);
        result.Paragraphs.Should().Be(0);
        result.ReadabilityBand.Should().Be("N/A (empty input)");
        result.LongestWord.Should().BeEmpty();
        result.MostCommonWord.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectCharacterCounts_WhenSingleSentenceProvided()
    {
        // "The cat sat on the mat." = 23 chars total, 18 without spaces (17 letters + 1 period)
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.Characters.Should().Be(23);
        result.CharactersNoSpaces.Should().Be(18);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectWordCount_WhenSingleSentenceProvided()
    {
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.Words.Should().Be(6);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectUniqueWordCount_WhenRepeatedWordsPresent()
    {
        // "the" appears twice; unique words are: the, cat, sat, on, mat = 5
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.UniqueWords.Should().Be(5);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnOneSentence_WhenSingleSentenceProvided()
    {
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.Sentences.Should().Be(1);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectSentenceCount_WhenMultipleSentencesProvided()
    {
        var result = _component.AnalyzeText("Hello world. How are you? Fine!");

        result.Sentences.Should().Be(3);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnOneParagraph_WhenNoBlanklinesPresent()
    {
        var result = _component.AnalyzeText("One sentence. Two sentences.");

        result.Paragraphs.Should().Be(1);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectParagraphCount_WhenBlankLinesPresent()
    {
        var result = _component.AnalyzeText("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.");

        result.Paragraphs.Should().Be(3);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectAverageWordsPerSentence_WhenMultipleSentencesProvided()
    {
        // "Hello world." = 2 words, 1 sentence → avg 2.0
        // "How are you?" = 3 words, 1 sentence → combined 5 words / 2 sentences = 2.5
        var result = _component.AnalyzeText("Hello world. How are you?");

        result.AverageWordsPerSentence.Should().Be(2.5);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectAverageSentencesPerParagraph_WhenMultipleParagraphsProvided()
    {
        // Para1: "One. Two." = 2 sentences; Para2: "Three." = 1 sentence → avg = 1.5
        var result = _component.AnalyzeText("One. Two.\n\nThree.");

        result.AverageSentencesPerParagraph.Should().Be(1.5);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnEstimatedPageCountOfOne_WhenWordCountIsUnder250()
    {
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.EstimatedPages.Should().Be(1);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnEstimatedPageCountOfTwo_WhenWordCountIsOver250()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 251));
        var result = _component.AnalyzeText(words);

        result.EstimatedPages.Should().Be(2);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnCorrectReadingTime_WhenSixWordTextProvided()
    {
        // 6 words / 238 wpm * 60s = 1.51… → ceiling = 2s
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.EstimatedReadingTimeSeconds.Should().Be(2);
    }

    [Fact]
    public void AnalyzeText_ShouldReturnLongestWord_WhenMultipleWordsPresent()
    {
        var result = _component.AnalyzeText("The extraordinary cat sat.");

        result.LongestWord.Should().Be("extraordinary");
    }

    [Fact]
    public void AnalyzeText_ShouldReturnMostCommonWord_WhenWordAppearsMultipleTimes()
    {
        // "the" appears 3 times; "cat" appears once
        var result = _component.AnalyzeText("The cat and the dog and the fish.");

        result.MostCommonWord.Should().Be("the");
    }

    [Fact]
    public void AnalyzeText_ShouldReturnFleschReadingEaseScore_WhenTextIsProvided()
    {
        var result = _component.AnalyzeText("The cat sat on the mat.");

        result.FleschReadingEase.Should().Be(100);
        result.ReadabilityBand.Should().Be("Very easy");
    }

    [Fact]
    public async Task AnalyzeFileAsync_ShouldReturnResult_WhenValidTextFileProvided()
    {
        var inputFile = Path.Combine(_testDirectory, "sample.txt");
        await File.WriteAllTextAsync(inputFile, "The cat sat on the mat.");

        var result = await _component.AnalyzeFileAsync(inputFile);

        result.Words.Should().Be(6);
        result.Sentences.Should().Be(1);
    }

    [Fact]
    public async Task AnalyzeFileAsync_ShouldThrowFileNotFoundException_WhenInputFileDoesNotExist()
    {
        var inputFile = Path.Combine(_testDirectory, "missing.txt");

        var act = async () => await _component.AnalyzeFileAsync(inputFile);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Input file '{inputFile}' does not exist.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeFileAsync_ShouldThrowArgumentException_WhenInputFilePathIsNullOrEmpty(string? inputFilePath)
    {
        var act = async () => await _component.AnalyzeFileAsync(inputFilePath!);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Input file path cannot be null or empty.*");
    }

    [Fact]
    public void AnalyzeText_ShouldTreatTextWithoutSentenceTerminators_AsSingleSentence()
    {
        var result = _component.AnalyzeText("Simple text without punctuation");

        result.Sentences.Should().Be(1);
    }

    [Fact]
    public void AnalyzeText_ShouldPopulateAllFields_WhenMultiParagraphTextProvided()
    {
        // "Hello world. How are you?\n\nI am fine. Thank you very much."
        // 58 chars total, 46 non-whitespace, 12 words, 11 unique, 4 sentences, 2 paragraphs
        // avg 3.0 words/sentence, 2.0 sentences/paragraph, 1 page
        // 12/238*60 = 3.025 → ceiling = 4s reading time
        // Flesch score > 100 → clamped to 100.0 "Very easy"
        // longest word = "hello" (5 chars, first in order), most common = "you" (2 occurrences)
        var text = "Hello world. How are you?\n\nI am fine. Thank you very much.";

        var result = _component.AnalyzeText(text);

        result.Characters.Should().Be(58);
        result.CharactersNoSpaces.Should().Be(46);
        result.Words.Should().Be(12);
        result.UniqueWords.Should().Be(11);
        result.Sentences.Should().Be(4);
        result.Paragraphs.Should().Be(2);
        result.AverageWordsPerSentence.Should().Be(3.0);
        result.AverageSentencesPerParagraph.Should().Be(2.0);
        result.EstimatedPages.Should().Be(1);
        result.EstimatedReadingTimeSeconds.Should().Be(4);
        result.FleschReadingEase.Should().Be(100);
        result.ReadabilityBand.Should().Be("Very easy");
        result.LongestWord.Should().Be("hello");
        result.MostCommonWord.Should().Be("you");
    }

    [Fact]
    public void AnalyzeText_ShouldNotOvercountParagraphs_WhenTextHasTrailingBlankLines()
    {
        var result = _component.AnalyzeText("First paragraph.\n\nSecond paragraph.\n\n");

        result.Paragraphs.Should().Be(2);
    }
}
