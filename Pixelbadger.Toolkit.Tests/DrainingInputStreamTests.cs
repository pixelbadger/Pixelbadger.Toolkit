using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class DrainingInputStreamTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task ReadAsync_ShouldReturnInnerBytesImmediately_WhenDataAvailable()
    {
        using var stream = new DrainingInputStream(new MemoryStream([1, 2, 3]), () => 5, Grace);
        var buffer = new byte[8];

        var read = await stream.ReadAsync(buffer);

        read.Should().Be(3);
        buffer.Take(3).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ReadAsync_ShouldWaitForInFlightCallsBeforeReportingEof_WhenInnerEnds()
    {
        var inFlight = 1;
        using var stream = new DrainingInputStream(new MemoryStream(), () => Volatile.Read(ref inFlight), Grace);

        var pending = stream.ReadAsync(new byte[4]).AsTask();
        await Task.Delay(200);
        pending.IsCompleted.Should().BeFalse();

        Volatile.Write(ref inFlight, 0);
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnEofPromptly_WhenNothingInFlight()
    {
        using var stream = new DrainingInputStream(new MemoryStream(), () => 0, Grace);

        (await stream.ReadAsync(new byte[4]).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnEof_WhenCancelledWhileDraining()
    {
        using var cts = new CancellationTokenSource(100);
        using var stream = new DrainingInputStream(new MemoryStream(), () => 1, Grace);

        (await stream.ReadAsync(new byte[4], cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
    }

    [Fact]
    public void Properties_ShouldDescribeReadOnlyForwardStream_WhenQueried()
    {
        using var stream = new DrainingInputStream(new MemoryStream(), () => 0, Grace);

        stream.CanRead.Should().BeTrue();
        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeFalse();
        ((Action)(() => _ = stream.Length)).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.Write([1], 0, 1))).Should().Throw<NotSupportedException>();
    }
}
