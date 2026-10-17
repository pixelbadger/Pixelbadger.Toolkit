using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Tests;

public class PaintToolsTests
{
    private readonly PaintCanvas _canvas = new();
    private readonly PaintTools _tools;

    public PaintToolsTests()
    {
        _tools = new PaintTools(_canvas);
    }

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static FunctionCallContent Call(string name, Dictionary<string, object?>? args = null) =>
        new("call-1", name, args);

    private static FunctionCallContent JsonCall(string name, string argsJson)
    {
        var root = J(argsJson);
        var d = new Dictionary<string, object?>();
        foreach (var p in root.EnumerateObject()) d[p.Name] = p.Value;
        return new FunctionCallContent("call-1", name, d);
    }

    private static string Text(PaintToolOutcome o) => (string)o.Result.Result!;

    private static void ShouldBeError(PaintToolOutcome o, string? contains = null)
    {
        Text(o).Should().StartWith("Error: ");
        if (contains != null) Text(o).Should().Contain(contains);
        o.Result.Exception.Should().NotBeNull();
        o.CanvasChanged.Should().BeFalse();
        o.Finished.Should().BeFalse();
        o.Result.CallId.Should().Be("call-1");
    }

    private JsonElement Schema(string name)
    {
        var tool = _tools.Tools.Single(t => t.Name == name);
        return ((AIFunctionDeclaration)tool).JsonSchema;
    }

