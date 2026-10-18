using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Pixelbadger.Toolkit.CommandLine;

/// <summary>
/// Marks commands that must never be exposed as MCP tools. Marking a command excludes its whole subtree.
/// Implemented as a marker table like <see cref="ImmutableDefaultExtensions"/>.
/// </summary>
public static class McpExposureExtensions
{
    private static readonly ConditionalWeakTable<Command, object> Marked = new();
    private static readonly object Marker = new();

    /// <summary>Marks <paramref name="command"/> (and so all its descendants) as not exposable over MCP.</summary>
    public static TCommand ExcludeFromMcp<TCommand>(this TCommand command) where TCommand : Command
    {
        ArgumentNullException.ThrowIfNull(command);
        Marked.AddOrUpdate(command, Marker);
        return command;
    }

    /// <summary>True if the command or any of its ancestors is marked.</summary>
    public static bool IsExcludedFromMcp(this Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Symbol? current = command;
        while (current is Command c)
        {
            if (Marked.TryGetValue(c, out _)) return true;
            current = c.Parents.FirstOrDefault();
        }
        return false;
    }
}
