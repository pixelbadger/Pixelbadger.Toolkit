using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;

namespace Pixelbadger.Toolkit.Commands;

public static class RagCommand
{
    public static Command Create()
    {
        var command = new Command("rag", "Chunk and index plain-text files into a Lucene.NET index, then list, search, get and delete them");
        command.Add(CreateIngest());
        command.Add(CreateList());
        command.Add(CreateGet());
        command.Add(CreateSearch());
        command.Add(CreateDelete());
        return command;
    }

    private sealed record Common(Option<string> Index, Option<string?> IndexPath, Option<RagOutputFormat> Format);

    private static Common AddCommon(Command command)
    {
        var common = new Common(
            new Option<string>("--index") { Description = "Index name; stored under ~/.pbtk/rag/<name>", DefaultValueFactory = _ => RagIndexLocator.DefaultIndexName },
            new Option<string?>("--index-path") { Description = "Directory of the index; overrides --index" },
            new Option<RagOutputFormat>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => RagOutputFormat.text });
        command.Add(common.Index);
        command.Add(common.IndexPath);
        command.Add(common.Format);
        return common;
    }

    private static int WithIndex(ParseResult parseResult, Common common, Func<IRagIndex, int> action)
    {
        string path;
        var name = parseResult.GetValue(common.Index);
        try
        {
            path = RagIndexLocator.Resolve(name, parseResult.GetValue(common.IndexPath));
        }
        catch (RagIndexPathException ex)
        {
            return RagOutputRenderer.WriteError(ex.Message);
        }
        catch (ArgumentException)
        {
            return RagOutputRenderer.WriteError($"Invalid index name '{name}'.");
        }

        try
        {
            return action(new LuceneRagIndex(path));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RagOutputRenderer.WriteError(ex.Message);
        }
    }

    private static Command CreateIngest()
    {
        var command = new Command("ingest", "Chunk a plain-text file and add it to the index, or replace an existing document with --id");
        var file = new Option<string>("--file") { Description = "Plain-text file to ingest (UTF-8, up to 50 MB)", Required = true };
        var id = new Option<string?>("--id") { Description = "Replace the document with this ID (32 hex characters), keeping the ID" }.WithImmutableDefault();
        var title = new Option<string?>("--title") { Description = "Document title (default: the file name; an update keeps the existing title)" }.WithImmutableDefault();
        var chunkSize = new Option<int>("--chunk-size") { Description = "Maximum characters per chunk (100 to 100000)", DefaultValueFactory = _ => 1000 };
        var chunkOverlap = new Option<int>("--chunk-overlap") { Description = "Characters of preceding text repeated at the start of each chunk (0 to half of --chunk-size)", DefaultValueFactory = _ => 0 };
        command.Add(file);
        command.Add(id);
        command.Add(title);
        command.Add(chunkSize);
        command.Add(chunkOverlap);
        var common = AddCommon(command);

        command.SetAction((parseResult, cancellationToken) => Task.FromResult(WithIndex(parseResult, common, index =>
            new RagIngestComponent(index, new ParagraphTextChunker(), TimeProvider.System).Run(new RagIngestOptions(
                parseResult.GetValue(file)!, parseResult.GetValue(id), parseResult.GetValue(title),
                parseResult.GetValue(chunkSize), parseResult.GetValue(chunkOverlap), parseResult.GetValue(common.Format)), cancellationToken))));
        return command;
    }

    private static Command CreateList()
    {
        var command = new Command("list", "List the documents in the index, oldest first, one page at a time");
        var page = new Option<int>("--page") { Description = "Page number, starting at 1", DefaultValueFactory = _ => 1 };
        var pageSize = new Option<int>("--page-size") { Description = "Documents per page (1 to 100)", DefaultValueFactory = _ => 20 };
        command.Add(page);
        command.Add(pageSize);
        var common = AddCommon(command);

        command.SetAction(parseResult => WithIndex(parseResult, common, index =>
            new RagListComponent(index).Run(new RagListOptions(parseResult.GetValue(page), parseResult.GetValue(pageSize), parseResult.GetValue(common.Format)))));
        return command;
    }

    private static Command CreateGet()
    {
        var command = new Command("get", "Show a document's details and its chunks (all, or a range with --from-chunk and --to-chunk)");
        var id = new Option<string>("--id") { Description = "Document ID (32 hex characters)", Required = true };
        var fromChunk = new Option<int?>("--from-chunk") { Description = "First chunk to return, 0-based and inclusive (default: 0)" }.WithImmutableDefault();
        var toChunk = new Option<int?>("--to-chunk") { Description = "Last chunk to return, 0-based and inclusive (default: the last chunk)" }.WithImmutableDefault();
        command.Add(id);
        command.Add(fromChunk);
        command.Add(toChunk);
        var common = AddCommon(command);

        command.SetAction(parseResult => WithIndex(parseResult, common, index =>
            new RagGetComponent(index).Run(new RagGetOptions(parseResult.GetValue(id)!, parseResult.GetValue(common.Format),
                parseResult.GetValue(fromChunk), parseResult.GetValue(toChunk)))));
        return command;
    }

    private static Command CreateSearch()
    {
        var command = new Command("search", "Search chunks for plain-text terms, ranked by BM25 (any term can match; stemming and stop words apply)");
        var query = new Option<string>("--query") { Description = "Plain text to search for; every term is analysed and none is query syntax", Required = true };
        var page = new Option<int>("--page") { Description = "Page number, starting at 1", DefaultValueFactory = _ => 1 };
        var pageSize = new Option<int>("--page-size") { Description = "Results per page (1 to 100)", DefaultValueFactory = _ => 10 };
        command.Add(query);
        command.Add(page);
        command.Add(pageSize);
        var common = AddCommon(command);

        command.SetAction(parseResult => WithIndex(parseResult, common, index =>
            new RagSearchComponent(index).Run(new RagSearchOptions(parseResult.GetValue(query)!, parseResult.GetValue(page),
                parseResult.GetValue(pageSize), parseResult.GetValue(common.Format)))));
        return command;
    }

    private static Command CreateDelete()
    {
        var command = new Command("delete", "Delete a document and all of its chunks from the index");
        var id = new Option<string>("--id") { Description = "Document ID (32 hex characters)", Required = true };
        command.Add(id);
        var common = AddCommon(command);

        command.SetAction((parseResult, cancellationToken) => Task.FromResult(WithIndex(parseResult, common, index =>
            new RagDeleteComponent(index).Run(new RagDeleteOptions(parseResult.GetValue(id)!, parseResult.GetValue(common.Format)), cancellationToken))));
        return command;
    }
}
