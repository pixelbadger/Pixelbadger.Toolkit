using System.Globalization;
using System.Net;
using System.Text;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// Writes a training run's per-step loss history to a standalone HTML file: the full series is
/// embedded as a JSON array and drawn to a canvas by inline script, so the report needs no
/// network access and stays readable for runs with tens of thousands of steps.
/// </summary>
public sealed class LossGraphService : ILossGraphService
{
    public async Task WriteAsync(string outputPath, LossGraphReport report)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(fullPath, Render(report));
    }

    /// <summary>Builds the report HTML. Exposed for testing without touching the file system.</summary>
    internal static string Render(LossGraphReport report)
    {
        var losses = report.Losses;
        var finite = losses.Where(float.IsFinite).ToList();

        int tokensSeen = report.TokensSeen.Count > 0 ? report.TokensSeen[^1] : 0;

        int runs = Math.Max(1, report.RunBoundaries.Count);

        var stats = new StringBuilder();
        AppendStat(stats, "Steps", losses.Count.ToString("N0", CultureInfo.InvariantCulture));
        AppendStat(stats, "Runs", runs.ToString("N0", CultureInfo.InvariantCulture));
        AppendStat(stats, "First loss", FormatLoss(finite.Count > 0 ? finite[0] : float.NaN));
        AppendStat(stats, "Final loss", FormatLoss(finite.Count > 0 ? finite[^1] : float.NaN));
        AppendStat(stats, "Best loss", FormatLoss(finite.Count > 0 ? finite.Min() : float.NaN));
        AppendStat(stats, "Corpus coverage", FormatPercent(tokensSeen, report.CorpusTokenCount));
        AppendStat(stats, "Vocab coverage", FormatPercent(report.VocabEntriesSeen, report.VocabSize));

        var meta = new StringBuilder();
        AppendMeta(meta, "Parameters", report.ParameterCount.ToString("N0", CultureInfo.InvariantCulture));
        AppendMeta(meta, "Vocab size", report.VocabSize.ToString("N0", CultureInfo.InvariantCulture));
        AppendMeta(meta, "Vocab entries seen", FormatSeen(report.VocabEntriesSeen, report.VocabSize));
        AppendMeta(meta, "Corpus tokens", report.CorpusTokenCount.ToString("N0", CultureInfo.InvariantCulture));
        AppendMeta(meta, "Corpus tokens seen", FormatSeen(tokensSeen, report.CorpusTokenCount));
        AppendMeta(meta, "Block size", report.Config.BlockSize.ToString(CultureInfo.InvariantCulture));
        AppendMeta(meta, "Batch size", report.BatchSize.ToString(CultureInfo.InvariantCulture));
        AppendMeta(meta, "n-embd", report.Config.NEmbd.ToString(CultureInfo.InvariantCulture));
        AppendMeta(meta, "n-head", report.Config.NHead.ToString(CultureInfo.InvariantCulture));
        AppendMeta(meta, "n-layer", report.Config.NLayer.ToString(CultureInfo.InvariantCulture));
        AppendMeta(meta, "Learning rate", report.LearningRate.ToString("0.#######", CultureInfo.InvariantCulture));
        AppendMeta(meta, "Checkpoint", WebUtility.HtmlEncode(report.CheckpointPath));
        AppendMeta(meta, "Training runs", runs.ToString("N0", CultureInfo.InvariantCulture));
        AppendMeta(meta, "Latest run resumed", report.Resumed ? "yes" : "no");

        var generated = WebUtility.HtmlEncode(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>GPT Training Loss</title>
<style>
  :root {
    --bg: #f8fafc; --panel: #ffffff; --border: #e2e8f0; --text: #0f172a;
    --muted: #64748b; --grid: #e2e8f0; --raw: #94a3b8; --line: #2563eb; --best: #16a34a;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #0f172a; --panel: #1e293b; --border: #334155; --text: #e2e8f0;
      --muted: #94a3b8; --grid: #334155; --raw: #475569; --line: #60a5fa; --best: #4ade80;
    }
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; padding: 2rem 1.25rem; background: var(--bg); color: var(--text);
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  }
  .wrap { max-width: 1000px; margin: 0 auto; }
  h1 { font-size: 1.5rem; margin: 0 0 .25rem; }
  .sub { color: var(--muted); font-size: .875rem; margin: 0 0 1.5rem; }
  .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: .75rem; margin-bottom: 1.5rem; }
  .stat { background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: .75rem 1rem; }
  .stat .label { color: var(--muted); font-size: .75rem; text-transform: uppercase; letter-spacing: .04em; }
  .stat .value { font-size: 1.375rem; font-variant-numeric: tabular-nums; margin-top: .25rem; }
  .chart {
    position: relative; background: var(--panel); border: 1px solid var(--border);
    border-radius: 8px; padding: .75rem; margin-bottom: 1.5rem;
  }
  .chart h2 { font-size: .9375rem; font-weight: 600; margin: .25rem .25rem .5rem; }
  .chart h2 small { color: var(--muted); font-weight: 400; }
  canvas { display: block; width: 100%; height: 320px; }
  .legend { display: flex; gap: 1.25rem; flex-wrap: wrap; color: var(--muted); font-size: .8125rem; padding: .5rem .25rem 0; }
  .legend span::before {
    content: ""; display: inline-block; width: 14px; height: 3px; border-radius: 2px;
    margin-right: .4rem; vertical-align: middle; background: currentColor;
  }
  .legend .raw { color: var(--raw); }
  .legend .smooth { color: var(--line); }
  .legend .divider { color: var(--muted); }
  .legend .divider::before { background: repeating-linear-gradient(90deg, currentColor 0 4px, transparent 4px 8px); }
  .tip {
    position: absolute; pointer-events: none; display: none; background: var(--panel);
    border: 1px solid var(--border); border-radius: 6px; padding: .4rem .6rem;
    font-size: .8125rem; font-variant-numeric: tabular-nums; white-space: nowrap;
    box-shadow: 0 4px 12px rgba(0,0,0,.15);
  }
  table { width: 100%; border-collapse: collapse; background: var(--panel); border: 1px solid var(--border); border-radius: 8px; overflow: hidden; }
  td { padding: .5rem .875rem; border-bottom: 1px solid var(--border); font-size: .875rem; }
  tr:last-child td { border-bottom: none; }
  td:first-child { color: var(--muted); width: 40%; }
  td:last-child { font-variant-numeric: tabular-nums; word-break: break-all; }
