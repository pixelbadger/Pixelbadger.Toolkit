using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

public sealed record RagDeleteOptions(string Id, RagOutputFormat Format);

public sealed class RagDeleteComponent(IRagIndex index)
{
    public int Run(RagDeleteOptions options, CancellationToken cancellationToken = default) => RagValidation.Run(() =>
    {
        var error = RagValidation.Id(options.Id, out var id);
        if (error is not null) return RagOutputRenderer.WriteError(error);
        if (!index.Delete(id, cancellationToken)) return RagOutputRenderer.WriteError($"Document '{id}' not found.");
        RagOutputRenderer.RenderDelete(options.Format, id);
        return 0;
    });
}
