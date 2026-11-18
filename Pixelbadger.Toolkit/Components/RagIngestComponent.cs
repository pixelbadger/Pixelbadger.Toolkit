using System.Security.Cryptography;
using System.Text;
using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

public sealed record RagIngestOptions(string File, string? Id, string? Title, int ChunkSize, int ChunkOverlap, RagOutputFormat Format);

public sealed class RagIngestComponent(IRagIndex index, ITextChunker chunker, TimeProvider timeProvider, long maxFileBytes = RagIngestComponent.DefaultMaxFileBytes)
{
    public const long DefaultMaxFileBytes = 50L * 1024 * 1024;

    public int Run(RagIngestOptions options, CancellationToken cancellationToken = default) => RagValidation.Run(() =>
    {
        if (options.ChunkSize < 100 || options.ChunkSize > 100000)
            return RagOutputRenderer.WriteError("--chunk-size must be between 100 and 100000.");
        if (options.ChunkOverlap < 0 || (long)options.ChunkOverlap * 2 > options.ChunkSize)
            return RagOutputRenderer.WriteError("--chunk-overlap must be between 0 and half of --chunk-size.");

        string? id = null;
        if (options.Id is not null)
        {
            var idError = RagValidation.Id(options.Id, out var normalized);
            if (idError is not null) return RagOutputRenderer.WriteError(idError);
            id = normalized;
        }

        var info = new FileInfo(options.File);
        if (!info.Exists) return RagOutputRenderer.WriteError($"File '{options.File}' not found.");
        if (info.Length > maxFileBytes) return RagOutputRenderer.WriteError($"File '{options.File}' is larger than 50 MB.");

        var bytes = File.ReadAllBytes(info.FullName);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var text = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);
        if (text.Contains('\0')) return RagOutputRenderer.WriteError($"File '{options.File}' is not a plain text file.");
        if (string.IsNullOrWhiteSpace(text)) return RagOutputRenderer.WriteError($"File '{options.File}' contains no text.");

        var chunking = chunker.Chunk(text, options.ChunkSize, options.ChunkOverlap);
        if (chunking.Chunks.Count == 0) return RagOutputRenderer.WriteError($"File '{options.File}' contains no text.");

        var now = timeProvider.GetUtcNow();
        var created = now;
        string action;
        if (id is null)
        {
            id = RagIds.NewId();
            action = "created";
        }
        else
        {
            if (index.GetHeader(id) is null) return RagOutputRenderer.WriteError($"Document '{id}' not found.");
            action = "updated";
        }

        var fileName = Path.GetFileName(info.FullName);
        var header = new RagDocumentHeader(id, options.Title ?? fileName, fileName, Path.GetFullPath(options.File), created, now,
            chunking.Chunks.Count, chunking.NormalizedText.Length, sha256, options.ChunkSize, options.ChunkOverlap);
        var document = new RagDocument(header, chunking.Chunks);
        if (action == "updated")
        {
            var stored = index.Replace(document, cancellationToken);
            if (stored is null) return RagOutputRenderer.WriteError($"Document '{id}' not found.");
            header = stored;
        }
        else
        {
            index.Upsert(document, cancellationToken);
        }
        RagOutputRenderer.RenderIngest(options.Format, action, header);
        return 0;
    });
}
