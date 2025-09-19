using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Components;

public class AbjadifyComponent
{
    private static readonly HashSet<char> Vowels = new HashSet<char> { 'a', 'e', 'i', 'o', 'u', 'A', 'E', 'I', 'O', 'U' };
    private static readonly HashSet<string> SingleVowelWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "A", "i", "I" };

    public async Task AbjadifyFileAsync(string inputFilePath, string outputFilePath)
    {
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException($"Input file '{inputFilePath}' does not exist.");
        }

        var content = await File.ReadAllTextAsync(inputFilePath);
        var abjadifiedContent = AbjadifyText(content);
        await File.WriteAllTextAsync(outputFilePath, abjadifiedContent);
    }

    public string AbjadifyText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = Regex.Split(text, @"(\W+)");
        var result = new List<string>();

        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word) || !char.IsLetter(word[0]))
            {
                result.Add(word);
                continue;
            }

            var processedWord = ProcessWord(word);
            result.Add(processedWord);
        }

        return string.Concat(result);
    }

    private string ProcessWord(string word)
    {
        if (SingleVowelWords.Contains(word))
            return word;

        return RemoveVowels(word);
    }

    private string RemoveVowels(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        var result = new List<char>();

        for (int i = 0; i < word.Length; i++)
        {
            if (!Vowels.Contains(word[i]))
            {
                result.Add(word[i]);
            }
        }

        return new string(result.ToArray());
    }
}