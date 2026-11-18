using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

internal static class RagValidation
{
    public static string? Paging(int page, int pageSize)
    {
        if (page < 1) return "--page must be at least 1.";
        if (pageSize < 1 || pageSize > 100) return "--page-size must be between 1 and 100.";
        return null;
    }

    public static string? Id(string? input, out string id) =>
        RagIds.TryNormalize(input, out id) ? null : $"Invalid document ID '{input}'. Expected 32 hexadecimal characters.";

    /// <summary>Runs an action, turning any exception (lock, query, IO) into an error line and exit code 1.</summary>
    public static int Run(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (OperationCanceledException)
        {
            return RagOutputRenderer.WriteError("Cancelled.");
        }
        catch (Exception ex)
        {
            return RagOutputRenderer.WriteError(ex.Message);
        }
    }
}
