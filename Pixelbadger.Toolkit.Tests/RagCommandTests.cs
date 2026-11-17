using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.Commands;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>In-process parsing, option and validation tests of the rag topic (real index on temp paths).</summary>
public class RagCommandTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly InMemoryConfigStore _store = new();

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, true);
    }

    private string IndexPath => Path.Combine(_dir, "idx");

    private Task<(int Code, string Out, string Err)> Run(params string[] args) => McpCommandTestSupport.RunAsync(_store, args);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Create_ShouldRegisterFiveActionsWithCommonOptions_WhenBuilt()
    {
        var rag = RagCommand.Create();

        rag.Subcommands.Select(c => c.Name).Should().Equal("ingest", "list", "get", "search", "delete");
        foreach (var action in rag.Subcommands)
            action.Options.Select(o => o.Name).Should().Contain(["--index", "--index-path", "--format"]);
        Option O(string a, string o) => rag.Subcommands.Single(c => c.Name == a).Options.Single(x => x.Name == o);
        O("ingest", "--file").Required.Should().BeTrue();
        O("get", "--id").Required.Should().BeTrue();
        O("delete", "--id").Required.Should().BeTrue();
        O("search", "--query").Required.Should().BeTrue();
    }

    [Fact]
    public async Task Ingest_ShouldRoundTripThroughAllActions_WhenRunInProcess()
    {
        var file = Write("a.txt", "Lucene [red]markup[/] test.\n\nSecond paragraph about badgers.");

        var ingest = await Run("rag", "ingest", "--file", file, "--index-path", IndexPath, "--format", "json");
        ingest.Code.Should().Be(0, ingest.Err);
        var id = System.Text.Json.JsonDocument.Parse(ingest.Out).RootElement.GetProperty("id").GetString()!;

        (await Run("rag", "list", "--index-path", IndexPath)).Out.Should().Contain(id).And.Contain("Page 1 of 1 (1 documents)");
        (await Run("rag", "search", "--query", "badgers", "--index-path", IndexPath)).Out.Should().Contain("[red]markup[/]");
        (await Run("rag", "get", "--id", id, "--index-path", IndexPath)).Out.Should().Contain($"Id: {id}");
        var deleted = await Run("rag", "delete", "--id", id, "--index-path", IndexPath);
        deleted.Code.Should().Be(0);
        (await Run("rag", "get", "--id", id, "--index-path", IndexPath)).Code.Should().Be(1);
    }

    [Fact]
    public async Task Run_ShouldExit1WithParseError_WhenFormatInvalid()
    {
        var result = await Run("rag", "list", "--index-path", IndexPath, "--format", "xml");

        result.Code.Should().Be(1);
        Directory.Exists(IndexPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("-lead")]
    [InlineData("a/b")]
    public async Task Run_ShouldExit1_WhenIndexNameInvalid(string name)
    {
        var result = await Run("rag", "list", "--index", name);

        result.Code.Should().Be(1);
        result.Err.Should().Be($"Error: Invalid index name '{name}'.{Environment.NewLine}");
    }

    [Fact]
    public async Task Run_ShouldPreferIndexPathOverInvalidIndexName_WhenBothGiven()
    {
        var result = await Run("rag", "list", "--index", "bad name", "--index-path", IndexPath);

        result.Code.Should().Be(0);
        result.Out.Should().Contain("No documents.");
    }

    [Theory]
    [InlineData("list")]
    [InlineData("search")]
    [InlineData("get")]
    [InlineData("delete")]
    public async Task Run_ShouldNotCreateIndexDirectory_WhenActionIsReadOrFailingDelete(string action)
    {
        var extra = action switch
        {
            "search" => new[] { "--query", "x" },
            "get" or "delete" => new[] { "--id", new string('a', 32) },
            _ => Array.Empty<string>()
        };

        await Run(["rag", action, .. extra, "--index-path", IndexPath]);

        Directory.Exists(IndexPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("ingest", "--chunk-size", "50", "--chunk-size must be between 100 and 100000.")]
    [InlineData("ingest", "--chunk-size", "100001", "--chunk-size must be between 100 and 100000.")]
    [InlineData("ingest", "--chunk-overlap", "-1", "--chunk-overlap must be between 0 and half of --chunk-size.")]
    [InlineData("ingest", "--chunk-overlap", "501", "--chunk-overlap must be between 0 and half of --chunk-size.")]
    [InlineData("list", "--page", "0", "--page must be at least 1.")]
    [InlineData("list", "--page-size", "0", "--page-size must be between 1 and 100.")]
    [InlineData("list", "--page-size", "101", "--page-size must be between 1 and 100.")]
    [InlineData("search", "--page", "0", "--page must be at least 1.")]
    [InlineData("search", "--page-size", "101", "--page-size must be between 1 and 100.")]
    public async Task Run_ShouldExit1NamingOption_WhenRangeViolated(string action, string option, string value, string message)
    {
        var file = Write("a.txt", "text");
        string[] extra = action switch { "ingest" => ["--file", file], "search" => ["--query", "x"], _ => [] };

        var result = await Run(["rag", action, .. extra, option, value, "--index-path", IndexPath]);

        result.Code.Should().Be(1);
        result.Err.Should().Contain(message);
        Directory.Exists(IndexPath).Should().BeFalse();
    }

    [Fact]
    public async Task Run_ShouldExit1_WhenRequiredOptionMissing()
    {
        (await Run("rag", "ingest", "--index-path", IndexPath)).Code.Should().Be(1);
        (await Run("rag", "get", "--index-path", IndexPath)).Code.Should().Be(1);
        (await Run("rag", "delete", "--index-path", IndexPath)).Code.Should().Be(1);
        (await Run("rag", "search", "--index-path", IndexPath)).Code.Should().Be(1);
    }

    [Fact]
    public async Task Run_ShouldExit1WithInvalidQuery_WhenQuerySyntaxBroken()
    {
        await Run("rag", "ingest", "--file", Write("a.txt", "x y"), "--index-path", IndexPath);

        var result = await Run("rag", "search", "--query", "title:(", "--index-path", IndexPath);

        result.Code.Should().Be(1);
        result.Err.Should().Contain("Error: Invalid query");
    }
}
