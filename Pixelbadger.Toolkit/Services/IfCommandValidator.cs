using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Validates commands sent to the interpreter, for both the model and the human.</summary>
public static partial class IfCommandValidator
{
    public const int MaxLength = 200;

    private static readonly HashSet<string> MetaVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "save", "restore", "quit", "q", "restart", "script", "unscript",
        "transcript", "recording", "replay"
    };

    /// <summary>Returns null when valid, else the error message.</summary>
    public static string? Validate(string? command, bool isHuman)
    {
        var trimmed = command?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "Command cannot be empty.";
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
            return "Command must be a single line.";
        if (trimmed.Length > MaxLength)
            return $"Command is longer than {MaxLength} characters.";
        if (trimmed[0] == '\\')
            return "Commands may not start with a backslash.";

        foreach (var clause in ClauseSeparator().Split(trimmed))
        {
            var firstWord = clause.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim(PunctuationChars);
            if (firstWord != null && MetaVerbs.Contains(firstWord))
            {
                return isHuman
                    ? "Use /save, /restore or /quit."
                    : "Use the save/restore slot tools instead of in-game save, restore, quit or restart.";
            }
        }

        return null;
    }

    private static readonly char[] PunctuationChars = [.. "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"];

    [GeneratedRegex(@"[.,;!?]|\bthen\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseSeparator();
}
