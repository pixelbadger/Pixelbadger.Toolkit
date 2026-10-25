using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class DfrotzLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DfrotzLocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Resolve_ShouldReturnFullPath_WhenConfiguredFileExists()
    {
        var file = Path.Combine(_dir, "my-frotz");
        File.WriteAllText(file, "x");

        DfrotzLocator.Resolve(file).Should().Be(file);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenConfiguredFileMissing_EvenIfPathHasDfrotz()
    {
        File.WriteAllText(Path.Combine(_dir, "dfrotz"), "x");

        DfrotzLocator.Resolve(Path.Combine(_dir, "nope"), _dir).Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldFindDfrotz_WhenOnSuppliedPath()
    {
        var file = Path.Combine(_dir, "dfrotz");
        File.WriteAllText(file, "x");
        var path = string.Join(Path.PathSeparator, Path.Combine(_dir, "missing"), _dir);

        DfrotzLocator.Resolve(null, path).Should().Be(file);
    }

    [Fact]
    public void Resolve_ShouldReturnFallback_WhenNotOnPathAndFallbackExists()
    {
        var fallback = Path.Combine(_dir, "fallback-dfrotz");
        File.WriteAllText(fallback, "x");

        DfrotzLocator.Resolve(null, Path.Combine(_dir, "empty"), fallback).Should().Be(fallback);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenNotOnPathAndFallbackMissing()
    {
        DfrotzLocator.Resolve(null, _dir, Path.Combine(_dir, "no-such-fallback")).Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldIgnoreEmptyPath_AndStillFallBack()
    {
        var fallback = Path.Combine(_dir, "fallback-dfrotz");
        File.WriteAllText(fallback, "x");

        DfrotzLocator.Resolve(null, "", fallback).Should().Be(fallback);
    }

    [Fact]
    public void Resolve_ShouldPreferPathOverFallback_WhenBothExist()
    {
        var onPath = Path.Combine(_dir, "dfrotz");
        var fallback = Path.Combine(_dir, "fallback-dfrotz");
        File.WriteAllText(onPath, "x");
        File.WriteAllText(fallback, "x");

        DfrotzLocator.Resolve(null, _dir, fallback).Should().Be(onPath);
    }
}
