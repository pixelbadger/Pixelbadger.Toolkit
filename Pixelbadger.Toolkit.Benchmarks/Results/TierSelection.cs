using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public static class TierSelection
{
    public const string SlowCategory = "slow";

    /// <summary>Public, non-abstract classes with at least one [Benchmark] method.</summary>
    public static IReadOnlyList<Type> BenchmarkTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsVisible: true } &&
                        t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Any(m => m.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    public static bool IsSlow(Type type) =>
        type.GetCustomAttributes<BenchmarkCategoryAttribute>(inherit: true)
            .Any(a => a.Categories.Contains(SlowCategory, StringComparer.OrdinalIgnoreCase));

    /// <summary>Slow tier = classes carrying [BenchmarkCategory("slow")]; standard = every other benchmark class.</summary>
    public static IReadOnlyList<Type> Select(IEnumerable<Type> benchmarkTypes, bool slow) =>
        benchmarkTypes.Where(t => IsSlow(t) == slow).ToList();

    public static string TierName(bool slow) => slow ? "slow" : "standard";
}
