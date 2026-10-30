using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class McpToolCatalogueTests
{
    private readonly RootCommand _root = ToolkitCommandTree.Create(new InMemoryConfigStore());

    private McpToolDefinition Tool(string entry) =>
        McpToolCatalogue.Build(_root, McpEntryResolver.Resolve(_root, entry)!).Single();

    private static JsonNode Node(JsonElement element) => JsonNode.Parse(element.GetRawText())!;

    [Fact]
    public void Build_ShouldReturnNameDescriptionAndExactSchema_WhenCommandIsBrainfuck()
    {
        var tool = Tool("interpreters/brainfuck");

        tool.Name.Should().Be("interpreters_brainfuck");
        tool.Description.Should().Be("Executes a Brainfuck program from a file");
        var expected = JsonNode.Parse("""
            {"type":"object","properties":{"file":{"type":"string","description":"Path to the Brainfuck program file"}},
             "required":["file"],"additionalProperties":false}
            """)!;
        JsonNode.DeepEquals(Node(tool.InputSchema), expected).Should().BeTrue(tool.InputSchema.GetRawText());
    }

    [Fact]
    public void Build_ShouldJoinPathWithUnderscore_WhenCommandIsNested()
    {
        Tool("llm/history/list").Name.Should().Be("llm_history_list");
        Tool("llm/history/list").CommandPath.Should().Equal("llm", "history", "list");
    }

    [Fact]
    public void Build_ShouldMapOptionTypes_WhenCommandIsGptTrain()
    {
        var schema = Node(Tool("gpt/train").InputSchema);
        var props = schema["properties"]!.AsObject();

        props["steps"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["lr"]!["type"]!.GetValue<string>().Should().Be("number");
        props["validation-split"]!["type"]!.GetValue<string>().Should().Be("number");
        props["resume"]!["type"]!.GetValue<string>().Should().Be("boolean");
        props["source"]!["type"]!.GetValue<string>().Should().Be("string");
        props["loss-graph"]!["type"]!.GetValue<string>().Should().Be("string");
        props["tokenizer"]!["type"]!.GetValue<string>().Should().Be("string");
        props["tokenizer"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().BeEquivalentTo("Char", "Bpe");
        props.ContainsKey("help").Should().BeFalse();
        props.ContainsKey("version").Should().BeFalse();
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("source");
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Build_ShouldMapNullableInteger_WhenOptionIsNullableInt()
    {
        var props = Node(Tool("crypto/substring").InputSchema)["properties"]!.AsObject();

        props["length"]!["type"]!.GetValue<string>().Should().Be("integer");
    }

    [Fact]
    public void Build_ShouldMapArrayOfStrings_WhenOptionIsStringArray()
    {
        var props = Node(Tool("llm/corpospeak").InputSchema)["properties"]!.AsObject();

        JsonNode.DeepEquals(props["user-messages"]!["items"], JsonNode.Parse("""{"type":"string"}""")).Should().BeTrue();
        props["user-messages"]!["type"]!.GetValue<string>().Should().Be("array");
    }

    [Fact]
    public void Build_ShouldOmitRequired_WhenNoOptionIsRequired()
    {
        var schema = Node(Tool("llm/history/list").InputSchema).AsObject();

        schema.ContainsKey("required").Should().BeFalse();
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["properties"]!.AsObject().Count.Should().Be(0);
    }

    [Theory]
    [InlineData("audio/record", "audio_record", "out-file")]
    [InlineData("audio/insert", "audio_insert", "file,at")]
    [InlineData("audio/overwrite", "audio_overwrite", "file,at")]
    public void Build_ShouldExposeAudioTools_WhenCatalogueIsBuiltFromTree(string entry, string name, string required)
    {
        var tool = Tool(entry);
        var schema = Node(tool.InputSchema);
        var props = schema["properties"]!.AsObject();

        tool.Name.Should().Be(name);
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(required.Split(','));
        props["duration"]!["type"]!.GetValue<string>().Should().Be("string");
        props["from-wav"]!["type"]!.GetValue<string>().Should().Be("string");
        props["bitrate"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["device"]!["type"]!.GetValue<string>().Should().Be("string");
        props["capture-format"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Fact]
    public void Build_ShouldListAllFourAudioActions_WhenTopicEntryResolved()
    {
        McpToolCatalogue.Build(_root, McpEntryResolver.Resolve(_root, "audio")!).Select(t => t.Name)
            .Should().BeEquivalentTo("audio_record", "audio_insert", "audio_overwrite", "audio_play-midi");
    }

    [Fact]
    public void Build_ShouldThrow_WhenToolNamesCollide()
    {
        var a = new Command("a", "A");
        var build = () => McpToolCatalogue.Build(_root, [a, a]);

        build.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate MCP tool name 'a'*");
    }

    [Fact]
    public void Describe_ShouldMapRemainingScalarTypes_WhenOptionsAreLongDecimalAndOddTypes()
    {
        var cmd = new Command("t", "T")
        {
            new Option<long>("--l"), new Option<short>("--s"), new Option<byte>("--b"), new Option<double>("--d"),
            new Option<decimal>("--m"), new Option<float?>("--f"), new Option<List<string>>("--list"), new Option<DateTime>("--when")
        };

        var props = Node(McpToolCatalogue.Describe(cmd).InputSchema)["properties"]!.AsObject();

        props["l"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["s"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["b"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["d"]!["type"]!.GetValue<string>().Should().Be("number");
        props["m"]!["type"]!.GetValue<string>().Should().Be("number");
        props["f"]!["type"]!.GetValue<string>().Should().Be("number");
        props["list"]!["type"]!.GetValue<string>().Should().Be("array");
        props["when"]!["type"]!.GetValue<string>().Should().Be("string");
    }
}
