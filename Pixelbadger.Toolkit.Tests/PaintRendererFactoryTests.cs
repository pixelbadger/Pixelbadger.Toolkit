using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class PaintRendererFactoryTests
{
    [Fact]
    public void Create_ShouldReturnDelta_WhenInteractiveAnsiTrueColor()
    {
        var console = PaintTestSupport.CreateConsole(new StringWriter());
        PaintRendererFactory.Create(console).Should().BeOfType<DeltaAnsiPaintRenderer>();
    }

    [Fact]
    public void Create_ShouldReturnSpectre_WhenNotInteractive()
    {
        var console = PaintTestSupport.CreateConsole(new StringWriter(), interactive: false);
        PaintRendererFactory.Create(console).Should().BeOfType<SpectrePaintRenderer>();
    }

    [Fact]
    public void Create_ShouldReturnSpectre_WhenColorSystemBelowTrueColor()
    {
        var console = PaintTestSupport.CreateConsole(new StringWriter(), colors: ColorSystemSupport.EightBit);
        PaintRendererFactory.Create(console).Should().BeOfType<SpectrePaintRenderer>();
    }

    [Fact]
    public void Create_ShouldReturnSpectre_WhenAnsiUnsupported()
    {
        var console = PaintTestSupport.CreateConsole(new StringWriter(), ansi: false);
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        PaintRendererFactory.Create(console).Should().BeOfType<SpectrePaintRenderer>();
    }

    [Fact]
    public void Create_ShouldPassInteractiveFlag_WhenFallingBackToSpectre()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, interactive: true, colors: ColorSystemSupport.Standard);
        using var r = PaintRendererFactory.Create(console);
        r.Should().BeOfType<SpectrePaintRenderer>();
        r.LogToolCall("x");
        w.ToString().Should().BeEmpty("interactive Spectre fallback logs nothing per tool call");
    }
}
