using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Pixelbadger.Toolkit.Tests;

public class ConfigCommandIntegrationTests : IDisposable
{
    private readonly string _configHome;
    private readonly string _workingDir;

    public ConfigCommandIntegrationTests()
    {
        _configHome = Path.Combine(Path.GetTempPath(), "pbtk-cfg-it-" + Guid.NewGuid().ToString("N"));
        _workingDir = Path.Combine(_configHome, "work");
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_configHome))
                Directory.Delete(_configHome, true);
        }
        catch (IOException)
        {
        }
    }

    private string DbPath => Path.Combine(_configHome, "pbtk", "config.db");

    private Task<(int ExitCode, string StandardOutput, string StandardError)> Run(params string[] args) =>
        ToolkitProcess.RunWithConfigHomeAsync(_workingDir, _configHome, args);

    // Returns the help entry (first line plus wrapped continuation lines) for the given option.
    private static string OptionEntry(string help, string option)
    {
        var lines = help.Replace("\r", "").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith(option + " ") || l.TrimStart() == option);
        start.Should().BeGreaterThanOrEqualTo(0, $"help should list {option}:\n{help}");
        var entry = new List<string> { lines[start] };
        for (var i = start + 1; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.Length == 0 || t.StartsWith("-") || !char.IsWhiteSpace(lines[i][0]) || (lines[i].Length - t.Length) <= (lines[start].Length - lines[start].TrimStart().Length))
                break;
            entry.Add(lines[i]);
        }
        return string.Join(" ", entry.Select(l => l.Trim()));
    }

    private async Task<string> HelpOf(params string[] args)
    {
        var r = await Run(args.Append("--help").ToArray());
        r.ExitCode.Should().Be(0, r.StandardOutput + r.StandardError);
        return r.StandardOutput;
    }

    private void Exec(string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static readonly string[] LlmActions = { "chat", "translate", "ocaaar", "corpospeak", "generate-image" };

    [Fact]
    public async Task ConfigLlmModel_ShouldSetDefaultForAllLlmActions_WhenSetAtTopicLevel()
    {
        var set = await Run("config", "llm", "--model", "claude-sonnet-5-5");
        set.ExitCode.Should().Be(0, set.StandardOutput + set.StandardError);

        foreach (var action in LlmActions)
        {
            var help = await HelpOf("llm", action);
            OptionEntry(help, "--model").Should().Contain("[default: claude-sonnet-5-5]", action);
        }
    }

    [Fact]
    public async Task ConfigLlmChatModel_ShouldOverrideOnlyChat_WhenSetAtActionLevelAndUnsetRestoresTopic()
    {
        (await Run("config", "llm", "--model", "claude-sonnet-5-5")).ExitCode.Should().Be(0);
        (await Run("config", "llm", "chat", "--model", "X")).ExitCode.Should().Be(0);

        OptionEntry(await HelpOf("llm", "chat"), "--model").Should().Contain("[default: X]");
        OptionEntry(await HelpOf("llm", "translate"), "--model").Should().Contain("[default: claude-sonnet-5-5]");

        var show = await Run("config", "llm", "chat");
        show.ExitCode.Should().Be(0);
        show.StandardOutput.Should().Contain("--model = X (from: llm chat)");

        (await Run("config", "llm", "chat", "--unset", "model")).ExitCode.Should().Be(0);

        OptionEntry(await HelpOf("llm", "chat"), "--model").Should().Contain("[default: claude-sonnet-5-5]");
        var show2 = await Run("config", "llm", "chat");
        show2.StandardOutput.Should().Contain("(from: llm)");
    }

    [Fact]
    public async Task ConfigLlmProvider_ShouldFailAndPersistNothing_WhenValueIsInvalid()
    {
        var bad = await Run("config", "llm", "--provider", "bogus");
        bad.ExitCode.Should().NotBe(0);
        (bad.StandardOutput + bad.StandardError).Should().Contain("bogus");

        var show = await Run("config", "llm");
        show.StandardOutput.Should().Contain("No configured defaults for llm");

        if (File.Exists(DbPath))
        {
            using var conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'llm'";
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(0);
        }
    }

    [Fact]
    public async Task ConfigGpt_ShouldApplyDefaultsOnlyToActionsWithTheOption_WhenSet()
    {
        (await Run("config", "gpt", "train", "--steps", "7")).ExitCode.Should().Be(0);
        OptionEntry(await HelpOf("gpt", "train"), "--steps").Should().Contain("[default: 7]");
        (await HelpOf("gpt", "complete")).Should().NotContain("--steps");

        (await Run("config", "gpt", "--seed", "42")).ExitCode.Should().Be(0);
        OptionEntry(await HelpOf("gpt", "train"), "--seed").Should().Contain("[default: 42]");
        OptionEntry(await HelpOf("gpt", "complete"), "--seed").Should().Contain("[default: 42]");
    }

    [Fact]
    public async Task ConfigHelp_ShouldOmitImmutableOptions_WhenShown()
    {
        (await HelpOf("config", "llm", "chat")).Should().NotContain("--session-id");
        (await HelpOf("config", "llm", "generate-image")).Should().NotContain("--overwrite");
        (await HelpOf("config", "gpt", "train")).Should().NotContain("--resume");
        (await HelpOf("config", "oauth", "profile", "update")).Should().NotContain("--client-secret");
        (await HelpOf("config", "oauth", "profile", "add")).Should().NotContain("--client-secret");

        var r = await Run("config", "llm", "chat", "--session-id", "5");
        r.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task WebServeHtmlHelp_ShouldShowConfiguredPort_WhenConfigured()
    {
        (await Run("config", "web", "serve-html", "--port", "9999")).ExitCode.Should().Be(0);
        OptionEntry(await HelpOf("web", "serve-html"), "--port").Should().Contain("[default: 9999]");
    }

    [Fact]
    public async Task GptComplete_ShouldFailOnlyItsUsers_WhenStoredValueIsCorrupt()
    {
        Exec("CREATE TABLE IF NOT EXISTS \"gpt\" (action TEXT NOT NULL, option TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (action, option))");
        Exec("INSERT INTO \"gpt\" (action, option, value) VALUES ('complete', 'max-tokens', '[\"abc\"]')");

        var r = await Run("gpt", "complete", "--prompt", "x", "--model", Path.Combine(_configHome, "no-such-model"));
        r.ExitCode.Should().NotBe(0);
        (r.StandardOutput + r.StandardError).Should().Contain("Configured default for gpt complete --max-tokens is invalid");

        (await Run("gpt", "train", "--help")).ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task LlmChatHelp_ShouldWarnAndSucceed_WhenStoredEntryIsUnknown()
    {
        Exec("CREATE TABLE IF NOT EXISTS \"llm\" (action TEXT NOT NULL, option TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (action, option))");
        Exec("INSERT INTO \"llm\" (action, option, value) VALUES ('', 'no-such-option', '[\"x\"]')");

        var r = await Run("llm", "chat", "--help");
        r.ExitCode.Should().Be(0);
        r.StandardError.Should().Contain("Warning: ignoring unknown config entry llm --no-such-option");
    }

    [Fact]
    public async Task Store_ShouldCreateOnlyTouchedTopicTables_WhenUsed()
    {
        (await Run("config", "gpt", "--seed", "1")).ExitCode.Should().Be(0);
        (await Run("llm", "chat", "--help")).ExitCode.Should().Be(0);

        using var conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        var tables = new List<string>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                tables.Add(reader.GetString(0));

        tables.Should().Contain("gpt");
        tables.Should().NotContain("llm");
    }

    [Fact]
    public async Task LlmChatHelp_ShouldReportErrorAndExitOne_WhenDatabaseIsUnreadable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var junk = new byte[4096];
        new Random(1234).NextBytes(junk);
        File.WriteAllBytes(DbPath, junk);

        var r = await Run("llm", "chat", "--help");
        r.ExitCode.Should().Be(1);
        r.StandardOutput.Should().Contain("Error:");
        r.StandardOutput.Should().Contain(DbPath);
    }

    [Fact]
    public async Task Config_ShouldPrintTopicHelpWithoutValues_WhenNoTopicGiven()
    {
        (await Run("config", "llm", "--model", "secret-model-name")).ExitCode.Should().Be(0);

        var r = await Run("config");
        var output = r.StandardOutput + r.StandardError;
        output.Should().Contain("llm");
        output.Should().NotContain("secret-model-name");
    }
}
