using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

public sealed record RagListOptions(int Page, int PageSize, RagOutputFormat Format);

public sealed class RagListComponent(IRagIndex index)
{
    public int Run(RagListOptions options) => RagValidation.Run(() =>
    {
        var error = RagValidation.Paging(options.Page, options.PageSize);
        if (error is not null) return RagOutputRenderer.WriteError(error);
        RagOutputRenderer.RenderList(options.Format, index.IndexPath, index.List(options.Page, options.PageSize));
        return 0;
    });
}
