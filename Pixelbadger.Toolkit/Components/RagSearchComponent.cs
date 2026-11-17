using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Components;

public sealed record RagSearchOptions(string Query, int Page, int PageSize, RagOutputFormat Format);

public sealed class RagSearchComponent(IRagIndex index)
{
    public int Run(RagSearchOptions options) => RagValidation.Run(() =>
    {
        var error = RagValidation.Paging(options.Page, options.PageSize);
        if (error is not null) return RagOutputRenderer.WriteError(error);
        RagOutputRenderer.RenderSearch(options.Format, options.Query, index.Search(options.Query, options.Page, options.PageSize));
        return 0;
    });
}
