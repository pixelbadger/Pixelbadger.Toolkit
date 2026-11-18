using FluentAssertions;
using Pixelbadger.Toolkit.Services.Rag;
using Xunit;

namespace Pixelbadger.Toolkit.Tests;

public class RagIndexLocatorTests
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_ShouldUseDefaultName_WhenNothingGiven()
    {
        RagIndexLocator.Resolve(null, null, _home).Should().Be(Path.Combine(_home, ".pbtk", "rag", "default"));
        RagIndexLocator.Resolve("", null, _home).Should().Be(Path.Combine(_home, ".pbtk", "rag", "default"));
    }

    [Fact]
    public void Resolve_ShouldMapNameUnderHome_WhenNameGiven()
    {
        RagIndexLocator.Resolve("foo.bar_1-x", null, _home).Should().Be(Path.Combine(_home, ".pbtk", "rag", "foo.bar_1-x"));
    }

    [Fact]
    public void Resolve_ShouldPreferIndexPath_WhenBothGiven()
    {
        var path = Path.Combine(_home, "custom", "..", "idx");
        RagIndexLocator.Resolve("foo", path, _home).Should().Be(Path.GetFullPath(path));
        RagIndexLocator.Resolve("bad name!", path, _home).Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public void Resolve_ShouldMakeRelativePathAbsolute_WhenRelativeIndexPathGiven()
    {
        RagIndexLocator.Resolve(null, "rel/idx", _home).Should().Be(Path.GetFullPath("rel/idx"));
    }

    [Fact]
    public void Resolve_ShouldUseUserProfile_WhenHomeNotGiven()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pbtk", "rag", "x");
        RagIndexLocator.Resolve("x", null).Should().Be(expected);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("-dash")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a b")]
    [InlineData("..")]
    [InlineData("é")]
    public void Resolve_ShouldThrow_WhenNameInvalid(string name)
    {
        var act = () => RagIndexLocator.Resolve(name, null, _home);
        act.Should().Throw<ArgumentException>().WithMessage("Invalid index name*");
    }

    [Fact]
    public void Resolve_ShouldEnforceLengthLimit_WhenNameIs64Or65Chars()
    {
        RagIndexLocator.Resolve(new string('a', 64), null, _home).Should().EndWith(new string('a', 64));
        var act = () => RagIndexLocator.Resolve(new string('a', 65), null, _home);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Resolve_ShouldThrowPathException_WhenIndexPathEmptyOrWhitespace(string path)
    {
        var act = () => RagIndexLocator.Resolve("foo", path, _home);
        act.Should().Throw<RagIndexPathException>().WithMessage($"Invalid index path '{path}'.");
    }

    [Fact]
    public void Resolve_ShouldThrowPathException_WhenPathCannotBeResolved()
    {
        var act = () => RagIndexLocator.Resolve(null, "bad\0path", _home);
        act.Should().Throw<RagIndexPathException>().WithMessage("Invalid index path 'bad\0path'.");
    }

    [Fact]
    public void Resolve_ShouldThrowPathException_WhenHomeDirectoryEmpty()
    {
        var act = () => RagIndexLocator.Resolve("foo", null, "");
        act.Should().Throw<RagIndexPathException>().WithMessage("Cannot determine the home directory; use --index-path.");
    }

    [Fact]
    public void Resolve_ShouldNotRequireHome_WhenIndexPathGiven()
    {
        RagIndexLocator.Resolve(null, "x/idx", "").Should().Be(Path.GetFullPath("x/idx"));
    }

    [Fact]
    public void Resolve_ShouldThrowNameExceptionNotPathException_WhenNameInvalid()
    {
        var act = () => RagIndexLocator.Resolve("a/b", null, _home);
        act.Should().Throw<ArgumentException>().Which.Should().NotBeOfType<RagIndexPathException>();
    }
}
