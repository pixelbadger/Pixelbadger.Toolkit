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
        RagIndexLocator.Resolve("", "", _home).Should().Be(Path.Combine(_home, ".pbtk", "rag", "default"));
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
}
