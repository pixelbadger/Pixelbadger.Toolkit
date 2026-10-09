using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;

namespace Pixelbadger.Toolkit.Tests;

public class ImmutableDefaultExtensionsTests
{
    [Fact]
    public void HasImmutableDefault_ShouldBeFalse_WhenOptionNotMarked()
    {
        new Option<string>("--a").HasImmutableDefault().Should().BeFalse();
    }

    [Fact]
    public void WithImmutableDefault_ShouldMarkOptionAndReturnSameInstance_WhenCalled()
    {
        var option = new Option<int>("--a");

        var returned = option.WithImmutableDefault();

        returned.Should().BeSameAs(option);
        option.HasImmutableDefault().Should().BeTrue();
    }

    [Fact]
    public void WithImmutableDefault_ShouldBeIdempotent_WhenCalledTwice()
    {
        var option = new Option<int>("--a").WithImmutableDefault().WithImmutableDefault();

        option.HasImmutableDefault().Should().BeTrue();
    }

    [Fact]
    public void HasImmutableDefault_ShouldNotLeakToOtherOptions_WhenOneIsMarked()
    {
        var marked = new Option<int>("--a").WithImmutableDefault();
        var other = new Option<int>("--a");

        marked.HasImmutableDefault().Should().BeTrue();
        other.HasImmutableDefault().Should().BeFalse();
    }

    [Fact]
    public void WithImmutableDefault_ShouldThrow_WhenOptionNull()
    {
        var act = () => ((Option<int>)null!).WithImmutableDefault();

        act.Should().Throw<ArgumentNullException>();
    }
}
