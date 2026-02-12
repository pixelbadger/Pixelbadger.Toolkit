using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Components;

public class FleschReadingEaseComponent
{
    private static readonly Regex WordRegex = new Regex("[A-Za-z]+(?:'[A-Za-z]+)?", RegexOptions.Compiled);
    private static readonly Regex SentenceRegex = new Regex("[.!?]+", RegexOptions.Compiled);

    public async Task<FleschReadingEaseResult> AnalyzeFileAsync(string inputFilePath)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
        {
            throw new ArgumentException("Input file path cannot be null or empty.", nameof(inputFilePath));
        }

        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException($"Input file '{inputFilePath}' does not exist.");
        }

        var content = await File.ReadAllTextAsync(inputFilePath);
        return AnalyzeText(content);
    }

    public FleschReadingEaseResult AnalyzeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FleschReadingEaseResult(0, 0, 0, 0, "N/A (empty input)");
        }

        var words = WordRegex.Matches(text);
        var wordCount = words.Count;

        if (wordCount == 0)
        {
            return new FleschReadingEaseResult(0, 0, 0, 0, "N/A (empty input)");
        }

        var sentenceMatches = SentenceRegex.Matches(text);
        var sentenceCount = Math.Max(1, sentenceMatches.Count);

        var syllableCount = 0;
        foreach (Match word in words)
        {
            syllableCount += CountSyllables(word.Value);
        }

        var rawScore = 206.835
            - (1.015 * ((double)wordCount / sentenceCount))
            - (84.6 * ((double)syllableCount / wordCount));

        var clampedScore = Math.Clamp(rawScore, 0d, 100d);

        return new FleschReadingEaseResult(
            Math.Round(clampedScore, 2),
            sentenceCount,
            wordCount,
            syllableCount,
            GetReadabilityBand(clampedScore));
    }

    private static int CountSyllables(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return 0;
        }

        var normalized = new string(word.ToLowerInvariant().Where(char.IsLetter).ToArray());
        if (normalized.Length == 0)
        {
            return 0;
        }

        var syllables = 0;
        var previousWasVowel = false;

        foreach (var c in normalized)
        {
            var isVowel = IsVowel(c);
            if (isVowel && !previousWasVowel)
            {
                syllables++;
            }

            previousWasVowel = isVowel;
        }

        if (normalized.EndsWith("e") && !normalized.EndsWith("le") && syllables > 1)
        {
            syllables--;
        }

        return Math.Max(1, syllables);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static string GetReadabilityBand(double score)
    {
        if (score >= 90)
            return "Very easy";
        if (score >= 80)
            return "Easy";
        if (score >= 70)
            return "Fairly easy";
        if (score >= 60)
            return "Standard";
        if (score >= 50)
            return "Fairly difficult";
        if (score >= 30)
            return "Difficult";

        return "Very confusing";
    }
}

public record FleschReadingEaseResult(
    double Score,
    int Sentences,
    int Words,
    int Syllables,
    string ReadabilityBand);
