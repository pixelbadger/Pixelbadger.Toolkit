using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

public sealed record RagGetOptions(string Id, RagOutputFormat Format, int? FromChunk = null, int? ToChunk = null);

public sealed class RagGetComponent(IRagIndex index)
{
    public int Run(RagGetOptions options) => RagValidation.Run(() =>
    {
        var error = RagValidation.Id(options.Id, out var id);
        if (error is not null) return RagOutputRenderer.WriteError(error);
        if (options.FromChunk < 0) return RagOutputRenderer.WriteError("--from-chunk must be ≥ 0.");
        if (options.ToChunk < 0) return RagOutputRenderer.WriteError("--to-chunk must be ≥ 0.");
        if (options.FromChunk > options.ToChunk) return RagOutputRenderer.WriteError("--from-chunk must not be greater than --to-chunk.");
        var document = index.Get(id, options.FromChunk, options.ToChunk);
        if (document is null) return RagOutputRenderer.WriteError($"Document '{id}' not found.");
        RagOutputRenderer.RenderGet(options.Format, document);
        return 0;
    });
}