</style>
</head>
<body>
<div class="wrap">
  <h1>GPT Training History</h1>
  <p class="sub">Per-step loss and corpus coverage across every run against this checkpoint &mdash; generated {{generated}}</p>

  <div class="stats">
{{stats}}  </div>

  <div class="chart">
    <h2>Loss <small>&mdash; cross-entropy per step</small></h2>
    <canvas id="loss-chart"></canvas>
    <div class="tip" id="loss-tip"></div>
    <div class="legend">
      <span class="raw">per-step loss</span>
      <span class="smooth">smoothed</span>
      <span class="divider">run boundary</span>
    </div>
  </div>

  <div class="chart">
    <h2>Corpus coverage <small>&mdash; share of the corpus's {{report.CorpusTokenCount.ToString("N0", CultureInfo.InvariantCulture)}} token positions sampled at least once</small></h2>
    <canvas id="coverage-chart"></canvas>
    <div class="tip" id="coverage-tip"></div>
    <div class="legend">
      <span class="smooth">cumulative unique tokens visited</span>
    </div>
  </div>

  <table>
    <tbody>
{{meta}}    </tbody>
  </table>
</div>

<script>
const LOSSES = [{{FormatSeries(losses)}}];
const TOKENS_SEEN = [{{FormatCounts(report.TokensSeen)}}];
const CORPUS_TOKENS = {{report.CorpusTokenCount.ToString(CultureInfo.InvariantCulture)}};
// Cumulative step count at the end of each training run; all but the last mark a --resume point.
const RUN_BOUNDARIES = [{{FormatCounts(report.RunBoundaries)}}];

