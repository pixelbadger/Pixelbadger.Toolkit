using System.Text;
using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class DrainingInputStreamTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private const string Request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"}\n";
    private const string Notification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n";
    private const string Response = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n";

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static async Task<int> ReadAllAsync(Stream stream)
    {
        var buffer = new byte[256];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0) { }
        return read;
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnInnerBytes_WhenDataAvailable()
    {
        using var stream = new DrainingInputStream(new MemoryStream([1, 2, 3]), new JsonRpcExchangeTracker(), Bound);
        var buffer = new byte[8];

        var read = await stream.ReadAsync(buffer);

        read.Should().Be(3);
        buffer.Take(3).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ReadAsync_ShouldWithholdEofUntilResponseWritten_WhenResponseLaterThanOldGrace()
    {
        var tracker = new JsonRpcExchangeTracker();
        using var stream = new DrainingInputStream(new MemoryStream(Bytes(Notification + Request)), tracker, Bound);
        using var output = new ResponseCountingOutputStream(new MemoryStream(), tracker);

        var pending = ReadAllAsync(stream);
        await Task.Delay(1500);
        pending.IsCompleted.Should().BeFalse();

        await output.WriteAsync(Bytes(Response));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
        tracker.Requests.Should().Be(1);
        tracker.Responses.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnEofImmediately_WhenInputHasOnlyNotifications()
    {
        using var stream = new DrainingInputStream(new MemoryStream(Bytes(Notification + Notification)), new JsonRpcExchangeTracker(), Bound);

        (await ReadAllAsync(stream).WaitAsync(TimeSpan.FromSeconds(2))).Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldCountRequestOnce_WhenLineSplitAcrossReads()
    {
        var tracker = new JsonRpcExchangeTracker();
        var data = Bytes(Request);
        using var stream = new DrainingInputStream(new MemoryStream(data), tracker, Bound);

        var small = new byte[7];
        (await stream.ReadAsync(small)).Should().Be(7);
        tracker.Requests.Should().Be(0);

        var rest = ReadAllAsync(stream);
        await Task.Delay(100);
        tracker.Requests.Should().Be(1);
        rest.IsCompleted.Should().BeFalse();

        tracker.RecordOutputLine(Bytes(Response.TrimEnd('\n')));
        (await rest.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
        tracker.Requests.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ShouldIgnoreInvalidJsonAndBlankLines_WhenCounting()
    {
        var tracker = new JsonRpcExchangeTracker();
        using var stream = new DrainingInputStream(new MemoryStream(Bytes("not json\n\n[1,2]\n{\"id\":2}\n")), tracker, Bound);

        (await ReadAllAsync(stream).WaitAsync(TimeSpan.FromSeconds(2))).Should().Be(0);
        tracker.Requests.Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldReleaseEof_WhenBoundElapses()
    {
        using var stream = new DrainingInputStream(new MemoryStream(Bytes(Request)), new JsonRpcExchangeTracker(), TimeSpan.FromMilliseconds(200));

        (await ReadAllAsync(stream).WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldReleaseEof_WhenCancelledWhileWaiting()
    {
        using var cts = new CancellationTokenSource(100);
        using var stream = new DrainingInputStream(new MemoryStream(Bytes(Request)), new JsonRpcExchangeTracker(), Bound);
        var buffer = new byte[256];
        await stream.ReadAsync(buffer, cts.Token);

        (await stream.ReadAsync(buffer, cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
    }

    [Fact]
    public void Properties_ShouldDescribeReadOnlyForwardStream_WhenQueried()
    {
        using var stream = new DrainingInputStream(new MemoryStream(), new JsonRpcExchangeTracker(), Bound);

        stream.CanRead.Should().BeTrue();
        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeFalse();
        ((Action)(() => _ = stream.Length)).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.Write([1], 0, 1))).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task ResponseCountingOutputStream_ShouldForwardBytesAndCountResponsesOnly_WhenMessagesWritten()
    {
        var tracker = new JsonRpcExchangeTracker();
        var sink = new MemoryStream();
        using var output = new ResponseCountingOutputStream(sink, tracker);
        var response = Bytes(Response);

        await output.WriteAsync(response.AsMemory(0, 10));
        tracker.Responses.Should().Be(0);
        await output.WriteAsync(response.AsMemory(10));
        var ping = Bytes("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}\n");
        output.Write(ping, 0, ping.Length);
        output.Write(Bytes("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\"}\n"));
        output.Write(Bytes("{\"jsonrpc\":\"2.0\",\"id\":2,\"error\":{\"code\":-1,\"message\":\"x\"}}\n"));
        output.Flush();

        tracker.Responses.Should().Be(2);
        sink.Length.Should().BeGreaterThan(response.Length);
        output.CanWrite.Should().BeTrue();
        output.CanRead.Should().BeFalse();
    }
}
