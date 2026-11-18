namespace Pixelbadger.Toolkit.Services.Rag;

public static class RagIds
{
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Accepts exactly 32 hex characters in any case and returns them lowercased.</summary>
    public static bool TryNormalize(string? input, out string id)
    {
        id = string.Empty;
        if (input is null || input.Length != 32)
            return false;
        foreach (var c in input)
        {
            var hex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex)
                return false;
        }
        id = input.ToLowerInvariant();
        return true;
    }
}
