using System.Diagnostics;

namespace SilentStream.Core.YouTube;

/// <summary>
/// Read-only pass-through stream that caps read throughput to protect the live uplink while a
/// VOD uploads concurrently (확장계획서 §4.3, D10: immediate-throttled). After each read it sleeps
/// just enough to keep the cumulative average at or below the target rate. A non-positive rate
/// disables throttling (transparent pass-through).
/// </summary>
public sealed class ThrottledStream(Stream inner, long maxBytesPerSecond) : Stream
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _totalRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Throttle(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        await ThrottleAsync(read, ct).ConfigureAwait(false);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
        await ThrottleAsync(read, ct).ConfigureAwait(false);
        return read;
    }

    private TimeSpan RequiredDelay(int read)
    {
        if (maxBytesPerSecond <= 0 || read <= 0)
        {
            return TimeSpan.Zero;
        }
        _totalRead += read;
        var targetSeconds = _totalRead / (double)maxBytesPerSecond;
        var aheadSeconds = targetSeconds - _clock.Elapsed.TotalSeconds;
        return aheadSeconds > 0 ? TimeSpan.FromSeconds(aheadSeconds) : TimeSpan.Zero;
    }

    private void Throttle(int read)
    {
        var delay = RequiredDelay(read);
        if (delay > TimeSpan.Zero)
        {
            Thread.Sleep(delay);
        }
    }

    private async Task ThrottleAsync(int read, CancellationToken ct)
    {
        var delay = RequiredDelay(read);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
