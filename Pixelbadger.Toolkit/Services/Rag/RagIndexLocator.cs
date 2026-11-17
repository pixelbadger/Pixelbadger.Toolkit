using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services.Rag;

public static class RagIndexLocator
{
    public const string DefaultIndexName = "default";
    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>An explicit path wins over the name; otherwise the name maps to ~/.pbtk/rag/&lt;name&gt;.</summary>
    /// <exception cref="ArgumentException">The index name is invalid.</exception>
    public static string Resolve(string? indexName, string? indexPath, string? homeDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(indexPath))
            return Path.GetFullPath(indexPath);

        var name = string.IsNullOrEmpty(indexName) ? DefaultIndexName : indexName;
        if (!NamePattern.IsMatch(name))
            throw new ArgumentException(
                $"Invalid index name '{name}'. Use 1-64 characters: letters, digits, '.', '_' or '-', starting with a letter or digit.",
                nameof(indexName));

        var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".pbtk", "rag", name);
    }
}
