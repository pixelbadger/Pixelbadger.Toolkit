using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Pixelbadger.Toolkit.CommandLine;

/// <summary>
/// Marks options whose default must never be persisted in per-user config (for example a session id or a
/// destructive flag). System.CommandLine 2.0.8 has no metadata bag on <see cref="Symbol"/>, hence this marker table.
/// </summary>
public static class ImmutableDefaultExtensions
{
    private static readonly ConditionalWeakTable<Option, object> Marked = new();
    private static readonly object Marker = new();

    /// <summary>Marks <paramref name="option"/> as not configurable and returns it for chaining.</summary>
    public static TOption WithImmutableDefault<TOption>(this TOption option) where TOption : Option
    {
        ArgumentNullException.ThrowIfNull(option);
        Marked.AddOrUpdate(option, Marker);
        return option;
    }

    public static bool HasImmutableDefault(this Option option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return Marked.TryGetValue(option, out _);
    }
}