const PAD = { top: 16, right: 16, bottom: 32, left: 56 };
const FONT = '11px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

function extent(values) {
  let min = Infinity, max = -Infinity;
  for (const v of values) {
    if (v === null) continue;
    if (v < min) min = v;
    if (v > max) max = v;
  }
  return min === Infinity ? [0, 1] : [min, max];
}

// Exponential moving average; the window scales with run length so short and long runs both
// read clearly. Non-finite losses (a diverged run) are carried through as null.
function smooth(values) {
  const alpha = 2 / (Math.max(1, Math.round(values.length / 100)) + 1);
  const out = [];
  let ema = null;
  for (const v of values) {
    if (v === null) { out.push(null); continue; }
    ema = ema === null ? v : alpha * v + (1 - alpha) * ema;
    out.push(ema);
  }
  return out;
}

/**
 * Draws one line chart onto a canvas and wires up its hover readout.
 * layers: [{ data, color, width }] over a shared step axis; opts.yLabel/opts.tipText format text.
 */
function chart(canvasId, tipId, layers, opts) {
  const canvas = document.getElementById(canvasId);
  const tip = document.getElementById(tipId);
  const ctx = canvas.getContext('2d');
  const steps = layers[0].data.length;

  let lo, hi;
  if (opts.yRange) {
    [lo, hi] = opts.yRange;
  } else {
    const [min, max] = extent(layers.flatMap(l => l.data));
    const pad = (max - min) * 0.08 || 0.5;
    lo = Math.max(0, min - pad);
    hi = max + pad;
  }

  let plot = { x: 0, y: 0, w: 0, h: 0 };
  const xAt = i => plot.x + (steps <= 1 ? plot.w / 2 : (i / (steps - 1)) * plot.w);
  const yAt = v => plot.y + plot.h - ((v - lo) / (hi - lo || 1)) * plot.h;

  function stroke(values, color, width) {
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineJoin = 'round';
    ctx.beginPath();
    let open = false;
    for (let i = 0; i < values.length; i++) {
      const v = values[i];
      if (v === null) { open = false; continue; }
      const x = xAt(i), y = yAt(v);
      if (open) ctx.lineTo(x, y); else { ctx.moveTo(x, y); open = true; }
    }
    ctx.stroke();
  }

  function draw() {
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.clientWidth, h = canvas.clientHeight;
    canvas.width = w * dpr;
    canvas.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);

    plot = { x: PAD.left, y: PAD.top, w: w - PAD.left - PAD.right, h: h - PAD.top - PAD.bottom };
    if (plot.w <= 0 || plot.h <= 0 || !steps) return;

    ctx.font = FONT;
    ctx.strokeStyle = css('--grid');
    ctx.fillStyle = css('--muted');
    ctx.lineWidth = 1;
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= 5; i++) {
      const v = lo + (hi - lo) * (i / 5);
      const y = Math.round(yAt(v)) + 0.5;
      ctx.beginPath();
      ctx.moveTo(plot.x, y);
      ctx.lineTo(plot.x + plot.w, y);
      ctx.stroke();
      ctx.fillText(opts.yLabel(v), plot.x - 8, y);
    }

    // Dashed divider wherever a run ended and a later --resume picked the checkpoint back up.
    ctx.save();
    ctx.setLineDash([4, 4]);
    ctx.strokeStyle = css('--muted');
    for (let b = 0; b < RUN_BOUNDARIES.length - 1; b++) {
      const x = Math.round(xAt(RUN_BOUNDARIES[b] - 1)) + 0.5;
      ctx.beginPath();
      ctx.moveTo(x, plot.y);
      ctx.lineTo(x, plot.y + plot.h);
      ctx.stroke();
    }
    ctx.restore();

    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    const xTicks = Math.min(6, steps);
    for (let i = 0; i < xTicks; i++) {
      const idx = xTicks === 1 ? 0 : Math.round((i / (xTicks - 1)) * (steps - 1));
      ctx.fillText(String(idx + 1), xAt(idx), plot.y + plot.h + 8);
    }

    for (const layer of layers) stroke(layer.data, css(layer.color), layer.width);
  }

  canvas.addEventListener('mousemove', e => {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    if (!steps || x < plot.x || x > plot.x + plot.w) { tip.style.display = 'none'; draw(); return; }

    const i = Math.max(0, Math.min(steps - 1, Math.round(((x - plot.x) / (plot.w || 1)) * (steps - 1))));
    const v = layers[0].data[i];
    draw();

    ctx.strokeStyle = css('--muted');
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(Math.round(xAt(i)) + 0.5, plot.y);
    ctx.lineTo(Math.round(xAt(i)) + 0.5, plot.y + plot.h);
    ctx.stroke();

    if (v !== null) {
      ctx.fillStyle = css(layers[layers.length - 1].color);
      ctx.beginPath();
      ctx.arc(xAt(i), yAt(v), 3, 0, Math.PI * 2);
      ctx.fill();
    }

    tip.textContent = opts.tipText(i, v);
    tip.style.display = 'block';
    tip.style.left = Math.min(rect.width - tip.offsetWidth - 8, xAt(i) + 12) + 'px';
    tip.style.top = (v === null ? plot.y : Math.max(plot.y, yAt(v) - 32)) + 'px';
  });

  canvas.addEventListener('mouseleave', () => { tip.style.display = 'none'; draw(); });
  window.addEventListener('resize', draw);
  if (window.matchMedia) window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', draw);
  draw();
}

