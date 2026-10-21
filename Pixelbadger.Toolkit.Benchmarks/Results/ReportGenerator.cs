using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

/// <summary>Renders the benchmark report page from stored runs. Pure and deterministic: no clock, invariant culture, '\n' line endings.</summary>
public static class ReportGenerator
{
    private sealed record ResultView(string Class, string Method, string Parameters, bool Success, double MeanNs, double ErrorNs, long? AllocatedBytes);

    private sealed record RunView(string Version, JobInfo Job, string CapturedAtUtc, string GitSha7, bool? GitDirty, IReadOnlyList<ResultView> Results);

    private sealed record TierView(string Tier, IReadOnlyList<RunView> Runs);

    private sealed record DeviceView(string Slug, DeviceFingerprint Fingerprint, IReadOnlyList<string> Versions, IReadOnlyList<TierView> Tiers);

    private sealed record ReportData(IReadOnlyList<DeviceView> Devices);

    private static readonly JsonSerializerOptions DataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string Render(IReadOnlyList<StoredRun> runs)
    {
        var data = BuildData(runs);
        var totalRuns = runs.Count;
        var json = JsonSerializer.Serialize(data, DataOptions);

        var sb = new StringBuilder();
        void L(string line) => sb.Append(line).Append('\n');

        L("<!DOCTYPE html>");
        L("<html lang=\"en\">");
        L("<head>");
        L("  <meta charset=\"UTF-8\">");
        L("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        L("  <title>Pixelbadger Toolkit — Benchmark Report</title>");
        L("  <script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js\"></script>");
        L("  <link rel=\"stylesheet\" href=\"styles.css\">");
        L("  <style>");
        sb.Append(StyleBlock.Replace("\r\n", "\n")).Append('\n');
        L("  </style>");
        L("</head>");
        L("<body>");
        L("");
        L("<header>");
        L("  <h1>Benchmark Report <span class=\"badge\">benchmarks</span></h1>");
        L("  <div class=\"subtitle\">Device and version results captured by the Benchmarks runner</div>");
        L("  <div class=\"meta\">Pixelbadger Toolkit &mdash; generated from <code>benchmarks/results</code> by <code>report</code> &mdash; <a href=\"index.html\" style=\"color:#60a5fa\">&#8592; all topics</a></div>");
        L("</header>");
        L("");
        L("<nav>");
        L("  <a href=\"#summary\">Summary</a>");
        L("  <a href=\"#results\">Results</a>");
        L("</nav>");
        L("");
        L("<div class=\"content\">");
        L("");
        L("<div class=\"summary-grid\" id=\"summary\">");
        L("  <div class=\"stat-card\">");
        L("    <div class=\"label\">Devices</div>");
        L($"    <div class=\"value\">{data.Devices.Count}</div>");
        L("    <div class=\"sub\">distinct hardware fingerprints</div>");
        L("  </div>");
        L("  <div class=\"stat-card\">");
        L("    <div class=\"label\">Runs Recorded</div>");
        L($"    <div class=\"value\">{totalRuns}</div>");
        L("    <div class=\"sub\">run files in the results tree</div>");
        L("  </div>");
        L("</div>");
        L("");
        L("<main id=\"results\">");
        if (data.Devices.Count == 0)
        {
            L("  <section>");
            L("    <p class=\"note\">No benchmark results recorded yet</p>");
            L("  </section>");
        }
        else
        {
            L("  <section>");
            L("    <div class=\"device-bar\">");
            L("      <label for=\"device-select\">Device</label>");
            L("      <select id=\"device-select\">");
            for (var i = 0; i < data.Devices.Count; i++)
            {
                var slug = Enc(data.Devices[i].Slug);
                L($"        <option value=\"{slug}\"{(i == 0 ? " selected" : "")}>{slug}</option>");
            }
            L("      </select>");
            L("    </div>");
            L("  </section>");
            L("  <div id=\"device-content\">");
            sb.Append(DeviceHtml(data.Devices[0]));
            L("  </div>");
        }
        L("</main>");
        L("");
        L("</div>");
        L("");
        L("<script type=\"application/json\" id=\"benchmark-data\">" + json + "</script>");
        L("<script>");
        sb.Append(ScriptBlock.Replace("\r\n", "\n")).Append('\n');
        L("</script>");
        L("</body>");
        L("</html>");
        return sb.ToString();
    }

    // ---- formatting -------------------------------------------------------------------------------------------

    public static string FormatNumber(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static string FormatDuration(double ns) =>
        ns < 1e3 ? FormatNumber(ns) + " ns"
        : ns < 1e6 ? FormatNumber(ns / 1e3) + " µs"
        : ns < 1e9 ? FormatNumber(ns / 1e6) + " ms"
        : FormatNumber(ns / 1e9) + " s";

    public static string FormatBytes(long? bytes) =>
        bytes is null ? "–"
        : bytes < 1024 ? bytes.Value.ToString(CultureInfo.InvariantCulture) + " B"
        : bytes < 1024 * 1024 ? FormatNumber(bytes.Value / 1024.0) + " KB"
        : FormatNumber(bytes.Value / (1024.0 * 1024.0)) + " MB";

    /// <summary>Orders versions by numeric dotted core, then release before pre-release, then ordinal.</summary>
    public static int CompareVersions(string a, string b)
    {
        var (coreA, preA) = SplitVersion(a);
        var (coreB, preB) = SplitVersion(b);
        var n = Math.Max(coreA.Length, coreB.Length);
        for (var i = 0; i < n; i++)
        {
            var x = i < coreA.Length ? coreA[i] : 0;
            var y = i < coreB.Length ? coreB[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        if (preA.Length == 0 != (preB.Length == 0)) return preA.Length == 0 ? 1 : -1;
        return string.CompareOrdinal(a, b);
    }

    private static (long[] Core, string Pre) SplitVersion(string v)
    {
        var cut = v.IndexOfAny(new[] { '-', '+' });
        var core = cut < 0 ? v : v[..cut];
        var pre = cut < 0 ? "" : v[cut..];
        var parts = core.Split('.')
            .Select(p => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .ToArray();
        return (parts, pre);
    }

    private static string Sha7(string? sha) => string.IsNullOrEmpty(sha) ? "nogit" : sha[..Math.Min(7, sha.Length)];

    private static string DirtyLabel(bool? dirty) => dirty switch { true => "dirty", false => "clean", null => "git state unknown" };

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    // ---- data -------------------------------------------------------------------------------------------------

    private static ReportData BuildData(IReadOnlyList<StoredRun> runs)
    {
        var devices = new List<DeviceView>();
        foreach (var group in runs.GroupBy(r => r.DeviceSlug, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var fingerprint = group.OrderBy(r => r.RelativePath, StringComparer.Ordinal).First().Device.Fingerprint;
            var versions = group.Select(r => r.Version).Distinct(StringComparer.Ordinal).ToList();
            versions.Sort((a, b) => CompareVersions(a, b));

            var tiers = new List<TierView>();
            foreach (var tier in group.Select(r => r.Run.Tier).Distinct(StringComparer.Ordinal)
                         .OrderBy(t => t == "standard" ? 0 : t == "slow" ? 1 : 2).ThenBy(t => t, StringComparer.Ordinal))
            {
                var latest = new List<RunView>();
                foreach (var version in versions)
                {
                    var best = group.Where(r => r.Version == version && r.Run.Tier == tier)
                        .OrderByDescending(r => r.Run.CapturedAtUtc)
                        .ThenByDescending(r => r.RelativePath, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (best is null) continue;
                    var results = best.Run.Results
                        .OrderBy(x => x.Class, StringComparer.Ordinal)
                        .ThenBy(x => x.Method, StringComparer.Ordinal)
                        .ThenBy(x => x.Parameters, StringComparer.Ordinal)
                        .Select(x => new ResultView(x.Class, x.Method, x.Parameters, x.Success, x.MeanNs, x.ErrorNs, x.AllocatedBytes))
                        .ToList();
                    latest.Add(new RunView(version, best.Run.Job,
                        best.Run.CapturedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
                        Sha7(best.Run.GitSha), best.Run.GitDirty, results));
                }
                tiers.Add(new TierView(tier, latest));
            }
            devices.Add(new DeviceView(group.Key, fingerprint, versions, tiers));
        }
        return new ReportData(devices);
    }

    // ---- static HTML (mirrors the page script's render) -------------------------------------------------------

    private static string DeviceHtml(DeviceView device)
    {
        var sb = new StringBuilder();
        void L(string line) => sb.Append(line).Append('\n');
        var f = device.Fingerprint;
        L("    <section id=\"device-info\">");
        L($"      <p class=\"note\">CPU: {Enc(f.Cpu)} · Cores: {f.PhysicalCores} physical / {f.LogicalCores} logical · Memory: {f.MemoryGb} GB · OS: {Enc(f.Os)} ({Enc(f.OsDescription)}) · Arch: {Enc(f.Arch)} · Hypervisor: {Enc(f.Hypervisor ?? "none")}</p>");
        L("    </section>");
        foreach (var tier in device.Tiers)
        {
            var newest = tier.Runs[^1];
            L($"    <section data-tier=\"{Enc(tier.Tier)}\">");
            L($"      <h2>{Enc(Capitalise(tier.Tier))} tier</h2>");
            L($"      <p class=\"note\">Version {Enc(newest.Version)} · job {Enc(newest.Job.Name)} · captured {newest.CapturedAtUtc} · git {Enc(newest.GitSha7)} ({DirtyLabel(newest.GitDirty)}) · versions recorded: {Enc(string.Join(", ", tier.Runs.Select(r => r.Version)))}</p>");
            L("      <div class=\"table-wrap\">");
            L("      <table>");
            L("        <thead><tr><th>Class</th><th>Method</th><th>Parameters</th><th>Mean</th><th>± Error</th><th>Allocated</th><th>Success</th></tr></thead>");
            L("        <tbody>");
            foreach (var r in newest.Results)
            {
                L($"          <tr><td>{Enc(r.Class)}</td><td>{Enc(r.Method)}</td><td>{Enc(r.Parameters)}</td><td class=\"num\">{FormatDuration(r.MeanNs)}</td><td class=\"num\">±{FormatDuration(r.ErrorNs)}</td><td class=\"num\">{FormatBytes(r.AllocatedBytes)}</td><td>{(r.Success ? "yes" : "no")}</td></tr>");
            }
            L("        </tbody>");
            L("      </table>");
            L("      </div>");
            L("      <div class=\"chart-grid\">");
            foreach (var cls in tier.Runs.SelectMany(r => r.Results).Select(r => r.Class).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal))
            {
                L("        <div class=\"chart-card\">");
                L($"          <h3>{Enc(cls)} — mean per version (ns, log scale)</h3>");
                L($"          <div class=\"chart-box\"><canvas id=\"chart-{Enc(tier.Tier)}-{Enc(cls)}\" data-tier=\"{Enc(tier.Tier)}\" data-class=\"{Enc(cls)}\"></canvas></div>");
                L("        </div>");
            }
            L("      </div>");
            L("    </section>");
        }
        return sb.ToString();
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---- inline assets ----------------------------------------------------------------------------------------

    private const string StyleBlock = """
    .summary-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; margin-bottom: 2.5rem; }
    .stat-card { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 1.1rem 1.4rem; }
    .stat-card .label { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 0.75rem; letter-spacing: 0.06em; text-transform: uppercase; color: var(--text3); }
    .stat-card .value { font-size: 2.1rem; font-weight: 700; line-height: 1.2; color: var(--heading); }
    .stat-card .sub { font-size: 0.85rem; color: var(--text2); }
    .note { color: var(--text2); font-size: 0.9rem; margin-bottom: 0.75rem; }
    .device-bar { display: flex; align-items: center; gap: 0.75rem; }
    .device-bar label { color: var(--text2); font-weight: 600; }
    .device-bar select { font: inherit; padding: 0.4rem 0.6rem; background: var(--surface); color: var(--text); border: 1px solid var(--border); border-radius: 6px; max-width: 100%; }
    .table-wrap { overflow-x: auto; }
    table td.num { white-space: nowrap; text-align: right; }
    .chart-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 460px), 1fr)); gap: 1.25rem; margin-top: 1.5rem; }
    .chart-card { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 1rem 1.25rem; min-width: 0; }
    .chart-card h3 { font-size: 0.95rem; color: var(--heading); margin-bottom: 0.75rem; }
    .chart-box { position: relative; height: 320px; }
    @media (max-width: 640px) { .stat-card .value { font-size: 1.7rem; } }
""";

    private const string ScriptBlock = """
(function () {
  var el = document.getElementById('benchmark-data');
  var select = document.getElementById('device-select');
  var content = document.getElementById('device-content');
  if (!el || !select || !content) return;
  var data = JSON.parse(el.textContent);
  var charts = [];
  var palette = ['#2563eb', '#dc2626', '#16a34a', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#65a30d', '#475569', '#ea580c'];

  function esc(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }
  function num(v) { return String(Number(v.toFixed(2))); }
  function dur(ns) {
    if (ns < 1e3) return num(ns) + ' ns';
    if (ns < 1e6) return num(ns / 1e3) + ' \u00b5s';
    if (ns < 1e9) return num(ns / 1e6) + ' ms';
    return num(ns / 1e9) + ' s';
  }
  function bytes(b) {
    if (b === null || b === undefined) return '\u2013';
    if (b < 1024) return b + ' B';
    if (b < 1048576) return num(b / 1024) + ' KB';
    return num(b / 1048576) + ' MB';
  }
  function dirty(d) { return d === true ? 'dirty' : d === false ? 'clean' : 'git state unknown'; }
  function cap(s) { return s.charAt(0).toUpperCase() + s.slice(1); }
  function uniqueSorted(list) {
    return list.filter(function (v, i) { return list.indexOf(v) === i; }).sort(function (a, b) { return a < b ? -1 : a > b ? 1 : 0; });
  }

  function html(device) {
    var f = device.fingerprint;
    var h = '    <section id="device-info">\n      <p class="note">CPU: ' + esc(f.cpu) + ' \u00b7 Cores: ' + f.physicalCores + ' physical / ' + f.logicalCores +
      ' logical \u00b7 Memory: ' + f.memoryGb + ' GB \u00b7 OS: ' + esc(f.os) + ' (' + esc(f.osDescription) + ') \u00b7 Arch: ' + esc(f.arch) +
      ' \u00b7 Hypervisor: ' + esc(f.hypervisor || 'none') + '</p>\n    </section>\n';
    device.tiers.forEach(function (tier) {
      var newest = tier.runs[tier.runs.length - 1];
      h += '    <section data-tier="' + esc(tier.tier) + '">\n      <h2>' + esc(cap(tier.tier)) + ' tier</h2>\n';
      h += '      <p class="note">Version ' + esc(newest.version) + ' \u00b7 job ' + esc(newest.job.name) + ' \u00b7 captured ' + newest.capturedAtUtc +
        ' \u00b7 git ' + esc(newest.gitSha7) + ' (' + dirty(newest.gitDirty) + ') \u00b7 versions recorded: ' +
        esc(tier.runs.map(function (r) { return r.version; }).join(', ')) + '</p>\n';
      h += '      <div class="table-wrap">\n      <table>\n        <thead><tr><th>Class</th><th>Method</th><th>Parameters</th><th>Mean</th><th>\u00b1 Error</th><th>Allocated</th><th>Success</th></tr></thead>\n        <tbody>\n';
      newest.results.forEach(function (r) {
        h += '          <tr><td>' + esc(r.class) + '</td><td>' + esc(r.method) + '</td><td>' + esc(r.parameters) + '</td><td class="num">' + dur(r.meanNs) +
          '</td><td class="num">\u00b1' + dur(r.errorNs) + '</td><td class="num">' + bytes(r.allocatedBytes) + '</td><td>' + (r.success ? 'yes' : 'no') + '</td></tr>\n';
      });
      h += '        </tbody>\n      </table>\n      </div>\n      <div class="chart-grid">\n';
      var classes = [];
      tier.runs.forEach(function (r) { r.results.forEach(function (x) { classes.push(x.class); }); });
      uniqueSorted(classes).forEach(function (c) {
        h += '        <div class="chart-card">\n          <h3>' + esc(c) + ' \u2014 mean per version (ns, log scale)</h3>\n          <div class="chart-box"><canvas id="chart-' + esc(tier.tier) +
          '-' + esc(c) + '" data-tier="' + esc(tier.tier) + '" data-class="' + esc(c) + '"></canvas></div>\n        </div>\n';
      });
      h += '      </div>\n    </section>\n';
    });
    return h;
  }

  function drawCharts(device) {
    charts.forEach(function (c) { c.destroy(); });
    charts = [];
    if (typeof Chart === 'undefined') return;
    var css = getComputedStyle(document.documentElement);
    var textColor = css.getPropertyValue('--text2').trim() || '#888';
    var gridColor = css.getPropertyValue('--border').trim() || '#8884';
    var canvases = content.querySelectorAll('canvas[data-class]');
    Array.prototype.forEach.call(canvases, function (canvas) {
      var tier = device.tiers.filter(function (t) { return t.tier === canvas.getAttribute('data-tier'); })[0];
      var cls = canvas.getAttribute('data-class');
      var labels = tier.runs.map(function (r) { return r.version; });
      var keys = [];
      tier.runs.forEach(function (r) {
        r.results.forEach(function (x) {
          if (x.class === cls) keys.push(x.method + (x.parameters ? ' [' + x.parameters + ']' : ''));
        });
      });
      var datasets = uniqueSorted(keys).map(function (key, i) {
        return {
          label: key,
          borderColor: palette[i % palette.length],
          backgroundColor: palette[i % palette.length],
          spanGaps: true,
          pointRadius: 4,
          data: tier.runs.map(function (r) {
            var hit = r.results.filter(function (x) { return x.class === cls && x.method + (x.parameters ? ' [' + x.parameters + ']' : '') === key; })[0];
            return hit ? hit.meanNs : null;
          })
        };
      });
      charts.push(new Chart(canvas, {
        type: 'line',
        data: { labels: labels, datasets: datasets },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: { legend: { labels: { color: textColor } } },
          scales: {
            x: { ticks: { color: textColor }, grid: { color: gridColor } },
            y: { type: 'logarithmic', ticks: { color: textColor }, grid: { color: gridColor }, title: { display: true, text: 'mean (ns)', color: textColor } }
          }
        }
      }));
    });
  }

  function render(slug) {
    var device = data.devices.filter(function (d) { return d.slug === slug; })[0];
    if (!device) return;
    content.innerHTML = html(device);
    drawCharts(device);
  }

  select.addEventListener('change', function () { render(select.value); });
  render(select.value);
})();
""";
}
