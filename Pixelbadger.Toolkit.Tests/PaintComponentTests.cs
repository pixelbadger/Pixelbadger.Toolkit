using FluentAssertions;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Tests;

public class PaintComponentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public PaintComponentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Queue<Func<ChatResponse>> _script = new();
        public List<List<ChatMessage>> Requests { get; } = new();
        public List<ChatOptions?> Options { get; } = new();

        public ScriptedChatClient Enqueue(ChatResponse response) { _script.Enqueue(() => response); return this; }
        public ScriptedChatClient EnqueueThrow(Exception ex) { _script.Enqueue(() => throw ex); return this; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            Options.Add(options);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_script.Dequeue()());
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RecordingRenderer : IPaintRenderer
    {
        public List<string> Events { get; } = new();
        public List<string> Statuses { get; } = new();
        public PixelColor[]? FinalPixels { get; private set; }
        public void Begin() => Events.Add("Begin");
        public void RenderFrame(PixelColor[] pixels, int size, string status) { Events.Add("Frame"); Statuses.Add(status); }
        public void LogToolCall(string description) => Events.Add("Log");
        public void End(PixelColor[] pixels, int size) { Events.Add("End"); FinalPixels = pixels; }
        public void Dispose() { }
    }

    private static FunctionCallContent Call(string id, string name, params (string, object?)[] args)
        => new(id, name, args.ToDictionary(a => a.Item1, a => a.Item2));

    private static ChatResponse Response(string? text, params FunctionCallContent[] calls)
    {
        var contents = new List<AIContent>();
        if (text is not null) contents.Add(new TextContent(text));
        contents.AddRange(calls);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    private static FunctionCallContent Fill(string id, string color = "#FF0000")
        => Call(id, "fill_rect", ("x", 0), ("y", 0), ("width", 4), ("height", 4), ("color", color));

    private static FunctionCallContent Finish(string id, string summary = "done")
        => Call(id, "finish", ("summary", summary));

    private static Task<PaintResult> Run(ScriptedChatClient client, RecordingRenderer r, int maxSteps = 10,
        string? outFile = null, bool overwrite = false, CancellationToken ct = default, string? animationOut = null)
        => new PaintComponent(client).PaintAsync("a badger", maxSteps, outFile, overwrite, r, animationOut, ct);

    [Fact]
    public async Task PaintAsync_ShouldStopAfterRemainingCalls_WhenFinishCalledMidResponse()
    {
        var client = new ScriptedChatClient()
            .Enqueue(Response("hi", Finish("c1", "A red square"), Fill("c2")))
            .Enqueue(Response("never used"));
        var renderer = new RecordingRenderer();

        var result = await Run(client, renderer);

        result.EndReason.Should().Be(PaintEndReason.Finished);
        result.Summary.Should().Be("A red square");
        result.Steps.Should().Be(1);
        client.Requests.Should().HaveCount(1);
        client.Requests[0].Should().HaveCount(2);
        renderer.Events.Should().Equal("Begin", "Frame", "Log", "Log", "Frame", "End");
        renderer.FinalPixels![0].Should().Be(new PixelColor(255, 0, 0));
    }

    [Fact]
    public async Task PaintAsync_ShouldEndWithNoToolCalls_WhenResponseHasNoToolCalls()
    {
        var client = new ScriptedChatClient().Enqueue(Response("I am done talking"));
        var renderer = new RecordingRenderer();

        var result = await Run(client, renderer);

        result.EndReason.Should().Be(PaintEndReason.NoToolCalls);
        result.Steps.Should().Be(1);
        result.Summary.Should().BeNull();
        renderer.Events.Should().Equal("Begin", "Frame", "End");
    }

    [Fact]
    public async Task PaintAsync_ShouldStopAtStepLimit_AfterExactlyMaxStepsCalls()
    {
        var client = new ScriptedChatClient();
        for (var i = 0; i < 5; i++) client.Enqueue(Response(null, Fill("c" + i)));
        var renderer = new RecordingRenderer();

        var result = await Run(client, renderer, maxSteps: 3);

        result.EndReason.Should().Be(PaintEndReason.StepLimit);
        result.Steps.Should().Be(3);
        client.Requests.Should().HaveCount(3);
        renderer.Events.Count(e => e == "End").Should().Be(1);
    }

    [Fact]
    public async Task PaintAsync_ShouldSendAllResultsInOneToolMessage_InCallOrder()
    {
        var client = new ScriptedChatClient()
            .Enqueue(Response(null, Fill("a"), Call("b", "read_region", ("x", 0), ("y", 0), ("width", 1), ("height", 1)), Call("c", "set_pixels", ("pixels", new object[0]))))
            .Enqueue(Response(null, Finish("d")));
        var renderer = new RecordingRenderer();

        await Run(client, renderer);

        var second = client.Requests[1];
        var toolMessages = second.Where(m => m.Role == ChatRole.Tool).ToList();
        toolMessages.Should().HaveCount(1);
        toolMessages[0].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).Should().Equal("a", "b", "c");
        second.Last().Should().BeSameAs(toolMessages[0]);
        second[^2].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public async Task PaintAsync_ShouldReturnErrorResultAndContinue_WhenToolCallInvalid()
    {
        var client = new ScriptedChatClient()
            .Enqueue(Response(null, Call("x", "no_such_tool"), Fill("y", "not-a-colour")))
            .Enqueue(Response(null, Finish("z")));
        var renderer = new RecordingRenderer();

        var result = await Run(client, renderer);

        result.EndReason.Should().Be(PaintEndReason.Finished);
        client.Requests.Should().HaveCount(2);
        var results = client.Requests[1].Last().Contents.OfType<FunctionResultContent>().ToList();
        results.Should().HaveCount(2);
        results.Select(r => r.Result?.ToString()).Should().OnlyContain(t => t!.StartsWith("Error: "));
        renderer.Events.Count(e => e == "Frame").Should().Be(1);
    }

    [Fact]
    public async Task PaintAsync_ShouldDeliverReadCanvasPng_ToNextRequest()
    {
        var client = new ScriptedChatClient()
            .Enqueue(Response(null, Fill("a"), Call("r", "read_canvas")))
            .Enqueue(Response(null, Finish("f")));

        await Run(client, new RecordingRenderer());

        var result = client.Requests[1].Last().Contents.OfType<FunctionResultContent>().Single(r => r.CallId == "r");
        var parts = ((IEnumerable<AIContent>)result.Result!).ToList();
        var data = parts.OfType<DataContent>().Single();
        data.MediaType.Should().Be("image/png");
        using var image = Image.Load<Rgb24>(data.Data.ToArray());
        image.Width.Should().Be(256);
        image[0, 0].Should().Be(new Rgb24(255, 0, 0));
        image[200, 200].Should().Be(new Rgb24(255, 255, 255));
    }

    [Fact]
    public async Task PaintAsync_ShouldSendSevenToolsAndSystemAndUserMessages()
    {
        var client = new ScriptedChatClient().Enqueue(Response("ok"));

        await Run(client, new RecordingRenderer(), maxSteps: 7);

        client.Options[0]!.Tools!.Select(t => t.Name).Should().BeEquivalentTo(
            "read_canvas", "read_region", "set_pixels", "fill_rect", "draw_line", "fill_ellipse", "finish");
        client.Options[0]!.ToolMode.Should().Be(ChatToolMode.Auto);
        client.Requests[0][0].Role.Should().Be(ChatRole.System);
        client.Requests[0][0].Text.Should().Contain("7 turns").And.Contain("256x256");
        client.Requests[0][1].Role.Should().Be(ChatRole.User);
        client.Requests[0][1].Text.Should().Be("a badger");
    }

    [Fact]
    public async Task PaintAsync_ShouldRenderFramesOnlyForPaintingCalls_InOrder()
    {
        var client = new ScriptedChatClient()
            .Enqueue(Response("thinking\nhard", Fill("a"), Call("b", "read_canvas"), Fill("c", "#00FF00")))
            .Enqueue(Response(null, Finish("d")));
        var renderer = new RecordingRenderer();

        await Run(client, renderer);

        renderer.Events.Should().Equal("Begin", "Frame", "Log", "Frame", "Log", "Log", "Frame", "Log", "End");
        renderer.Statuses[0].Should().Be("Step 0/10 · starting");
        renderer.Statuses[1].Should().StartWith("Step 1/10 · ").And.EndWith(" · thinking hard");
    }

    [Fact]
    public async Task PaintAsync_ShouldCallEndOnceAndPropagate_WhenChatClientThrows()
    {
        var client = new ScriptedChatClient().EnqueueThrow(new InvalidOperationException("boom"));
        var renderer = new RecordingRenderer();
        var outFile = Path.Combine(_dir, "out.png");

        var act = () => Run(client, renderer, outFile: outFile);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        renderer.Events.Should().Equal("Begin", "Frame", "End");
        File.Exists(outFile).Should().BeFalse();
    }

    [Fact]
    public async Task PaintAsync_ShouldCallEndOnceAndWriteNothing_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new ScriptedChatClient().Enqueue(Response("x"));
        var renderer = new RecordingRenderer();
        var outFile = Path.Combine(_dir, "out.png");

        var act = () => Run(client, renderer, outFile: outFile, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        renderer.Events.Should().Equal("Begin", "Frame", "End");
        File.Exists(outFile).Should().BeFalse();
    }

    [Fact]
    public async Task PaintAsync_ShouldWritePngEqualToCanvas_WhenEndedCleanly()
    {
        var outFile = Path.Combine(_dir, "sub", "out.png");
        var client = new ScriptedChatClient()
            .Enqueue(Response(null, Fill("a", "#123456"), Finish("f")));
        var renderer = new RecordingRenderer();

        var result = await Run(client, renderer, outFile: outFile);

        result.OutFile.Should().Be(Path.GetFullPath(outFile));
        var expected = new PaintCanvas();
        expected.FillRect(0, 0, 4, 4, new PixelColor(0x12, 0x34, 0x56));
        File.ReadAllBytes(outFile).Should().Equal(expected.ToPng());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PaintAsync_ShouldWritePng_WhenNoToolCallsOrStepLimit(bool noToolCalls)
    {
        var outFile = Path.Combine(_dir, "o.png");
        var client = new ScriptedChatClient().Enqueue(noToolCalls ? Response("hm") : Response(null, Fill("a")));

        var result = await Run(client, new RecordingRenderer(), maxSteps: 1, outFile: outFile);

        result.EndReason.Should().Be(noToolCalls ? PaintEndReason.NoToolCalls : PaintEndReason.StepLimit);
        File.Exists(outFile).Should().BeTrue();
    }

    [Fact]
    public async Task PaintAsync_ShouldRefuseBeforeBeginAndModelCall_WhenOutFileExists()
    {
        var outFile = Path.Combine(_dir, "exists.png");
        File.WriteAllText(outFile, "keep");
        var client = new ScriptedChatClient().Enqueue(Response("x"));
        var renderer = new RecordingRenderer();

        var act = () => Run(client, renderer, outFile: outFile);

        (await act.Should().ThrowAsync<IOException>())
            .WithMessage($"Output file already exists: {Path.GetFullPath(outFile)}. Use --overwrite to replace it.");
        client.Requests.Should().BeEmpty();
        renderer.Events.Should().BeEmpty();
        File.ReadAllText(outFile).Should().Be("keep");
    }

    [Fact]
    public async Task PaintAsync_ShouldReplaceFile_WhenOverwriteSet()
    {
        var outFile = Path.Combine(_dir, "exists.png");
        File.WriteAllText(outFile, "old");
        var client = new ScriptedChatClient().Enqueue(Response("x"));

        await Run(client, new RecordingRenderer(), outFile: outFile, overwrite: true);

        File.ReadAllBytes(outFile).Should().Equal(new PaintCanvas().ToPng());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task PaintAsync_ShouldThrowBeforeBegin_WhenMaxStepsOutOfRange(int maxSteps)
    {
        var client = new ScriptedChatClient();
        var renderer = new RecordingRenderer();

        var act = () => Run(client, renderer, maxSteps);

        (await act.Should().ThrowAsync<ArgumentOutOfRangeException>()).WithMessage("*--max-steps must be between 1 and 200*");
        renderer.Events.Should().BeEmpty();
        client.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PaintAsync_ShouldThrowArgumentException_WhenPromptBlank(string prompt)
    {
        var renderer = new RecordingRenderer();

        var act = () => new PaintComponent(new ScriptedChatClient()).PaintAsync(prompt, 5, null, false, renderer);

        await act.Should().ThrowAsync<ArgumentException>();
        renderer.Events.Should().BeEmpty();
    }

    private static List<(Rgb24 Pixel, int Delay)> DecodeGif(string path, out int repeatCount)
    {
        using var image = Image.Load<Rgb24>(path);
        repeatCount = image.Metadata.GetGifMetadata().RepeatCount;
        return image.Frames.Select(f => (f[0, 0], f.Metadata.GetGifMetadata().FrameDelay)).ToList();
    }

    [Fact]
    public async Task PaintAsync_ShouldWriteGifWithOneFramePerPaintingCall_WhenAnimationOutSet()
    {
        var gif = Path.Combine(_dir, "sub", "a.gif");
        var client = new ScriptedChatClient()
            .Enqueue(Response(null, Fill("a", "#FF0000"), Call("b", "read_canvas"), Fill("c", "#00FF00")))
            .Enqueue(Response(null, Call("d", "read_region", ("x", 0), ("y", 0), ("width", 2), ("height", 2)), Fill("e", "#0000FF"), Finish("f")));

        var result = await Run(client, new RecordingRenderer(), animationOut: gif);

        result.AnimationOut.Should().Be(Path.GetFullPath(gif));
        var frames = DecodeGif(gif, out var repeat);
        repeat.Should().Be(0);
        frames.Select(f => f.Pixel).Should().Equal(
            new Rgb24(255, 255, 255), new Rgb24(255, 0, 0), new Rgb24(0, 255, 0), new Rgb24(0, 0, 255));
        frames.Select(f => f.Delay).Should().Equal(15, 15, 15, 200);
    }

    [Fact]
    public async Task PaintAsync_ShouldWriteSingleBlankFrame_WhenNothingPainted()
    {
        var gif = Path.Combine(_dir, "a.gif");

        await Run(new ScriptedChatClient().Enqueue(Response("hm")), new RecordingRenderer(), animationOut: gif);

        var frames = DecodeGif(gif, out _);
        frames.Should().Equal((new Rgb24(255, 255, 255), 200));
    }

    [Fact]
    public async Task PaintAsync_ShouldNotWriteGif_WhenChatClientThrows()
    {
        var gif = Path.Combine(_dir, "a.gif");
        var client = new ScriptedChatClient().EnqueueThrow(new InvalidOperationException("boom"));

        var act = () => Run(client, new RecordingRenderer(), animationOut: gif);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(gif).Should().BeFalse();
    }

    [Fact]
    public async Task PaintAsync_ShouldNotWriteGif_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var gif = Path.Combine(_dir, "a.gif");

        var act = () => Run(new ScriptedChatClient().Enqueue(Response("x")), new RecordingRenderer(), ct: cts.Token, animationOut: gif);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(gif).Should().BeFalse();
    }

    [Fact]
    public async Task PaintAsync_ShouldRefuseBeforeBeginAndModelCall_WhenAnimationOutExists()
    {
        var gif = Path.Combine(_dir, "exists.gif");
        File.WriteAllText(gif, "keep");
        var client = new ScriptedChatClient().Enqueue(Response("x"));
        var renderer = new RecordingRenderer();

        var act = () => Run(client, renderer, animationOut: gif);

        (await act.Should().ThrowAsync<IOException>())
            .WithMessage($"Output file already exists: {Path.GetFullPath(gif)}. Use --overwrite to replace it.");
        client.Requests.Should().BeEmpty();
        renderer.Events.Should().BeEmpty();
        File.ReadAllText(gif).Should().Be("keep");
    }

    [Fact]
    public async Task PaintAsync_ShouldThrowBeforeBeginAndModelCall_WhenOutFileAndAnimationOutSamePath()
    {
        var client = new ScriptedChatClient().Enqueue(Response("x"));
        var renderer = new RecordingRenderer();
        var path = Path.Combine(_dir, "x.gif");

        var act = () => Run(client, renderer, outFile: path, animationOut: Path.Combine(_dir, "sub", "..", "x.gif"));

        (await act.Should().ThrowAsync<ArgumentException>()).WithMessage("--out-file and --animation-out must be different paths.");
        client.Requests.Should().BeEmpty();
        renderer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task PaintAsync_ShouldReplaceGif_WhenOverwriteSet()
    {
        var gif = Path.Combine(_dir, "exists.gif");
        File.WriteAllText(gif, "old");

        await Run(new ScriptedChatClient().Enqueue(Response("x")), new RecordingRenderer(), overwrite: true, animationOut: gif);

        DecodeGif(gif, out _).Should().HaveCount(1);
    }

    [Fact]
    public async Task PaintAsync_ShouldWriteBothFiles_WhenOutFileAndAnimationOutDiffer()
    {
        var png = Path.Combine(_dir, "o.png");
        var gif = Path.Combine(_dir, "o.gif");

        await Run(new ScriptedChatClient().Enqueue(Response(null, Fill("a"), Finish("f"))), new RecordingRenderer(), outFile: png, animationOut: gif);

        File.Exists(png).Should().BeTrue();
        DecodeGif(gif, out _).Should().HaveCount(2);
    }
}
