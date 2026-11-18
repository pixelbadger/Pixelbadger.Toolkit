using System.Text.Json;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// Counts JSON-RPC requests read and responses written on a newline-delimited stdio transport, so shutdown at EOF
/// can wait until every request that was read has been answered. Notifications (no "id") need no reply. A line that
/// is not valid JSON counts as nothing: the SDK transport logs and skips it without replying.
/// </summary>
internal sealed class JsonRpcExchangeTracker
{
    private readonly object _gate = new();
    private int _requests;
    private int _responses;

    public int Requests { get { lock (_gate) return _requests; } }
    public int Responses { get { lock (_gate) return _responses; } }
    public bool Settled { get { lock (_gate) return _responses >= _requests; } }

    public void RecordInputLine(ReadOnlySpan<byte> line)
    {
        if (Classify(line) is { HasMethod: true, HasId: true })
            lock (_gate) _requests++;
    }

    public void RecordOutputLine(ReadOnlySpan<byte> line)
    {
        if (Classify(line) is { HasMethod: false, HasId: true, HasOutcome: true })
            lock (_gate) _responses++;
    }

    /// <summary>Waits until responses have caught up with requests, the bound elapses, or cancellation is requested.</summary>
    public async Task WaitSettledAsync(TimeSpan bound, CancellationToken cancellationToken)
    {
        using var boundCts = new CancellationTokenSource(bound);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, boundCts.Token);
        try
        {
            while (!Settled) await Task.Delay(10, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private static (bool HasMethod, bool HasId, bool HasOutcome)? Classify(ReadOnlySpan<byte> line)
    {
        if (line.IsEmpty) return null;
        try
        {
            var reader = new Utf8JsonReader(line);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = doc.RootElement;
            return (root.TryGetProperty("method", out _), root.TryGetProperty("id", out _),
                root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Splits a byte stream into lines across arbitrary chunk boundaries.</summary>
internal sealed class LineScanner(Action<ReadOnlySpan<byte>> onLine)
{
    private readonly MemoryStream _partial = new();

    public void Feed(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var nl = data.IndexOf((byte)'\n');
            if (nl < 0) { _partial.Write(data); return; }
            if (_partial.Length == 0) onLine(data[..nl]);
            else
            {
                _partial.Write(data[..nl]);
                onLine(_partial.GetBuffer().AsSpan(0, (int)_partial.Length));
                _partial.SetLength(0);
            }
            data = data[(nl + 1)..];
        }
    }
}

/// <summary>
/// Wraps the protocol input so that, at EOF, the 0-byte read is withheld until every request read has been answered
/// (or <paramref name="bound"/> elapses, or the read is cancelled). Without it a client that writes its requests and
/// closes stdin loses the replies.
/// </summary>
internal sealed class DrainingInputStream(Stream inner, JsonRpcExchangeTracker tracker, TimeSpan bound) : Stream
{
    private readonly LineScanner _scanner = new(tracker.RecordInputLine);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0) _scanner.Feed(buffer.Span[..read]);
        else await tracker.WaitSettledAsync(bound, cancellationToken).ConfigureAwait(false);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

/// <summary>Forwards protocol output and records each response line written, after it reaches the inner stream.</summary>
internal sealed class ResponseCountingOutputStream(Stream inner, JsonRpcExchangeTracker tracker) : Stream
{
    private readonly LineScanner _scanner = new(tracker.RecordOutputLine);
    private readonly object _gate = new();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        lock (_gate) _scanner.Feed(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        lock (_gate) _scanner.Feed(buffer.Span);
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
