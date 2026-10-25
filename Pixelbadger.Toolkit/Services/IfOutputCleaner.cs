using System.Text;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Cleans raw dfrotz output down to the game text.</summary>
public static class IfOutputCleaner
{
    private const string SaveNoiseMarker = "Please enter a filename";

    public static string Clean(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var lines = rawOutput.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains(SaveNoiseMarker, StringComparison.Ordinal))
                break;

            var trimmedStart = line.TrimStart();
            if (trimmedStart.Length > 0 && trimmedStart.TrimEnd() == ">")
                continue;

            var text = line;
            if (text.StartsWith('>'))
                text = text[1..];
            text = text.TrimEnd();

            if (text.Length == 0 && result.Count > 0 && result[^1].Length == 0)
                continue;
            result.Add(text);
        }

        while (result.Count > 0 && result[0].Length == 0)
            result.RemoveAt(0);
        while (result.Count > 0 && result[^1].Length == 0)
            result.RemoveAt(result.Count - 1);

        return string.Join("\n", result);
    }
}