    [Fact]
    public void Tools_ShouldExposeExactlySevenNamedTools_WhenCreated()
    {
        _tools.Tools.Select(t => t.Name).Should().Equal(
            "read_canvas", "read_region", "set_pixels", "fill_rect", "draw_line", "fill_ellipse", "finish");
        _tools.Tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Description));
    }

    [Theory]
    [InlineData("read_canvas", new string[0], new string[0])]
    [InlineData("read_region", new[] { "x", "y", "width", "height" }, new[] { "x", "y", "width", "height" })]
    [InlineData("set_pixels", new[] { "pixels" }, new[] { "pixels" })]
    [InlineData("fill_rect", new[] { "x", "y", "width", "height", "color" }, new[] { "x", "y", "width", "height", "color" })]
    [InlineData("draw_line", new[] { "x0", "y0", "x1", "y1", "color", "thickness" }, new[] { "x0", "y0", "x1", "y1", "color" })]
    [InlineData("fill_ellipse", new[] { "cx", "cy", "rx", "ry", "color" }, new[] { "cx", "cy", "rx", "ry", "color" })]
    [InlineData("finish", new[] { "summary" }, new[] { "summary" })]
    public void Tools_ShouldDeclareParametersAndRequired_WhenSchemaInspected(string name, string[] props, string[] required)
    {
        var schema = Schema(name);
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(props);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(required);
    }

    [Fact]
    public void Tools_ShouldDeclareParameterTypes_WhenSchemaInspected()
    {
        var props = Schema("draw_line").GetProperty("properties");
        props.GetProperty("x0").GetProperty("type").GetString().Should().Be("integer");
        props.GetProperty("color").GetProperty("type").GetString().Should().Be("string");
        props.GetProperty("thickness").GetProperty("type").GetString().Should().Be("integer");
        var items = Schema("set_pixels").GetProperty("properties").GetProperty("pixels");
        items.GetProperty("type").GetString().Should().Be("array");
        items.GetProperty("items").GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("x", "y", "color");
    }

    [Fact]
    public void Tools_ShouldMentionCanvasConventions_WhenDescribed()
    {
        _tools.Tools.Single(t => t.Name == "fill_rect").Description.Should().Contain("256x256").And.Contain("#RRGGBB");
    }

    [Fact]
    public async Task InvokeAsync_ShouldFillRectFromJsonElements_WhenValid()
    {
        var o = await _tools.InvokeAsync(JsonCall("fill_rect", """{"x":10,"y":10,"width":20,"height":20,"color":"#ff0000"}"""));
        Text(o).Should().Be("Painted 400 pixels.");
        o.CanvasChanged.Should().BeTrue();
        o.Finished.Should().BeFalse();
        o.Result.CallId.Should().Be("call-1");
        o.Result.Exception.Should().BeNull();
        o.Description.Should().Be("fill_rect(10,10,20,20,#FF0000) → 400 px");
        _canvas.GetPixel(10, 10).Should().Be(PixelColor.Red);
        _canvas.GetPixel(29, 29).Should().Be(PixelColor.Red);
        _canvas.GetPixel(30, 30).Should().Be(PixelColor.White);
    }

    [Fact]
    public async Task InvokeAsync_ShouldFillRectFromClrValues_WhenIntLongAndDouble()
    {
        var o = await _tools.InvokeAsync(Call("fill_rect", new()
        {
            ["x"] = 1, ["y"] = 2L, ["width"] = 3.0, ["height"] = (short)2, ["color"] = "00FF00",
        }));
        Text(o).Should().Be("Painted 6 pixels.");
        _canvas.GetPixel(3, 3).Should().Be(new PixelColor(0, 255, 0));
    }

    [Fact]
    public async Task InvokeAsync_ShouldReportZeroAndNotChange_WhenShapeFullyOffCanvas()
    {
        var o = await _tools.InvokeAsync(JsonCall("fill_rect", """{"x":500,"y":500,"width":5,"height":5,"color":"#000000"}"""));
        Text(o).Should().Be("Painted 0 pixels.");
        o.CanvasChanged.Should().BeFalse();
        o.Result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetPixelsFromJsonArray_WhenValid()
    {
        var o = await _tools.InvokeAsync(JsonCall("set_pixels",
            """{"pixels":[{"x":1,"y":1,"color":"#0000FF"},{"x":2,"y":1,"color":"#0000ff"},{"x":999,"y":1,"color":"#000000"}]}"""));
        Text(o).Should().Be("Painted 2 pixels.");
        o.CanvasChanged.Should().BeTrue();
        o.Description.Should().Be("set_pixels(3 px) → 2 px");
        _canvas.GetPixel(2, 1).Should().Be(new PixelColor(0, 0, 255));
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetPixelsFromClrCollections_WhenNestedDictionaries()
    {
        var pixels = new List<object?>
        {
            new Dictionary<string, object?> { ["x"] = 4L, ["y"] = 5L, ["color"] = "#112233" },
            new Dictionary<string, object> { ["x"] = 6, ["y"] = 7, ["color"] = "#445566" },
        };
        var o = await _tools.InvokeAsync(Call("set_pixels", new() { ["pixels"] = pixels }));
        Text(o).Should().Be("Painted 2 pixels.");
        _canvas.GetPixel(4, 5).Should().Be(new PixelColor(0x11, 0x22, 0x33));
        _canvas.GetPixel(6, 7).Should().Be(new PixelColor(0x44, 0x55, 0x66));
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenSetPixelsOverLimit()
    {
        var arr = string.Join(",", Enumerable.Range(0, 4097).Select(i => $"{{\"x\":{i % 256},\"y\":{i / 256},\"color\":\"#000000\"}}"));
        var o = await _tools.InvokeAsync(JsonCall("set_pixels", $"{{\"pixels\":[{arr}]}}"));
        ShouldBeError(o, "4097");
        _canvas.GetPixel(0, 0).Should().Be(PixelColor.White);
    }

    [Fact]
    public async Task InvokeAsync_ShouldAcceptSetPixelsAtLimit_When4096Entries()
    {
        var arr = string.Join(",", Enumerable.Range(0, 4096).Select(i => $"{{\"x\":{i % 256},\"y\":{i / 256},\"color\":\"#000000\"}}"));
        var o = await _tools.InvokeAsync(JsonCall("set_pixels", $"{{\"pixels\":[{arr}]}}"));
        Text(o).Should().Be("Painted 4096 pixels.");
    }

    [Theory]
    [InlineData("""{"pixels":"nope"}""")]
    [InlineData("""{"pixels":5}""")]
    [InlineData("""{"pixels":[5]}""")]
    [InlineData("""{"pixels":[{"x":1,"y":1}]}""")]
    [InlineData("""{"pixels":[{"x":"a","y":1,"color":"#000000"}]}""")]
    [InlineData("""{"pixels":[{"x":1,"y":1,"color":"red"}]}""")]
    [InlineData("""{}""")]
    public async Task InvokeAsync_ShouldReturnError_WhenSetPixelsMalformed(string json)
    {
        ShouldBeError(await _tools.InvokeAsync(JsonCall("set_pixels", json)));
    }

    [Fact]
    public async Task InvokeAsync_ShouldDrawLineWithDefaultThickness_WhenThicknessOmitted()
    {
        var o = await _tools.InvokeAsync(JsonCall("draw_line", """{"x0":0,"y0":0,"x1":3,"y1":3,"color":"#000000"}"""));
        Text(o).Should().Be("Painted 4 pixels.");
        o.Description.Should().Be("draw_line(0,0,3,3,#000000,1) → 4 px");
    }

    [Fact]
    public async Task InvokeAsync_ShouldDrawThickLine_WhenThicknessGiven()
    {
        var o = await _tools.InvokeAsync(JsonCall("draw_line", """{"x0":5,"y0":5,"x1":7,"y1":5,"color":"#000000","thickness":3}"""));
        Text(o).Should().Be("Painted 15 pixels.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task InvokeAsync_ShouldReturnError_WhenThicknessOutOfRange(int t)
    {
        var o = await _tools.InvokeAsync(JsonCall("draw_line", $"{{\"x0\":0,\"y0\":0,\"x1\":3,\"y1\":3,\"color\":\"#000000\",\"thickness\":{t}}}"));
        ShouldBeError(o, "Thickness");
        o.Description.Should().Contain("draw_line").And.Contain("→ error: ");
    }

    [Fact]
    public async Task InvokeAsync_ShouldFillEllipse_WhenValid()
    {
        var o = await _tools.InvokeAsync(JsonCall("fill_ellipse", """{"cx":10,"cy":10,"rx":2,"ry":1,"color":"#123456"}"""));
        Text(o).Should().Be("Painted 11 pixels.");
        o.CanvasChanged.Should().BeTrue();
        _canvas.GetPixel(12, 10).Should().Be(new PixelColor(0x12, 0x34, 0x56));
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenEllipseRadiusNegative()
    {
        ShouldBeError(await _tools.InvokeAsync(JsonCall("fill_ellipse", """{"cx":10,"cy":10,"rx":-1,"ry":1,"color":"#123456"}""")));
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenRectSizeNotPositive()
    {
        ShouldBeError(await _tools.InvokeAsync(JsonCall("fill_rect", """{"x":0,"y":0,"width":0,"height":3,"color":"#000000"}""")));
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnSpecificError_WhenColourInvalid()
    {
        var o = await _tools.InvokeAsync(JsonCall("fill_rect", """{"x":0,"y":0,"width":2,"height":2,"color":"blue"}"""));
        ShouldBeError(o);
        Text(o).Should().Be("Error: Invalid colour 'blue'. Use #RRGGBB.");
        _canvas.GetPixel(0, 0).Should().Be(PixelColor.White);
    }

    [Theory]
    [InlineData("""{"x":0,"y":0,"width":2,"color":"#000000"}""")]                       // missing
    [InlineData("""{"x":"0","y":0,"width":2,"height":2,"color":"#000000"}""")]          // string for int
    [InlineData("""{"x":0.5,"y":0,"width":2,"height":2,"color":"#000000"}""")]          // fractional
    [InlineData("""{"x":0,"y":0,"width":2,"height":2,"color":5}""")]                    // int for string
    [InlineData("""{"x":null,"y":0,"width":2,"height":2,"color":"#000000"}""")]         // null
    [InlineData("""{"x":99999999999,"y":0,"width":2,"height":2,"color":"#000000"}""")]  // overflow
    [InlineData("""{"x":true,"y":0,"width":2,"height":2,"color":"#000000"}""")]         // bool
    public async Task InvokeAsync_ShouldReturnError_WhenArgumentsMissingOrMistyped(string json)
    {
        ShouldBeError(await _tools.InvokeAsync(JsonCall("fill_rect", json)));
        _canvas.GetPixel(0, 0).Should().Be(PixelColor.White);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenArgumentsNull()
    {
        ShouldBeError(await _tools.InvokeAsync(new FunctionCallContent("call-1", "fill_rect", null)), "Missing");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenToolUnknown()
    {
        var o = await _tools.InvokeAsync(Call("erase_all"));
        ShouldBeError(o, "erase_all");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnRegionText_WhenReadRegionValid()
    {
        _canvas.SetPixels([(0, 0, PixelColor.Red)]);
        var o = await _tools.InvokeAsync(JsonCall("read_region", """{"x":0,"y":0,"width":2,"height":1}"""));
        Text(o).Should().Be("Region x=0 y=0 width=2 height=1 (rows top to bottom, pixels left to right)\ny=0: #FF0000 #FFFFFF");
        o.CanvasChanged.Should().BeFalse();
        o.Finished.Should().BeFalse();
        o.Result.Exception.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"x":250,"y":0,"width":10,"height":1}""")]
    [InlineData("""{"x":0,"y":0,"width":33,"height":1}""")]
    [InlineData("""{"x":0,"y":0,"width":1}""")]
    public async Task InvokeAsync_ShouldReturnError_WhenReadRegionInvalid(string json)
    {
        ShouldBeError(await _tools.InvokeAsync(JsonCall("read_region", json)));
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnTextAndPng_WhenReadCanvas()
    {
        _canvas.FillRect(0, 0, 4, 4, PixelColor.Red);
        var o = await _tools.InvokeAsync(Call("read_canvas"));
        o.CanvasChanged.Should().BeFalse();
        o.Finished.Should().BeFalse();
        o.Description.Should().Be("read_canvas");
        var items = o.Result.Result.Should().BeAssignableTo<List<AIContent>>().Subject;
        items.Should().HaveCount(2);
        items[0].Should().BeOfType<TextContent>().Which.Text.Should().Be("Canvas 256x256 PNG (origin top-left).");
        var data = items[1].Should().BeOfType<DataContent>().Subject;
        data.MediaType.Should().Be("image/png");
        using var image = Image.Load<Rgb24>(data.Data.ToArray());
        image.Width.Should().Be(256);
        image[0, 0].Should().Be(new Rgb24(255, 0, 0));
        image[10, 10].Should().Be(new Rgb24(255, 255, 255));
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetFinished_WhenFinishCalled()
    {
        var o = await _tools.InvokeAsync(JsonCall("finish", """{"summary":"A badger"}"""));
        Text(o).Should().Be("Finished.");
        o.Finished.Should().BeTrue();
        o.CanvasChanged.Should().BeFalse();
        o.Description.Should().Be("finish: A badger");
        o.Result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenFinishSummaryMissing()
    {
        ShouldBeError(await _tools.InvokeAsync(Call("finish", new())));
    }

    [Fact]
    public async Task InvokeAsync_ShouldThrowOperationCanceled_WhenTokenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await _tools.InvokeAsync(Call("read_canvas"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
