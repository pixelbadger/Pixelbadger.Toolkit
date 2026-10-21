using System.Globalization;
using FluentAssertions;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class ReportGeneratorTests
{
    private static StoredRun Stored(DeviceRecord device, RunRecord run, string? file = null) =>
        new($"{device.Slug}/{run.ToolkitVersion}/{file ?? run.CapturedAtUtc.ToString("yyyyMMddTHHmmss") + "Z-" + run.Tier + ".json"}",
            device.Slug, run.ToolkitVersion, device, run);

    private static StoredRun Make(string version = "8.2.0", string tier = "standard", DateTime? at = null, double mean = 100.5,
        string slug = BenchmarkTestData.Slug, string cls = "LevenshteinBenchmarks", string method = "Short", string job = "medium") =>
        Stored(BenchmarkTestData.Device(slug),
            BenchmarkTestData.Run(version: version, tier: tier, at: at, job: job, results: BenchmarkTestData.Result(cls, method, "", mean)));

    [Fact]
    public void Render_ShouldBeIdentical_WhenRenderedTwice()
    {
        var runs = new[] { Make(), Make("8.3.0", at: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)) };

        ReportGenerator.Render(runs).Should().Be(ReportGenerator.Render(runs));
    }

    [Fact]
    public void Render_ShouldBeIdentical_UnderGermanAndInvariantCulture()
    {
        var runs = new[] { Make(mean: 0.5), Make("8.3.0", mean: 1234567.891, at: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)) };
        var original = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            var german = ReportGenerator.Render(runs);
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var invariant = ReportGenerator.Render(runs);

            german.Should().Be(invariant);
            invariant.Should().Contain("1.23 ms");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = original;
        }
    }

    [Fact]
    public void Render_ShouldBeIdentical_WhenInputOrderShuffled()
    {
        var a = Make("8.2.0");
        var b = Make("8.10.0", method: "Long");
        var c = Make("8.9.0", tier: "slow", cls: "HomomorphicStringBenchmarks");
        var d = Make(slug: "other-box");

        ReportGenerator.Render(new[] { a, b, c, d }).Should().Be(ReportGenerator.Render(new[] { d, c, b, a }));
    }

    [Fact]
    public void Render_ShouldUseLfEndingsAndNoTimestampOfGeneration()
    {
        var html = ReportGenerator.Render(new[] { Make() });

        html.Should().NotContain("\r");
        html.Should().Contain("<title>Pixelbadger Toolkit — Benchmark Report</title>");
        html.Should().Contain("href=\"styles.css\"").And.Contain("https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js");
    }

    [Fact]
    public void Render_ShouldShowEmptyMessageAndNoCharts_WhenNoRuns()
    {
        var html = ReportGenerator.Render(Array.Empty<StoredRun>());

        html.Should().Contain("No benchmark results recorded yet");
        var markup = html[..html.IndexOf("<script", StringComparison.Ordinal)];
        markup.Should().NotContain("<canvas");
        markup.Should().NotContain("device-select\"");
        html.Should().Contain("id=\"benchmark-data\"").And.Contain("</html>");
    }

    [Fact]
    public void Render_ShouldListEveryDeviceSlugSortedWithFirstSelected()
    {
        var html = ReportGenerator.Render(new[] { Make(slug: "zeta-box"), Make(slug: "alpha-box"), Make(slug: "mid-box") });

        var alpha = html.IndexOf("<option value=\"alpha-box\" selected>", StringComparison.Ordinal);
        var mid = html.IndexOf("<option value=\"mid-box\">", StringComparison.Ordinal);
        var zeta = html.IndexOf("<option value=\"zeta-box\">", StringComparison.Ordinal);
        alpha.Should().BeGreaterThan(0);
        mid.Should().BeGreaterThan(alpha);
        zeta.Should().BeGreaterThan(mid);
    }

    [Fact]
    public void Render_ShouldShowFingerprintOfSelectedDevice()
    {
        var html = ReportGenerator.Render(new[] { Make() });

        html.Should().Contain("CPU: intel-xeon-2-80ghz").And.Contain("2 physical / 4 logical").And.Contain("Memory: 16 GB").And.Contain("OS: linux");
    }

    [Fact]
    public void Render_ShouldTableOnlyLatestRunPerVersionAndTier()
    {
        var older = Make("8.2.0", at: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), mean: 111, method: "OldOnly");
        var newer = Make("8.2.0", at: new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), mean: 222, method: "NewOnly");

        var html = ReportGenerator.Render(new[] { newer, older });

        html.Should().Contain("<td>NewOnly</td>").And.NotContain("<td>OldOnly</td>");
        html.Should().Contain("captured 2026-10-02T00:00:00Z");
    }

    [Fact]
    public void Render_ShouldKeepTiersSeparate_StandardBeforeSlow()
    {
        var slow = Make(tier: "slow", cls: "HomomorphicStringBenchmarks", method: "SlowOne", job: "short");
        var std = Make(tier: "standard", method: "StdOne");

        var html = ReportGenerator.Render(new[] { slow, std });

        html.IndexOf("data-tier=\"standard\"", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("data-tier=\"slow\"", StringComparison.Ordinal));
        html.Should().Contain("<td>SlowOne</td>").And.Contain("<td>StdOne</td>");
        html.Should().Contain("job short").And.Contain("job medium");
    }

    [Fact]
    public void Render_ShouldTableNewestVersionByNumericOrder_When8Point10Follows8Point9()
    {
        var v9 = Make("8.9.0", method: "AtNine");
        var v10 = Make("8.10.0", method: "AtTen");

        var html = ReportGenerator.Render(new[] { v10, v9 });

        html.Should().Contain("<td>AtTen</td>").And.NotContain("<td>AtNine</td>");
        html.Should().Contain("versions recorded: 8.9.0, 8.10.0");
    }

    [Theory]
    [InlineData("8.9.0", "8.10.0", -1)]
    [InlineData("8.10.0", "8.9.0", 1)]
    [InlineData("8.2.0", "8.2.0", 0)]
    [InlineData("8.2", "8.2.0", -1)]
    [InlineData("8.2.0-beta", "8.2.0", -1)]
    [InlineData("9.0.0", "8.99.99", 1)]
    public void CompareVersions_ShouldOrderNumerically(string a, string b, int sign)
    {
        Math.Sign(ReportGenerator.CompareVersions(a, b)).Should().Be(sign);
    }

    [Theory]
    [InlineData(0.0, "0 ns")]
    [InlineData(999.0, "999 ns")]
    [InlineData(1000.0, "1 µs")]
    [InlineData(1500.0, "1.5 µs")]
    [InlineData(2_345_000.0, "2.35 ms")]
    [InlineData(1e9, "1 s")]
    [InlineData(7.5e9, "7.5 s")]
    public void FormatDuration_ShouldPickUnit(double ns, string expected)
    {
        ReportGenerator.FormatDuration(ns).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "–")]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(5L * 1048576 + 524288, "5.5 MB")]
    public void FormatBytes_ShouldPickUnit(long? bytes, string expected)
    {
        ReportGenerator.FormatBytes(bytes).Should().Be(expected);
    }

    [Fact]
    public void Render_ShouldLabelRunWithVersionJobSha7AndDirtyFlag()
    {
        var dirty = Stored(BenchmarkTestData.Device(), BenchmarkTestData.Run(sha: "abcdef1234567890abcdef1234567890abcdef12", dirty: true, job: "short"));

        var html = ReportGenerator.Render(new[] { dirty });

        html.Should().Contain("Version 8.2.0 · job short · captured 2026-10-20T12:30:45Z · git abcdef1 (dirty)");
    }

    [Fact]
    public void Render_ShouldLabelNogitAndAllocatedDash_WhenGitAndAllocationMissing()
    {
        var run = BenchmarkTestData.Run(sha: null, dirty: null,
            results: BenchmarkTestData.Result() with { AllocatedBytes = null });

        var html = ReportGenerator.Render(new[] { Stored(BenchmarkTestData.Device(), run) });

        html.Should().Contain("git nogit (git state unknown)");
        html.Should().Contain("<td class=\"num\">–</td>");
    }

    [Fact]
    public void Render_ShouldEmitRowWithFormattedValues_AndSuccessFlag()
    {
        var run = BenchmarkTestData.Run(results: BenchmarkTestData.Result("Cls", "M", "n=5", 1500.0, success: false));

        var html = ReportGenerator.Render(new[] { Stored(BenchmarkTestData.Device(), run) });

        html.Should().Contain("<tr><td>Cls</td><td>M</td><td>n=5</td><td class=\"num\">1.5 µs</td><td class=\"num\">±0.5 ns</td><td class=\"num\">128 B</td><td>no</td></tr>");
    }

    [Fact]
    public void Render_ShouldEmitCanvasPerClass_EvenWithSingleVersion()
    {
        var run = BenchmarkTestData.Run(results: new[] { BenchmarkTestData.Result("ClsA", "M"), BenchmarkTestData.Result("ClsB", "M") });

        var html = ReportGenerator.Render(new[] { Stored(BenchmarkTestData.Device(), run) });

        html.Should().Contain("<canvas id=\"chart-standard-ClsA\"").And.Contain("<canvas id=\"chart-standard-ClsB\"");
    }

    [Fact]
    public void Render_ShouldEscapeHtml_InNamesAndEmbedJsonSafely()
    {
        var run = BenchmarkTestData.Run(results: BenchmarkTestData.Result("Cls", "<b>&M", "</script>"));

        var html = ReportGenerator.Render(new[] { Stored(BenchmarkTestData.Device(), run) });

        html.Should().Contain("<td>&lt;b&gt;&amp;M</td>");
        html.Should().NotContain("<b>&M");
        var start = html.IndexOf("id=\"benchmark-data\">", StringComparison.Ordinal);
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        html[start..end].Should().Contain("\\u003C/script\\u003E");
    }

    [Fact]
    public void Render_ShouldEmbedDataJsonOnce_WithAllDevices()
    {
        var html = ReportGenerator.Render(new[] { Make(slug: "box-b"), Make(slug: "box-a") });

        html.Split("id=\"benchmark-data\"").Length.Should().Be(2);
        var start = html.IndexOf("id=\"benchmark-data\">", StringComparison.Ordinal) + "id=\"benchmark-data\">".Length;
        var json = html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("slug").GetString())
            .Should().Equal("box-a", "box-b");
    }
}
