using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pixelbadger.Toolkit.Components;

public sealed record PaintToolOutcome(FunctionResultContent Result, bool CanvasChanged, bool Finished, string Description);

/// <summary>The seven painting tools exposed to the model, plus validated invocation.</summary>
public class PaintTools
{
    private readonly PaintCanvas _canvas;

    public IReadOnlyList<AITool> Tools { get; }

    public PaintTools(PaintCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        const string c = "Canvas is 256x256, origin top-left (x right, y down), colours are #RRGGBB.";
        Tools =
        [
            Declare("read_canvas", "Return the whole canvas as a PNG image. " + c, Schema([], [])),
            Declare("read_region", "Return exact #RRGGBB values of a region; width and height 1..32, region fully inside the canvas. " + c,
                Schema(Ints("x", "y", "width", "height"), ["x", "y", "width", "height"])),
            Declare("set_pixels", "Set individual pixels; at most 4096 per call, out-of-canvas entries are skipped. " + c,
                Schema([("pixels", """{"type":"array","items":{"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"integer"},"color":{"type":"string"}},"required":["x","y","color"]}}""")], ["pixels"])),
            Declare("fill_rect", "Fill a rectangle (clipped to the canvas); width and height >= 1. " + c,
                Schema([.. Ints("x", "y", "width", "height"), ("color", StrSchema)], ["x", "y", "width", "height", "color"])),
            Declare("draw_line", "Draw a line between two points inclusive (clipped); thickness 1..16, default 1. " + c,
                Schema([.. Ints("x0", "y0", "x1", "y1"), ("color", StrSchema), ("thickness", IntSchema)], ["x0", "y0", "x1", "y1", "color"])),
            Declare("fill_ellipse", "Fill an ellipse centred at (cx,cy) with radii rx,ry >= 0 (clipped). " + c,
                Schema([.. Ints("cx", "cy", "rx", "ry"), ("color", StrSchema)], ["cx", "cy", "rx", "ry", "color"])),
            Declare("finish", "Call when the painting is done to end the run, with a short summary.",
                Schema([("summary", StrSchema)], ["summary"])),
        ];
    }

    private const string IntSchema = """{"type":"integer"}""";
    private const string StrSchema = """{"type":"string"}""";

    private static (string, string)[] Ints(params string[] names) => names.Select(n => (n, IntSchema)).ToArray();

    private static JsonElement Schema((string Name, string Json)[] props, string[] required)
    {
        var p = string.Join(",", props.Select(x => $"\"{x.Name}\":{x.Json}"));
        var r = string.Join(",", required.Select(x => $"\"{x}\""));
        using var doc = JsonDocument.Parse($"{{\"type\":\"object\",\"properties\":{{{p}}},\"required\":[{r}]}}");
        return doc.RootElement.Clone();
    }

    private static AITool Declare(string name, string description, JsonElement schema) =>
        AIFunctionFactory.CreateDeclaration(name, description, schema);

    public Task<PaintToolOutcome> InvokeAsync(FunctionCallContent call, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        return Task.FromResult(Invoke(call));
    }

