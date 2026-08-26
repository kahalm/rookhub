using System.Text;
using System.Threading.Channels;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class NdjsonHeartbeatPumpTests
{
    /// <summary>Quell-Stream, der Bytes erst liefert, wenn der Test sie freigibt (simuliert einen
    /// Broker, der minutenlang schweigt). Ein leeres Array = Stream-Ende.</summary>
    private sealed class GatedStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[]? _pending; private int _pendingPos;

        public void Push(string s) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(s));
        public void Complete() => _chunks.Writer.TryWrite([]);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pending is null || _pendingPos >= _pending.Length)
            {
                _pending = await _chunks.Reader.ReadAsync(ct);
                _pendingPos = 0;
                if (_pending.Length == 0) return 0;
            }
            var n = Math.Min(buffer.Length, _pending.Length - _pendingPos);
            _pending.AsSpan(_pendingPos, n).CopyTo(buffer.Span);
            _pendingPos += n;
            return n;
        }

        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!cond() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(cond(), "Bedingung nicht binnen Frist erfüllt");
    }

    [Fact]
    public async Task PumpAsync_PassesDataThroughUnchanged_WithoutHeartbeatsWhenBusy()
    {
        var src = new GatedStream(); var dst = new MemoryStream();
        src.Push("{\"depth\":1}\n"); src.Push("{\"depth\":2}\n"); src.Complete();

        await NdjsonHeartbeatPump.PumpAsync(src, dst, TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal("{\"depth\":1}\n{\"depth\":2}\n", Encoding.UTF8.GetString(dst.ToArray()));
    }

    [Fact]
    public async Task PumpAsync_WritesBlankLinesWhileSourceIsSilent_ThenContinuesWithData()
    {
        var src = new GatedStream(); var dst = new MemoryStream();
        var pump = NdjsonHeartbeatPump.PumpAsync(src, dst, TimeSpan.FromMilliseconds(30), CancellationToken.None);

        // Broker schweigt → nach kurzer Zeit müssen Lebenszeichen (Leerzeilen) da sein.
        await WaitUntilAsync(() => dst.Length >= 2);
        Assert.All(dst.ToArray(), b => Assert.Equal((byte)'\n', b));

        // Danach kommt eine echte Zeile durch — hinter den Leerzeilen, unverändert.
        src.Push("{\"depth\":28}\n");
        await WaitUntilAsync(() => Encoding.UTF8.GetString(dst.ToArray()).Contains("\"depth\":28"));
        src.Complete();
        await pump.WaitAsync(TimeSpan.FromSeconds(3));

        var text = Encoding.UTF8.GetString(dst.ToArray());
        // Der Client-Parser ignoriert Leerzeilen — die Nutzlast bleibt exakt eine gültige Zeile.
        Assert.Equal(["{\"depth\":28}"], text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task PumpAsync_StopsOnCancellation()
    {
        var src = new GatedStream(); var dst = new MemoryStream();
        using var cts = new CancellationTokenSource();
        var pump = NdjsonHeartbeatPump.PumpAsync(src, dst, TimeSpan.FromMilliseconds(30), cts.Token);

        await WaitUntilAsync(() => dst.Length >= 1);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.WaitAsync(TimeSpan.FromSeconds(3)));
    }
}
