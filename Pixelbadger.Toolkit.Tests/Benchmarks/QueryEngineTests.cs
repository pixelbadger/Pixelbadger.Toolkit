using FluentAssertions;
using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class QueryEngineTests : TempDirTestBase
{
    private IReadOnlyList<StoredRun> Seed()
    {
        var device = BenchmarkTestData.Device();
        ResultStore.SaveRun(Root, device, BenchmarkTestData.Run(results: new[]
        {
            BenchmarkTestData.Result("A", "Fast", mean: 10.5),
            BenchmarkTestData.Result("A", "Slow", "N=2", mean: 2000) with { AllocatedBytes = null },
        }));
        ResultStore.SaveRun(Root, device, BenchmarkTestData.Run(version: "8.3.0", tier: "slow", dirty: true, job: "short",
            at: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), results: BenchmarkTestData.Result("C", "Heavy", mean: 5)));
        return ResultStore.LoadAll(Root);
    }

    [Fact]
    public void Execute_ShouldReturnHeaderAndTabSeparatedRows_WhenQueryingResults()
    {
        var output = QueryEngine.Execute(Seed(), "select class, method, mean_ns from results order by class, method");

        output.Should().Be("class\tmethod\tmean_ns\nA\tFast\t10.5\nA\tSlow\t2000\nC\tHeavy\t5\n");
    }

    [Fact]
    public void Execute_ShouldExposeDeviceColumns()
    {
        var output = QueryEngine.Execute(Seed(), "select slug, os, os_description, arch, cpu, physical_cores, logical_cores, memory_gb, hypervisor from devices");

        output.Should().Be("slug\tos\tos_description\tarch\tcpu\tphysical_cores\tlogical_cores\tmemory_gb\thypervisor\n" +
                           $"{BenchmarkTestData.Slug}\tlinux\tUbuntu 24.04\tx64\tintel-xeon-2-80ghz\t2\t4\t16\t\n");
    }

    [Fact]
    public void Execute_ShouldExposeRunColumns_AndJoinToResults()
    {
        var output = QueryEngine.Execute(Seed(),
            "select r.version, r.tier, r.job, r.git_dirty, r.captured_at_utc, r.total_seconds, count(*) " +
            "from runs r join results x on x.run_id = r.id group by r.id order by r.version");

        output.Should().Be("version\ttier\tjob\tgit_dirty\tcaptured_at_utc\ttotal_seconds\tcount(*)\n" +
                           "8.2.0\tstandard\tmedium\t0\t2026-10-20T12:30:45Z\t12.3\t2\n" +
                           "8.3.0\tslow\tshort\t1\t2026-11-01T00:00:00Z\t12.3\t1\n");
    }

    [Fact]
    public void Execute_ShouldRenderNullAsEmpty_AndSuccessAsInteger()
    {
        var output = QueryEngine.Execute(Seed(), "select allocated_bytes, success from results where method = 'Slow'");

        output.Should().Be("allocated_bytes\tsuccess\n\t1\n");
    }

    [Fact]
    public void Execute_ShouldReturnHeaderOnly_WhenNoRowsMatch()
    {
        QueryEngine.Execute(Seed(), "select class from results where class = 'zzz'").Should().Be("class\n");
    }

    [Fact]
    public void Execute_ShouldReturnHeaderOnly_WhenNoRunsExist()
    {
        QueryEngine.Execute(Array.Empty<StoredRun>(), "select count(*) as n from runs").Should().Be("n\n0\n");
    }

    [Fact]
    public void Execute_ShouldThrowSqliteException_WhenSqlIsInvalid()
    {
        var runs = Seed();

        var act = () => QueryEngine.Execute(runs, "select nope from nowhere");

        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Execute_ShouldBeCultureInvariant_ForNumbers()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            QueryEngine.Execute(Seed(), "select mean_ns from results where method = 'Fast'").Should().Be("mean_ns\n10.5\n");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