    private PaintToolOutcome Invoke(FunctionCallContent call)
    {
        var args = call.Arguments ?? new Dictionary<string, object?>();
        try
        {
            switch (call.Name)
            {
                case "read_canvas":
                {
                    var content = new List<AIContent>
                    {
                        new TextContent("Canvas 256x256 PNG (origin top-left)."),
                        new DataContent(_canvas.ToPng(), "image/png"),
                    };
                    return new(new FunctionResultContent(call.CallId, content), false, false, "read_canvas");
                }
                case "read_region":
                {
                    int x = Int32(args, "x"), y = Int32(args, "y"), w = Int32(args, "width"), h = Int32(args, "height");
                    var text = _canvas.ReadRegion(x, y, w, h);
                    return new(new FunctionResultContent(call.CallId, text), false, false, $"read_region({x},{y},{w},{h})");
                }
                case "set_pixels":
                {
                    var pixels = ParsePixels(args);
                    var n = _canvas.SetPixels(pixels);
                    return Painted(call, n, $"set_pixels({pixels.Count} px)");
                }
                case "fill_rect":
                {
                    int x = Int32(args, "x"), y = Int32(args, "y"), w = Int32(args, "width"), h = Int32(args, "height");
                    var col = Color(args, "color");
                    var n = _canvas.FillRect(x, y, w, h, col);
                    return Painted(call, n, $"fill_rect({x},{y},{w},{h},{PaintCanvas.FormatColor(col)})");
                }
                case "draw_line":
                {
                    int x0 = Int32(args, "x0"), y0 = Int32(args, "y0"), x1 = Int32(args, "x1"), y1 = Int32(args, "y1");
                    var col = Color(args, "color");
                    var t = Has(args, "thickness") ? Int32(args, "thickness") : 1;
                    var n = _canvas.DrawLine(x0, y0, x1, y1, col, t);
                    return Painted(call, n, $"draw_line({x0},{y0},{x1},{y1},{PaintCanvas.FormatColor(col)},{t})");
                }
                case "fill_ellipse":
                {
                    int cx = Int32(args, "cx"), cy = Int32(args, "cy"), rx = Int32(args, "rx"), ry = Int32(args, "ry");
                    var col = Color(args, "color");
                    var n = _canvas.FillEllipse(cx, cy, rx, ry, col);
                    return Painted(call, n, $"fill_ellipse({cx},{cy},{rx},{ry},{PaintCanvas.FormatColor(col)})");
                }
                case "finish":
                {
                    var summary = Str(args, "summary");
                    return new(new FunctionResultContent(call.CallId, "Finished."), false, true, $"finish: {summary}");
                }
                default:
                    throw new ArgumentException($"Unknown tool '{call.Name}'.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = new FunctionResultContent(call.CallId, $"Error: {ex.Message}") { Exception = ex };
            return new(result, false, false, $"{call.Name}(...) → error: {ex.Message}");
        }
    }

    private static PaintToolOutcome Painted(FunctionCallContent call, int n, string desc) =>
        new(new FunctionResultContent(call.CallId, $"Painted {n} pixels."), n > 0, false, $"{desc} → {n} px");

    private static bool Has(IDictionary<string, object?> args, string name) =>
        args.TryGetValue(name, out var v) && v is not null && !(v is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined });

    private static object Require(IDictionary<string, object?> args, string name)
    {
        if (!Has(args, name)) throw new ArgumentException($"Missing argument '{name}'.");
        return args[name]!;
    }

    private static int Int32(IDictionary<string, object?> args, string name) => ToInt(Require(args, name), name);

    private static int ToInt(object v, string name)
    {
        switch (v)
        {
            case JsonElement { ValueKind: JsonValueKind.Number } je:
                if (je.TryGetInt32(out var i)) return i;
                if (je.TryGetDouble(out var jd) && IsIntegral(jd)) return (int)jd;
                break;
            case int n: return n;
            case long l when l >= int.MinValue && l <= int.MaxValue: return (int)l;
            case short or byte or sbyte or ushort: return Convert.ToInt32(v);
            case double d when IsIntegral(d): return (int)d;
            case float f when IsIntegral(f): return (int)f;
            case decimal m when m == decimal.Truncate(m) && m >= int.MinValue && m <= int.MaxValue: return (int)m;
        }
        throw new ArgumentException($"Argument '{name}' must be an integer.");
    }

    private static bool IsIntegral(double d) => d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue;

    private static string Str(IDictionary<string, object?> args, string name) => ToStr(Require(args, name), name);

    private static string ToStr(object v, string name) => v switch
    {
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
        string s => s,
        _ => throw new ArgumentException($"Argument '{name}' must be a string."),
    };

    private static PixelColor Color(IDictionary<string, object?> args, string name) => ParseColor(Str(args, name));

    private static PixelColor ParseColor(string text)
    {
        if (!PaintCanvas.TryParseColor(text, out var col))
            throw new ArgumentException($"Invalid colour '{text}'. Use #RRGGBB.");
        return col;
    }

    private static List<(int X, int Y, PixelColor Color)> ParsePixels(IDictionary<string, object?> args)
    {
        var raw = Require(args, "pixels");
        var items = new List<object?>();
        switch (raw)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } je:
                foreach (var e in je.EnumerateArray()) items.Add(e);
                break;
            case string:
                throw new ArgumentException("Argument 'pixels' must be an array.");
            case IEnumerable en:
                foreach (var e in en) items.Add(e);
                break;
            default:
                throw new ArgumentException("Argument 'pixels' must be an array.");
        }
        if (items.Count > PaintCanvas.MaxPixelsPerCall)
            throw new ArgumentException($"Too many pixels: {items.Count}. At most {PaintCanvas.MaxPixelsPerCall} per call.");

        var result = new List<(int, int, PixelColor)>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var entry = ToEntry(items[i], i);
            result.Add((ToInt(Require(entry, "x"), $"pixels[{i}].x"),
                        ToInt(Require(entry, "y"), $"pixels[{i}].y"),
                        ParseColor(ToStr(Require(entry, "color"), $"pixels[{i}].color"))));
        }
        return result;
    }

    private static IDictionary<string, object?> ToEntry(object? item, int index)
    {
        switch (item)
        {
            case JsonElement { ValueKind: JsonValueKind.Object } je:
                var map = new Dictionary<string, object?>();
                foreach (var p in je.EnumerateObject()) map[p.Name] = p.Value;
                return map;
            case IDictionary<string, object?> dict:
                return dict;
            default:
                throw new ArgumentException($"pixels[{index}] must be an object with x, y and color.");
        }
    }
}
