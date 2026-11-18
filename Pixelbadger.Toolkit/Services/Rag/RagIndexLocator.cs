using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services.Rag;

public static class RagIndexLocator
{
    public const string DefaultIndexName = "default";
    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>An explicit path wins over the name; otherwise the name maps to ~/.pbtk/rag/&lt;name&gt;.</summary>
    /// <exception cref="ArgumentException">The index name is invalid.</exception>
    /// <exception cref="RagIndexPathException">The index path is invalid or the home directory is unknown.</exception>
    public static string Resolve(string? indexName, string? indexPath, string? homeDirectory = null)
    {
        if (indexPath is not null)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new RagIndexPathException($"Invalid index path '{indexPath}'.");
            try
            {
                return Path.GetFullPath(indexPath);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                throw new RagIndexPathException($"Invalid index path '{indexPath}'.", ex);
            }
        }

        var name = string.IsNullOrEmpty(indexName) ? DefaultIndexName : indexName;
        if (!NamePattern.IsMatch(name))
            throw new ArgumentException(
                $"Invalid index name '{name}'. Use 1-64 characters: letters, digits, '.', '_' or '-', starting with a letter or digit.",
                nameof(indexName));

        var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            throw new RagIndexPathException("Cannot determine the home directory; use --index-path.");
        return Path.Combine(home, ".pbtk", "rag", name);
    }
}
