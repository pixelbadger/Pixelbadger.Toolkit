using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

public sealed record RagGetOptions(string Id, RagOutputFormat Format);

public sealed class RagGetComponent(IRagIndex index)
{
    public int Run(RagGetOptions options) => RagValidation.Run(() =>
    {
        var error = RagValidation.Id(options.Id, out var id);
        if (error is not null) return RagOutputRenderer.WriteError(error);
        var document = index.Get(id);
        if (document is null) return RagOutputRenderer.WriteError($"Document '{id}' not found.");
        RagOutputRenderer.RenderGet(options.Format, document);
        return 0;
    });
}
