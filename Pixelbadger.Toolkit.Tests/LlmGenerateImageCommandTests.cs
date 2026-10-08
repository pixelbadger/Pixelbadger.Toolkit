using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class LlmGenerateImageCommandTests
{
    private static Command Build() => LlmCommand.CreateGenerateImageCommand();

    [Fact]
    public void Parse_ShouldSucceed_WhenRequiredOptionsProvided()
    {
        var result = Build().Parse(new[] { "--prompt", "a cat", "--out-file", "cat.png" });

        result.Errors.Should().BeEmpty();
        result.GetValue<string>("--prompt").Should().Be("a cat");
        result.GetValue<string>("--out-file").Should().Be("cat.png");
        result.GetValue<string?>("--model").Should().BeNull();
        result.GetValue<string>("--provider").Should().Be("openai");
        result.GetValue<bool>("--overwrite").Should().BeFalse();
    }

    [Fact]
    public void Parse_ShouldSetOverwriteAndModel_WhenProvided()
    {
        var result = Build().Parse(new[] { "--prompt", "p", "--out-file", "o.png", "--model", "m", "--overwrite" });

        result.Errors.Should().BeEmpty();
        result.GetValue<string>("--model").Should().Be("m");
        result.GetValue<bool>("--overwrite").Should().BeTrue();
    }

    [Theory]
    [InlineData("--out-file", "o.png")]
    [InlineData("--prompt", "p")]
    public void Parse_ShouldReportError_WhenRequiredOptionMissing(string option, string value)
    {
        var result = Build().Parse(new[] { option, value });

        result.Errors.Should().NotBeEmpty();
    }
}
