using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class FfmpegLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FfmpegLocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static string FileName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    [Fact]
    public void Resolve_ShouldFindExecutable_WhenOnPath()
    {
        var expected = Path.Combine(_dir, FileName);
        File.WriteAllText(expected, "#!/bin/sh\n");
        var other = Path.Combine(_dir, "other");
        Directory.CreateDirectory(other);

        FfmpegLocator.Resolve(other + Path.PathSeparator + _dir).Should().Be(expected);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenPathIsEmpty()
    {
        FfmpegLocator.Resolve(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenDirectoryHasNoFfmpeg()
    {
        FfmpegLocator.Resolve(_dir).Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldSkipMalformedEntries_WhenPathContainsInvalidDirectory()
    {
        var expected = Path.Combine(_dir, FileName);
        File.WriteAllText(expected, "x");
        FfmpegLocator.Resolve("bad\0dir" + Path.PathSeparator + _dir).Should().Be(expected);
    }

    [Fact]
    public void Resolve_ShouldUseProcessPath_WhenNoArgument()
    {
        var resolved = FfmpegLocator.Resolve();
        resolved.Should().NotBeNull();
        File.Exists(resolved).Should().BeTrue();
    }
}