chart('loss-chart', 'loss-tip',
  [{ data: LOSSES, color: '--raw', width: 1 }, { data: smooth(LOSSES), color: '--line', width: 2 }],
  {
    yLabel: v => v.toFixed(2),
    tipText: (i, v) => 'step ' + (i + 1) + ' — loss ' + (v === null ? 'n/a' : v.toFixed(4))
  });

const coveragePct = TOKENS_SEEN.map(n => CORPUS_TOKENS ? (n / CORPUS_TOKENS) * 100 : 0);
chart('coverage-chart', 'coverage-tip',
  [{ data: coveragePct, color: '--line', width: 2 }],
  {
    yRange: [0, 100],
    yLabel: v => v.toFixed(0) + '%',
    tipText: (i, v) => 'step ' + (i + 1) + ' — ' + TOKENS_SEEN[i].toLocaleString() +
      ' tokens (' + v.toFixed(1) + '%)'
  });
</script>
</body>
</html>

""";
    }

    /// <summary>Emits the loss series as JSON numbers; non-finite values become <c>null</c> so the page can skip them.</summary>
    private static string FormatSeries(IReadOnlyList<float> losses)
    {
        var builder = new StringBuilder(losses.Count * 8);
        for (int i = 0; i < losses.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(float.IsFinite(losses[i])
                ? losses[i].ToString("0.######", CultureInfo.InvariantCulture)
                : "null");
        }
        return builder.ToString();
    }

    /// <summary>Emits the cumulative tokens-seen series as JSON integers.</summary>
    private static string FormatCounts(IReadOnlyList<int> counts) =>
        string.Join(',', counts.Select(c => c.ToString(CultureInfo.InvariantCulture)));

    private static string FormatPercent(int seen, int total) =>
        total > 0 ? (100d * seen / total).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static string FormatSeen(int seen, int total) =>
        $"{seen.ToString("N0", CultureInfo.InvariantCulture)} of {total.ToString("N0", CultureInfo.InvariantCulture)} ({FormatPercent(seen, total)})";

    private static string FormatLoss(float value) =>
        float.IsFinite(value) ? value.ToString("0.0000", CultureInfo.InvariantCulture) : "n/a";

    private static void AppendStat(StringBuilder builder, string label, string value) =>
        builder.Append("    <div class=\"stat\"><div class=\"label\">").Append(WebUtility.HtmlEncode(label))
            .Append("</div><div class=\"value\">").Append(value).AppendLine("</div></div>");

    private static void AppendMeta(StringBuilder builder, string label, string value) =>
        builder.Append("      <tr><td>").Append(WebUtility.HtmlEncode(label))
            .Append("</td><td>").Append(value).AppendLine("</td></tr>");
}
