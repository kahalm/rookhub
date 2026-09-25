using System.Text;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Eine laufende Analyse beim Broker — gleich, ob beim eigenen (in-Prozess) oder bei Lichess
/// (HTTP-Antwort). <see cref="StatusCode"/> 200 = <see cref="Ndjson"/> ist der Zeilenstrom
/// (<c>{"time":…,"pvs":[…]}</c>, <c>{"keepalive":true}</c>); sonst hat der Broker abgelehnt (503 = für diese
/// Engine nimmt gerade kein Provider an — der Worker wechselt dann die Engine).
///
/// <para>Beenden = <see cref="DisposeAsync"/>: beim eigenen Broker endet damit sofort der Upload des
/// Providers (er stoppt die Engine), bei Lichess schließt es die Verbindung (dasselbe, einen Hop weiter).</para>
/// </summary>
public sealed class EngineAnalysisSession : IAsyncDisposable
{
    private readonly Func<ValueTask>? _dispose;

    public EngineAnalysisSession(int statusCode, Stream? ndjson, string? error = null, Func<ValueTask>? dispose = null)
    {
        StatusCode = statusCode;
        Ndjson = ndjson;
        Error = error;
        _dispose = dispose;
    }

    public int StatusCode { get; }
    public bool IsSuccess => StatusCode is >= 200 and < 300 && Ndjson is not null;
    public Stream? Ndjson { get; }
    public string? Error { get; }

    public static EngineAnalysisSession Rejected(int statusCode, string error) => new(statusCode, null, error);

    public async ValueTask DisposeAsync()
    {
        if (Ndjson is not null) await Ndjson.DisposeAsync();
        if (_dispose is not null) await _dispose();
    }
}

/// <summary>
/// Der Anfragende spricht mit „einem Broker" — welcher, entscheidet die Quelle der Engine
/// (<see cref="EngineRef.Source"/>): <c>rhe_</c> → <see cref="LocalEngineBroker"/>, sonst Lichess.
/// Wirft wie bisher bei Netzfehlern (<see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/>)
/// und bei Abbruch (<see cref="OperationCanceledException"/>) — die Aufrufer behandeln das unverändert.
/// </summary>
public interface IEngineBroker
{
    Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct);
}

/// <summary>
/// Liest die fertigen Zeilen eines Auftrags als Byte-Strom — damit bleiben
/// <c>NdjsonHeartbeatPump.PumpAsync</c> im Controller und <c>AnalysisJobStream.ConsumeAsync</c> im Worker
/// unverändert. Schließen = der Anfragende ist weg (<see cref="PendingJob.CancelRequester"/>).
/// </summary>
public sealed class JobNdjsonStream(PendingJob job) : Stream
{
    private byte[] _current = [];
    private int _offset;
    private int _disposed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_offset >= _current.Length)
        {
            if (!await job.Lines.Reader.WaitToReadAsync(ct)) return 0;
            if (job.Lines.Reader.TryRead(out var line))
            {
                _current = Encoding.UTF8.GetBytes(line);
                _offset = 0;
            }
        }
        var n = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, n).CopyTo(buffer);
        _offset += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) job.CancelRequester();
        base.Dispose(disposing);
    }
}
