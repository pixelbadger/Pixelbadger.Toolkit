using FluentAssertions;
using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

public class OpusLibraryTests
{
    private const string ExpectedMessage =
        "The native Opus library (libopus) was not found. Install it: Debian/Ubuntu 'apt install libopus0', macOS 'brew install opus', Windows: put opus.dll on PATH.";

    [Fact]
    public void VersionString_ShouldStartWithLibopus_WhenLibraryAvailable()
    {
        OpusLibrary.VersionString.Should().StartWith("libopus");
    }

    [Fact]
    public void EnsureAvailable_ShouldNotThrow_WhenLibraryAvailable()
    {
        var act = () => OpusLibrary.EnsureAvailable();
        act.Should().NotThrow();
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAvailable_ShouldThrowWithInstallGuidance_WhenNoCandidateLoads()
    {
        var act = () => OpusLibrary.EnsureAvailable(["libdoesnotexist.so.9"]);

        act.Should().Throw<OpusLibraryNotFoundException>().WithMessage(ExpectedMessage);
    }

    [Fact]
    public void EnsureAvailable_ShouldThrow_WhenCandidateListEmpty()
    {
        var act = () => OpusLibrary.EnsureAvailable(Array.Empty<string>());
        act.Should().Throw<OpusLibraryNotFoundException>();
    }

    [Fact]
    public void EnsureAvailable_ShouldSucceed_WhenOneCandidateLoads()
    {
        var act = () => OpusLibrary.EnsureAvailable(["libdoesnotexist.so.9", "libopus.so.0"]);
        act.Should().NotThrow();
    }

    [Fact]
    public void OpusException_ShouldExposeErrorCodeAndMessage()
    {
        var ex = new OpusException(-1, "opus_encode failed: invalid argument");
        ex.ErrorCode.Should().Be(-1);
        ex.Message.Should().Be("opus_encode failed: invalid argument");
    }

    [Fact]
    public void NativeResolver_ShouldNotBreakOtherImports_WhenOpusLoaded()
    {
        OpusLibrary.EnsureAvailable();
        // A different managed API backed by its own native lookup still works after the resolver is registered.
        System.Runtime.InteropServices.NativeLibrary.TryLoad("libc.so.6", out var handle).Should().BeTrue();
        System.Runtime.InteropServices.NativeLibrary.Free(handle);
    }
}
