namespace RookHub.Api.Services;

/// <summary>
/// Nur-Lese-Hülle um einen Strom, die höchstens <c>limit</c> Bytes durchlässt und danach
/// <see cref="LimitExceededException"/> wirft. Gedacht für entpackte Rümpfe (gzip): <c>RequestSizeLimit</c>
/// zählt nur die KOMPRIMIERTEN Bytes, eine gzip-Bombe (Verhältnis bis ~1000:1) oder eine versehentlich
/// riesige Sammlung entpackt sonst ungebremst in den Speicher des API-Prozesses.
/// </summary>
public sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    private long _read;

    /// <summary>Der Strom hat mehr als <see cref="Limit"/> Bytes geliefert.</summary>
    public sealed class LimitExceededException(long limit)
        : IOException($"Der entpackte Inhalt ist größer als {limit} Bytes.")
    {
        public long Limit { get; } = limit;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    private int Count(int n)
    {
        _read += n;
        if (_read > limit) throw new LimitExceededException(limit);
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
