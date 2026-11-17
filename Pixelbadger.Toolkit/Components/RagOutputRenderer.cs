using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Services.Rag;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

/// <summary>Renders rag action results as Spectre text (user content escaped) or one compact JSON line.</summary>
public static class RagOutputRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public sealed record IngestJson(string Id, string Action, string Title, string SourcePath, int ChunkCount, int CharCount);

    public sealed record DocumentJson(string Id, string Title, string FileName, string SourcePath, string CreatedUtc, string UpdatedUtc,
        int ChunkCount, int CharCount, string Sha256);

    public sealed record ListJson(string Index, int Page, int PageSize, int Total, int TotalPages, IReadOnlyList<DocumentJson> Documents);

    public sealed record ChunkJson(int Index, int Start, int End, string Content);

    public sealed record GetJson(string Id, string Title, string FileName, string SourcePath, string CreatedUtc, string UpdatedUtc,
        int ChunkCount, int CharCount, string Sha256, IReadOnlyList<ChunkJson> Chunks);

    public sealed record ResultJson(string Id, string Title, int ChunkIndex, float Score, int Start, int End, string Content);

    public sealed record SearchJson(string Query, int Page, int PageSize, int Total, int TotalPages, IReadOnlyList<ResultJson> Results);

    public sealed record DeleteJson(string Id, bool Deleted);

    /// <summary>Errors are plain text on stderr whatever the format.</summary>
    public static int WriteError(string message)
    {
        ToolkitConsole.Error.WriteLine($"Error: {message}");
        return 1;
    }

    public static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static void WriteJson<T>(T value) => ToolkitConsole.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    // Redirected output has no real terminal width; stop Spectre wrapping user content mid-line.
    private static void Line(string markup)
    {
        if (!ToolkitConsole.IsCaptured && Console.IsOutputRedirected && AnsiConsole.Profile.Width < 4096)
            AnsiConsole.Profile.Width = 4096;
        AnsiConsole.MarkupLine(markup);
    }

    private static string E(string value) => Markup.Escape(value);

    private static DocumentJson ToJson(RagDocumentHeader h) =>
        new(h.Id, h.Title, h.FileName, h.SourcePath, Iso(h.CreatedUtc), Iso(h.UpdatedUtc), h.ChunkCount, h.CharCount, h.Sha256);

    public static void RenderIngest(RagOutputFormat format, string action, RagDocumentHeader header)
    {
        if (format == RagOutputFormat.json)
        {
            WriteJson(new IngestJson(header.Id, action, header.Title, header.SourcePath, header.ChunkCount, header.CharCount));
            return;
        }
        var verb = action == "created" ? "Created" : "Updated";
        Line($"{verb} {E(header.Id)} from '{E(header.SourcePath)}' ({header.ChunkCount} chunks).");
    }

    public static void RenderList(RagOutputFormat format, string indexPath, RagPage<RagDocumentHeader> page)
    {
        if (format == RagOutputFormat.json)
        {
            WriteJson(new ListJson(indexPath, page.Page, page.PageSize, page.Total, page.TotalPages, page.Items.Select(ToJson).ToList()));
            return;
        }
        if (page.Total == 0)
        {
            Line("No documents.");
            return;
        }
        foreach (var h in page.Items)
            Line($"{E(h.Id)}  {E(h.Title)}  {h.ChunkCount} chunks  {E(Iso(h.CreatedUtc))}");
        Line($"Page {page.Page} of {page.TotalPages} ({page.Total} documents)");
    }

    public static void RenderGet(RagOutputFormat format, RagDocument document)
    {
        var h = document.Header;
        if (format == RagOutputFormat.json)
        {
            var d = ToJson(h);
            WriteJson(new GetJson(d.Id, d.Title, d.FileName, d.SourcePath, d.CreatedUtc, d.UpdatedUtc, d.ChunkCount, d.CharCount, d.Sha256,
                document.Chunks.Select(c => new ChunkJson(c.Index, c.Start, c.End, c.Content)).ToList()));
            return;
        }
        Line($"Id: {E(h.Id)}");
        Line($"Title: {E(h.Title)}");
        Line($"File: {E(h.FileName)}");
        Line($"Source: {E(h.SourcePath)}");
        Line($"Created: {E(Iso(h.CreatedUtc))}");
        Line($"Updated: {E(Iso(h.UpdatedUtc))}");
        Line($"Chunks: {h.ChunkCount}");
        Line($"Characters: {h.CharCount}");
        Line($"SHA-256: {E(h.Sha256)}");
        foreach (var c in document.Chunks)
        {
            Line($"--- chunk {c.Index} {E($"[{c.Start}-{c.End}]")} ---");
            Line(E(c.Content));
        }
    }

    public static void RenderSearch(RagOutputFormat format, string query, RagPage<RagSearchHit> page)
    {
        if (format == RagOutputFormat.json)
        {
            WriteJson(new SearchJson(query, page.Page, page.PageSize, page.Total, page.TotalPages,
                page.Items.Select(r => new ResultJson(r.Id, r.Title, r.ChunkIndex, r.Score, r.Start, r.End, r.Content)).ToList()));
            return;
        }
        if (page.Total == 0)
        {
            Line("No results.");
            return;
        }
        var rank = (page.Page - 1) * page.PageSize;
        foreach (var r in page.Items)
        {
            rank++;
            Line($"{rank}. {E(r.Id)} #{r.ChunkIndex} (score {r.Score.ToString("0.000", CultureInfo.InvariantCulture)}) {E(r.Title)}");
            Line(E(r.Content));
        }
        Line($"Page {page.Page} of {page.TotalPages} ({page.Total} results)");
    }

    public static void RenderDelete(RagOutputFormat format, string id)
    {
        if (format == RagOutputFormat.json)
        {
            WriteJson(new DeleteJson(id, true));
            return;
        }
        Line($"Deleted {E(id)}.");
    }
}
