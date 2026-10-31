using System.Text;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.Components;

/// <summary>Spectre.Console live playback view: braille oscilloscope, progress bar and a status line.</summary>
public sealed class LiveMidiPlaybackView : IMidiPlaybackView
{
    private const int WindowFrames = 960;
    private const int ScopeHeight = 8;
    private const string LeftColor = "#5fd7ff";
    private const string RightColor = "#ff87d7";
    private const string BothColor = "#ffffaf";
    private const string BarFilled = "#5fd7ff";
    private const string BarEmpty = "grey35";
    private const string Frame = "grey50";

    private readonly IAnsiConsole _console;
    private readonly TimeSpan _refreshInterval;
    private readonly object _gate = new();
    private readonly float[] _left = new float[WindowFrames];
    private readonly float[] _right = new float[WindowFrames];
    private int _count;
    private TimeSpan _elapsed;
    private MidiPlaybackInfo? _info;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _finished;

    public LiveMidiPlaybackView(IAnsiConsole console, TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMilliseconds(33);
    }

    public void Start(MidiPlaybackInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (_task is not null || _finished) return;
        _info = info;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(() => RunLive(token));
    }

    public void Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right, TimeSpan elapsed)
    {
        var n = Math.Min(left.Length, right.Length);
        lock (_gate)
        {
            _elapsed = elapsed;
            if (n == 0) return;
            if (n >= WindowFrames)
            {
                left.Slice(n - WindowFrames, WindowFrames).CopyTo(_left);
                right.Slice(n - WindowFrames, WindowFrames).CopyTo(_right);
                _count = WindowFrames;
                return;
            }

            var keep = Math.Min(_count, WindowFrames - n);
            Array.Copy(_left, _count - keep, _left, 0, keep);
            Array.Copy(_right, _count - keep, _right, 0, keep);
            left[..n].CopyTo(_left.AsSpan(keep));
            right[..n].CopyTo(_right.AsSpan(keep));
            _count = keep + n;
        }
    }

    /// <summary>Copy of the retained scope window (oldest first), for tests.</summary>
    internal (float[] Left, float[] Right) WindowSnapshot()
    {
        lock (_gate)
            return (_left.AsSpan(0, _count).ToArray(), _right.AsSpan(0, _count).ToArray());
    }

    public void Finish(TimeSpan elapsed)
    {
        if (!Teardown()) return;
        Print($"Played '{_info?.MidiPath}' ({MidiPlaybackTime.Format(elapsed)})");
    }

    public void Stop(TimeSpan elapsed)
    {
        if (!Teardown()) return;
        Print($"Stopped at {MidiPlaybackTime.Format(elapsed)}");
    }

    public void Dispose() => Teardown();

    /// <summary>Stops the live display. Returns true only for the call that performed the teardown.</summary>
    private bool Teardown()
    {
        if (_finished) return false;
        _finished = true;
        _cts?.Cancel();
        try
        {
            _task?.Wait();
        }
        catch (AggregateException)
        {
        }

        _cts?.Dispose();
        return _info is not null;
    }

    // Raw writer: Spectre renderables word-wrap at console width and interpret markup; the terminal should soft-wrap this line instead.
    private void Print(string line)
    {
        var writer = _console.Profile.Out.Writer;
        writer.Write(line + Environment.NewLine);
        writer.Flush();
    }

    private void RunLive(CancellationToken token)
    {
        _console.Live(Build()).AutoClear(true).Overflow(VerticalOverflow.Crop).Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx =>
            {
                ctx.Refresh();
                while (!token.IsCancellationRequested)
                {
                    token.WaitHandle.WaitOne(_refreshInterval);
                    if (token.IsCancellationRequested) break;
                    ctx.UpdateTarget(Build());
                    ctx.Refresh();
                }
            });
    }

    private IRenderable Build()
    {
        float[] left;
        float[] right;
        TimeSpan elapsed;
        lock (_gate)
        {
            left = _left.AsSpan(0, _count).ToArray();
            right = _right.AsSpan(0, _count).ToArray();
            elapsed = _elapsed;
        }

        var info = _info!;
        var scopeWidth = Math.Max(10, _console.Profile.Width - 4);
        var frame = OscilloscopeRenderer.Render(left, right, scopeWidth, ScopeHeight);

        var scope = new Rows(Enumerable.Range(0, frame.Height).Select(r => (IRenderable)new Markup(ScopeRow(frame, r))));
        var panel = new Panel(scope)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Frame),
            Padding = new Padding(0, 0),
            Expand = false,
        };

        var fraction = info.Length <= TimeSpan.Zero ? 1.0 : Math.Clamp(elapsed / info.Length, 0.0, 1.0);
        var time = $"{MidiPlaybackTime.Format(elapsed)} / {MidiPlaybackTime.Format(info.Length)}";
        var barWidth = Math.Max(4, scopeWidth + 2 - time.Length - 3);
        var bar = new Markup(" " + ProgressBar(fraction, barWidth) + $"  [bold]{Markup.Escape(time)}[/]");
        var title = new Markup($" [grey]Playing[/] [bold]'{Markup.Escape(info.MidiPath)}'[/] [grey]with[/] [bold]'{Markup.Escape(info.SoundFontPath)}'[/]");

        return new Rows(panel, bar, title);
    }

    private static string ScopeRow(OscilloscopeFrame frame, int row)
    {
        var sb = new StringBuilder();
        var text = frame.Rows[row];
        var col = 0;
        while (col < frame.Width)
        {
            var flags = frame.CellChannels[row, col];
            var start = col;
            while (col < frame.Width && frame.CellChannels[row, col] == flags) col++;
            if (flags == ScopeChannels.None)
            {
                sb.Append(' ', col - start);
                continue;
            }

            var color = flags == ScopeChannels.Both ? BothColor : flags == ScopeChannels.Left ? LeftColor : RightColor;
            sb.Append('[').Append(color).Append(']').Append(Markup.Escape(text.Substring(start, col - start))).Append("[/]");
        }

        return sb.ToString();
    }

    private static string ProgressBar(double fraction, int width)
    {
        var cells = fraction * width;
        var full = (int)Math.Floor(cells);
        if (full >= width) return $"[{BarFilled}]{new string('━', width)}[/]";
        var sb = new StringBuilder();
        if (full > 0) sb.Append('[').Append(BarFilled).Append(']').Append('━', full).Append("[/]");
        var remaining = width - full;
        if (cells - full >= 0.5)
        {
            sb.Append('[').Append(BarFilled).Append("]╸[/]");
            remaining--;
        }

        sb.Append('[').Append(BarEmpty).Append(']').Append('━', remaining).Append("[/]");
        return sb.ToString();
    }
}
