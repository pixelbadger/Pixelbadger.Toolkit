using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Commands;

/// <summary>Reusable factory for the toolkit's root command. Each call builds a brand-new independent tree.</summary>
public static class ToolkitCommandTree
{
    public static RootCommand Create(IConfigStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var root = new RootCommand("CLI toolkit exposing varied functionality organized by topic");

        root.Add(StringsCommand.Create());
        root.Add(InterpretersCommand.Create());
        root.Add(ImagesCommand.Create());
        root.Add(WebCommand.Create());
        root.Add(SqliteCommand.Create());
        root.Add(LlmCommand.Create());
        root.Add(OAuthCommand.Create());
        root.Add(CryptoCommand.Create());
        root.Add(MarkovCommand.Create());
        root.Add(DemosceneCommand.Create());
        root.Add(GptCommand.Create());
        root.Add(AudioCommand.Create());
        root.Add(McpCommand.Create(store));
        // Further topics are registered above this line, before the config topic.

        // The config topic mirrors the finished tree, so it must be added last.
        ConfigBootstrap.AddConfigTopic(root, store);
        return root;
    }
}
