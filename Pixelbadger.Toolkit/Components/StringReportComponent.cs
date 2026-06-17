using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Components;

public interface IStringReportComponent
{
    Task<StringReportResult> AnalyzeFileAsync(string inputFilePath);
    StringReportResult AnalyzeText(string text);
}

public class StringReportComponent : IStringReportComponent
{
    private static readonly Regex ParagraphRegex = new Regex(@"\n\s*\n", RegexOptions.Compiled);

    private readonly IFleschReadingEaseComponent _fleschComponent;

    public StringReportComponent() : this(new FleschReadingEaseComponent()) { }

    public StringReportComponent(IFleschReadingEaseComponent fleschComponent)
    {
        _fleschComponent = fleschComponent;
    }

    public async Task<StringReportResult> AnalyzeFileAsync(string inputFilePath)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
            throw new ArgumentException("Input file path cannot be null or empty.", nameof(inputFilePath));

        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException($"Input file '{inputFilePath}' does not exist.");

        var content = await File.ReadAllTextAsync(inputFilePath);
        return AnalyzeText(content);
    }

    public StringReportResult AnalyzeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StringReportResult(
                Characters: 0,
                CharactersNoSpaces: 0,
                Words: 0,
                UniqueWords: 0,
                Sentences: 0,
                Paragraphs: 0,
                AverageWordsPerSentence: 0,
                AverageSentencesPerParagraph: 0,
                EstimatedPages: 0,
                EstimatedReadingTimeSeconds: 0,
                FleschReadingEase: 0,
                ReadabilityBand: "N/A (empty input)",
                LongestWord: string.Empty,
                MostCommonWord: string.Empty);
        }

        var characterCount = text.Length;
        var characterCountNoSpaces = text.Count(c => !char.IsWhiteSpace(c));

        var wordMatches = StringTokenPatterns.WordRegex.Matches(text);
        var words = wordMatches.Select(m => m.Value.ToLowerInvariant()).ToList();
        var wordCount = words.Count;
        var uniqueWordCount = words.Distinct().Count();

        var flesch = _fleschComponent.AnalyzeText(text);
        var sentenceCount = flesch.Sentences;

        var paragraphCount = Math.Max(1, ParagraphRegex.Matches(text.Trim()).Count + 1);

        var averageWordsPerSentence = sentenceCount > 0 ? Math.Round((double)wordCount / sentenceCount, 1) : 0;
        var averageSentencesPerParagraph = Math.Round((double)sentenceCount / paragraphCount, 1);

        var estimatedPageCount = (int)Math.Ceiling((double)wordCount / 250);
        var estimatedReadingTimeSeconds = (int)Math.Ceiling((double)wordCount / 238 * 60);

        var longestWord = wordMatches.Count > 0
            ? wordMatches.OrderByDescending(m => m.Value.Length).First().Value.ToLowerInvariant()
            : string.Empty;

        var mostCommonWord = words.Count > 0
            ? words.GroupBy(w => w).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key
            : string.Empty;

        return new StringReportResult(
            Characters: characterCount,
            CharactersNoSpaces: characterCountNoSpaces,
            Words: wordCount,
            UniqueWords: uniqueWordCount,
            Sentences: sentenceCount,
            Paragraphs: paragraphCount,
            AverageWordsPerSentence: averageWordsPerSentence,
            AverageSentencesPerParagraph: averageSentencesPerParagraph,
            EstimatedPages: estimatedPageCount,
            EstimatedReadingTimeSeconds: estimatedReadingTimeSeconds,
            FleschReadingEase: flesch.Score,
            ReadabilityBand: flesch.ReadabilityBand,
            LongestWord: longestWord,
            MostCommonWord: mostCommonWord);
    }
}

public record StringReportResult(
    int Characters,
    int CharactersNoSpaces,
    int Words,
    int UniqueWords,
    int Sentences,
    int Paragraphs,
    double AverageWordsPerSentence,
    double AverageSentencesPerParagraph,
    int EstimatedPages,
    int EstimatedReadingTimeSeconds,
    double FleschReadingEase,
    string ReadabilityBand,
    string LongestWord,
    string MostCommonWord);
