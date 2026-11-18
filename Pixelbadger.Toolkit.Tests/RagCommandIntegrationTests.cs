using System.Text.Json;
using FluentAssertions;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>Runs the built tool as a process against temp index paths (and a temp HOME for --index).</summary>
public class RagCommandIntegrationTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => Directory.Delete(_dir, true);

    private string Index => Path.Combine(_dir, "idx");

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Task<(int ExitCode, string StandardOutput, string StandardError)> Run(params string[] args) =>
        ToolkitProcess.RunAsync(null, ["rag", .. args]);

    private async Task<string> IngestAsync(string file, params string[] extra)
    {
        var r = await Run(["ingest", "--file", file, "--index-path", Index, "--format", "json", .. extra]);
        r.ExitCode.Should().Be(0, r.StandardError);
        return JsonDocument.Parse(r.StandardOutput).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Ingest_ShouldCreateTwoDistinctDocuments_WhenSameFileIngestedTwice()
    {
        var file = Write("a.txt", "Alpha paragraph.\n\nBeta paragraph.");

        var first = await IngestAsync(file);
        var second = await IngestAsync(file);

        first.Should().MatchRegex("^[0-9a-f]{32}$");
        second.Should().NotBe(first);
        var get = await Run("get", "--id", first, "--index-path", Index, "--format", "json");
        get.ExitCode.Should().Be(0, get.StandardError);
        var root = JsonDocument.Parse(get.StandardOutput).RootElement;
        root.GetProperty("title").GetString().Should().Be("a.txt");
        root.GetProperty("sourcePath").GetString().Should().Be(file);
        root.GetProperty("chunkCount").GetInt32().Should().Be(1);
        root.GetProperty("charCount").GetInt32().Should().Be(33);
        root.GetProperty("sha256").GetString().Should().HaveLength(64);
    }

    [Fact]
    public async Task Ingest_ShouldReplaceContentKeepingIdAndCreated_WhenIdGiven()
    {
        var id = await IngestAsync(Write("a.txt", "oldterm here"), "--title", "First");
        var before = JsonDocument.Parse((await Run("get", "--id", id, "--index-path", Index, "--format", "json")).StandardOutput).RootElement.Clone();
        await Task.Delay(50);

        var upd = await Run("ingest", "--file", Write("b.txt", "newterm there"), "--id", id, "--index-path", Index, "--format", "json");

        upd.ExitCode.Should().Be(0, upd.StandardError);
        JsonDocument.Parse(upd.StandardOutput).RootElement.GetProperty("action").GetString().Should().Be("updated");
        var after = JsonDocument.Parse((await Run("get", "--id", id, "--index-path", Index, "--format", "json")).StandardOutput).RootElement;
        after.GetProperty("createdUtc").GetString().Should().Be(before.GetProperty("createdUtc").GetString());
        DateTimeOffset.Parse(after.GetProperty("updatedUtc").GetString()!).Should().BeAfter(DateTimeOffset.Parse(before.GetProperty("updatedUtc").GetString()!));
        (await Run("search", "--query", "oldterm", "--index-path", Index, "--format", "json")).StandardOutput.Should().Contain("\"total\":0");
        var found = await Run("search", "--query", "newterm", "--index-path", Index, "--format", "json");
        found.StandardOutput.Should().Contain("\"total\":1").And.Contain(id);
        after.GetProperty("title").GetString().Should().Be("First");
        var list = JsonDocument.Parse((await Run("list", "--index-path", Index, "--format", "json")).StandardOutput).RootElement;
        list.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Ingest_ShouldExit1AndLeaveIndexAbsent_WhenFileInvalidOrIdBad()
    {
        var nul = Write("nul.txt", "a\0b");
        var empty = Write("empty.txt", "  \n ");
        var cases = new (string[] Args, string Message)[]
        {
            (["--file", Path.Combine(_dir, "missing.txt")], "not found."),
            (["--file", nul], "is not a plain text file."),
            (["--file", empty], "contains no text."),
            (["--file", Write("ok.txt", "x"), "--id", "zzz"], "Invalid document ID 'zzz'"),
            (["--file", Write("ok2.txt", "x"), "--id", new string('b', 32)], "not found."),
        };

        foreach (var (args, message) in cases)
        {
            var r = await Run(["ingest", .. args, "--index-path", Index]);
            r.ExitCode.Should().Be(1, string.Join(' ', args));
            r.StandardError.Should().Contain(message);
        }
    }

    [Fact]
    public async Task List_ShouldPageTwentyFiveDocuments_WhenPageSizeTen()
    {
        var file = Write("a.txt", "text");
        var ids = new List<string>();
        for (var i = 0; i < 25; i++) ids.Add(await IngestAsync(file, "--title", $"T{i:00}"));

        foreach (var (page, count) in new[] { (1, 10), (2, 10), (3, 5), (4, 0) })
        {
            var r = await Run("list", "--index-path", Index, "--page", page.ToString(), "--page-size", "10", "--format", "json");
            r.ExitCode.Should().Be(0);
            var root = JsonDocument.Parse(r.StandardOutput).RootElement;
            root.GetProperty("total").GetInt32().Should().Be(25);
            root.GetProperty("totalPages").GetInt32().Should().Be(3);
            root.GetProperty("documents").GetArrayLength().Should().Be(count);
            if (page == 1)
                root.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetString()).Should().Equal(ids.Take(10));
        }
    }

    [Fact]
    public async Task Search_ShouldTreatQueryAsPlainTermsAndReturnOnlyChunks_WhenIndexed()
    {
        var id = await IngestAsync(Write("a.txt", "The quick brown fox jumps over the lazy dog"), "--title", "Animals");
        await IngestAsync(Write("b.txt", "Completely different words"), "--title", "Other");

        async Task<int> Total(string query)
        {
            var r = await Run("search", "--query", query, "--index-path", Index, "--format", "json");
            r.ExitCode.Should().Be(0, r.StandardError);
            return JsonDocument.Parse(r.StandardOutput).RootElement.GetProperty("total").GetInt32();
        }

        (await Total("fox")).Should().Be(1);
        (await Total("fox?")).Should().Be(1);
        (await Total("what's the \"brown fox\"?")).Should().Be(1);
        (await Total("fox dog")).Should().Be(1);
        (await Total("fox different")).Should().Be(2);
        (await Total("animals")).Should().Be(1);
        (await Total("kind:header")).Should().Be(0);
        (await Total("title:(")).Should().Be(0);
        var none = await Run("search", "--query", "how do I", "--index-path", Index);
        none.ExitCode.Should().Be(1);
        none.StandardError.Should().Contain("Query has no searchable terms.");
        id.Should().HaveLength(32);
    }

    [Fact]
    public async Task Delete_ShouldRemoveDocument_AndFailForUnknownId()
    {
        var id = await IngestAsync(Write("a.txt", "removable"));

        (await Run("delete", "--id", id, "--index-path", Index)).ExitCode.Should().Be(0);

        (await Run("get", "--id", id, "--index-path", Index)).ExitCode.Should().Be(1);
        (await Run("search", "--query", "removable", "--index-path", Index, "--format", "json")).StandardOutput.Should().Contain("\"total\":0");
        var again = await Run("delete", "--id", id, "--index-path", Index);
        again.ExitCode.Should().Be(1);
        again.StandardError.Should().Contain($"Document '{id}' not found.");
    }

    [Fact]
    public async Task Text_ShouldPrintMarkupLiterally_WhenFileContainsMarkup()
    {
        await IngestAsync(Write("m.txt", "markup [red]x[/] after"));

        var r = await Run("search", "--query", "markup", "--index-path", Index);

        r.ExitCode.Should().Be(0);
        r.StandardOutput.Should().Contain("markup [red]x[/] after");
    }

    [Fact]
    public async Task Index_ShouldResolveToHomeDirectory_WhenNameGivenAndPathWins()
    {
        var home = Path.Combine(_dir, "home");
        Directory.CreateDirectory(home);
        var env = new Dictionary<string, string> { ["HOME"] = home, ["USERPROFILE"] = home };
        var file = Write("a.txt", "homed text");

        var named = await ToolkitProcess.RunWithEnvironmentAsync(env, "rag", "ingest", "--file", file, "--index", "foo");
        named.ExitCode.Should().Be(0, named.StandardError);
        Directory.Exists(Path.Combine(home, ".pbtk", "rag", "foo")).Should().BeTrue();

        var explicitPath = Path.Combine(_dir, "explicit");
        var wins = await ToolkitProcess.RunWithEnvironmentAsync(env, "rag", "ingest", "--file", file, "--index", "foo", "--index-path", explicitPath);
        wins.ExitCode.Should().Be(0, wins.StandardError);
        Directory.Exists(explicitPath).Should().BeTrue();

        var missing = await ToolkitProcess.RunWithEnvironmentAsync(env, "rag", "list", "--index", "nothere");
        missing.ExitCode.Should().Be(0);
        Directory.Exists(Path.Combine(home, ".pbtk", "rag", "nothere")).Should().BeFalse();

        var bad = await ToolkitProcess.RunWithEnvironmentAsync(env, "rag", "list", "--index", "bad name");
        bad.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Json_ShouldBeSingleCompactLine_WhenFormatIsJsonForEveryAction()
    {
        var id = await IngestAsync(Write("a.txt", "hello"));
        foreach (var args in new[]
        {
            new[] { "list" }, new[] { "get", "--id", id }, new[] { "search", "--query", "hello" }, new[] { "delete", "--id", id }
        })
        {
            var r = await Run([.. args, "--index-path", Index, "--format", "json"]);
            r.ExitCode.Should().Be(0);
            r.StandardOutput.Trim().Split('\n').Should().HaveCount(1);
            JsonDocument.Parse(r.StandardOutput).RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        }
    }
}
