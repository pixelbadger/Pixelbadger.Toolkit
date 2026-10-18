using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class McpArgumentMapperTests
{
    private static McpToolDefinition Tool()
    {
        var cmd = new Command("run", "Run")
        {
            new Option<string>("--name"), new Option<int>("--count"), new Option<double>("--ratio"),
            new Option<bool>("--flag"), new Option<string[]>("--tags")
        };
        var root = new RootCommand { new Command("grp", "G") { cmd } };
        return McpToolCatalogue.Describe(cmd);
    }

    private static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void Map_ShouldProduceTokensInOptionOrder_WhenAllKindsGiven()
    {
        var result = McpArgumentMapper.Map(Tool(),
            Args("""{"tags":["a","b"],"flag":false,"ratio":1.5,"count":3,"name":"x y"}"""));

        result.Error.Should().BeNull();
        result.Tokens.Should().Equal("grp", "run", "--name", "x y", "--count", "3", "--ratio", "1.5", "--flag", "false",
            "--tags", "a", "--tags", "b");
    }

    [Fact]
    public void Map_ShouldPassBooleanExplicitly_WhenTrue()
    {
        McpArgumentMapper.Map(Tool(), Args("""{"flag":true}""")).Tokens.Should().Equal("grp", "run", "--flag", "true");
    }

    [Fact]
    public void Map_ShouldOmitOption_WhenValueIsNull()
    {
        McpArgumentMapper.Map(Tool(), Args("""{"name":null,"count":2}""")).Tokens.Should().Equal("grp", "run", "--count", "2");
    }

    [Fact]
    public void Map_ShouldReturnOnlyPath_WhenArgumentsAreNullOrEmpty()
    {
        McpArgumentMapper.Map(Tool(), null).Tokens.Should().Equal("grp", "run");
        McpArgumentMapper.Map(Tool(), new Dictionary<string, JsonElement>()).Tokens.Should().Equal("grp", "run");
    }

    [Fact]
    public void Map_ShouldReturnError_WhenArgumentIsUnknown()
    {
        var result = McpArgumentMapper.Map(Tool(), Args("""{"name":"x","bogus":1}"""));

        result.Tokens.Should().BeNull();
        result.Error.Should().Be("Unknown argument 'bogus' for tool grp_run.");
    }

    [Theory]
    [InlineData("""{"name":{"a":1}}""")]
    [InlineData("""{"tags":[{"a":1}]}""")]
    [InlineData("""{"tags":[null]}""")]
    [InlineData("""{"tags":[["x"]]}""")]
    public void Map_ShouldReturnError_WhenValueIsObjectOrNestedStructure(string json)
    {
        var result = McpArgumentMapper.Map(Tool(), Args(json));

        result.Tokens.Should().BeNull();
        result.Error.Should().Contain("has an unsupported type.").And.Contain("for tool grp_run");
    }

    [Fact]
    public void Map_ShouldAcceptScalarsInArrays_WhenElementsAreNumbersAndBooleans()
    {
        McpArgumentMapper.Map(Tool(), Args("""{"tags":["a",2,true]}""")).Tokens
            .Should().Equal("grp", "run", "--tags", "a", "--tags", "2", "--tags", "true");
    }

    [Fact]
    public void Map_ShouldUseInvariantCulture_WhenCurrentCultureUsesCommaDecimals()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = McpArgumentMapper.Map(Tool(), Args("""{"ratio":1.5}"""));

            result.Tokens.Should().Equal("grp", "run", "--ratio", "1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
