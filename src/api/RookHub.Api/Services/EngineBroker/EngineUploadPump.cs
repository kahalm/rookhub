using System.Buffers;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services.EngineBroker;

public enum UploadOutcome
{
    /// <summary>Mit <c>bestmove</c> beendet.</summary>
    Completed,
    /// <summary>Der Provider hat den Upload ohne <c>bestmove</c> beendet (lila-engine: 400 — wir nicht).</summary>
    WithoutBestmove,
    /// <summary>Der Anfragende ist weg — Antwort 200 sofort, der Provider stoppt die Engine.</summary>
    RequesterGone,
    /// <summary>Die Verbindung zum Provider ist gerissen.</summary>
    ProviderGone,
}

/// <param name="Error">Bei <see cref="UploadOutcome.ProviderGone"/>: woran der Upload gerissen ist.</param>
public sealed record UploadResult(UploadOutcome Outcome, int Emits, int Keepalives, int SkippedLines, string? Error = null);

/// <summary>
/// Liest den Upload des Providers (<c>POST /api/external-engine/work/{id}</c>, chunked, eine Zeile je
/// <c>\n</c>) und reicht dem Anfragenden die fertigen JSON-Zeilen weiter — der Kern von lila-engine
/// <c>main.rs::submit</c>.
///
/// <para>Unterschiede zu lila-engine, alle in Richtung „weiterrechnen statt abbrechen": eine unlesbare
/// <c>info</c>-Zeile, eine zu lange Zeile (&gt; 16 KiB) und eine unbekannte JSON-Steuerzeile werden
/// ÜBERSPRUNGEN (lila: 400), und ein Upload-Ende ohne <c>bestmove</c> beendet den Strom regulär (lila: 400 —
/// die Ursache des 5-s-Schlafs im Provider bis d0eeb242). Die Warnungen sind je Upload gedeckelt.</para>
///
/// <para><c>{"keepalive":true}</c> geht UNVERÄNDERT als eigene Zeile an den Anfragenden: der Worker zählt es
/// als Lebenszeichen (<c>StreamTally.IsKeepalive</c>), der Browser verwirft es im Parser.</para>
/// </summary>
public static class EngineUploadPump
{
    public const int MaxLineBytes = 16 * 1024;
    private const int MaxWarningsPerUpload = 3;

    public static async Task<UploadResult> RunAsync(PendingJob job, Stream body, CancellationToken aborted, ILogger logger)
    {
        job.Accepted.TrySetResult();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(aborted, job.RequesterGone);
        var ct = linked.Token;
        var reader = new UploadLineReader(body, MaxLineBytes);
        var emit = new EmitBuilder(job.RootFen);
        var writer = job.Lines.Writer;
        int emits = 0, keepalives = 0, skipped = 0, warnings = 0;
        UploadOutcome outcome;
        string? error = null;

        void Skip(string why, string line)
        {
            skipped++;
            if (warnings++ < MaxWarningsPerUpload)
                logger.LogWarning("EngineBroker: Zeile uebersprungen ({Reason}) engine={EngineId} job={JobId}: {Line}",
                    why, job.EngineId, job.Id, line.Length > 200 ? line[..200] + "…" : line);
        }

        try
        {
            while (true)
            {
                var next = await reader.NextAsync(ct);
                if (next.Kind == UploadLineKind.Eof)
                {
                    emit.Finish(null, null);
                    await writer.WriteAsync(emit.ToJson() + "\n", ct);
                    emits++;
                    outcome = UploadOutcome.WithoutBestmove;
                    break;
                }
                if (next.Kind == UploadLineKind.TooLong) { Skip("zu lang", "…"); continue; }
                if (next.Kind == UploadLineKind.Invalid) { Skip("kein UTF-8", "…"); continue; }

                var line = next.Text!;
                if (line.AsSpan().TrimStart().StartsWith("{"))
                {
                    if (IsKeepalive(line))
                    {
                        await writer.WriteAsync(line.Trim() + "\n", ct);
                        keepalives++;
                    }
                    else Skip("unbekannte Steuerzeile", line);
                    continue;
                }

                var parsed = UciLineParser.Parse(line);
                if (parsed.Kind == UciLineKind.BestMove)
                {
                    emit.Finish(parsed.BestMove!.Move, parsed.BestMove.Ponder);
                    await writer.WriteAsync(emit.ToJson() + "\n", ct);
                    emits++;
                    outcome = UploadOutcome.Completed;
                    break;
                }
                if (parsed.Kind == UciLineKind.Info)
                {
                    emit.Update(parsed.Info!);
                    if (emit.ShouldEmit)
                    {
                        await writer.WriteAsync(emit.ToJson() + "\n", ct);
                        emits++;
                    }
                    continue;
                }
                if (parsed.Kind == UciLineKind.Error)
                {
                    if (line.AsSpan().TrimStart().StartsWith("bestmove"))
                    {
                        // Das Ende der Suche bleibt das Ende, auch wenn der Zug unlesbar ist.
                        Skip(parsed.Error ?? "Fehler", line);
                        emit.Finish(null, null);
                        await writer.WriteAsync(emit.ToJson() + "\n", ct);
                        emits++;
                        outcome = UploadOutcome.Completed;
                        break;
                    }
                    Skip(parsed.Error ?? "Fehler", line);
                }
            }
        }
        catch (Exception ex) when (job.RequesterGone.IsCancellationRequested
                                   && ex is OperationCanceledException or IOException
                                       or Microsoft.AspNetCore.Http.BadHttpRequestException)
        {
            // Kestrel meldet ein abgebrochenes Lesen des Rumpfs nicht immer als Abbruch, sondern auch als
            // „Unexpected end of request content" — entscheidend ist, dass der ANFRAGENDE weg ist.
            outcome = UploadOutcome.RequesterGone;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException
                                   or Microsoft.AspNetCore.Http.BadHttpRequestException
                                   or Microsoft.AspNetCore.Connections.ConnectionResetException)
        {
            // Wie lila-engine: der Anfragende bekommt den letzten Stand als Abschluss, dann endet sein Strom.
            outcome = UploadOutcome.ProviderGone;
            error = $"{ex.GetType().Name}: {ex.Message}";
            emit.Finish(null, null);
            writer.TryWrite(emit.ToJson() + "\n");
        }
        finally
        {
            writer.TryComplete();
        }

        if (outcome == UploadOutcome.WithoutBestmove)
            logger.LogWarning("EngineBroker: Upload ohne bestmove engine={EngineId} job={JobId} ({Emits} Zeilen)",
                job.EngineId, job.Id, emits);
        if (skipped > MaxWarningsPerUpload)
            logger.LogWarning("EngineBroker: {Skipped} Zeilen uebersprungen engine={EngineId} job={JobId}",
                skipped, job.EngineId, job.Id);
        return new UploadResult(outcome, emits, keepalives, skipped, error);
    }

    /// <summary><c>{"keepalive": …}</c> — ein JSON-Objekt mit genau diesem Feld (lila-engine
    /// <c>ControlMessage::Keepalive</c>).</summary>
    public static bool IsKeepalive(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("keepalive", out _);
        }
        catch (JsonException) { return false; }
    }
}

public enum UploadLineKind { Line, TooLong, Invalid, Eof }

public readonly record struct UploadLine(UploadLineKind Kind, string? Text = null);

/// <summary>
/// Zeilenweises Lesen des Upload-Rumpfs OHNE Puffern des Ganzen (der Upload läuft, solange die Engine
/// rechnet — Minuten bis Stunden). Wie lila-engine <c>next_line_limited</c>: <c>\r\n</c> und <c>\n</c>
/// trennen, eine letzte Zeile ohne Umbruch zählt, eine Zeile über dem Deckel wird bis zum nächsten Umbruch
/// verworfen (lila: Fehler, wir: überspringen).
/// </summary>
public sealed class UploadLineReader(Stream body, int maxLineBytes)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);
    private readonly byte[] _buffer = new byte[8192];
    private int _start;
    private int _end;
    private bool _eof;

    public async ValueTask<UploadLine> NextAsync(CancellationToken ct)
    {
        var line = new ArrayBufferWriter<byte>();
        var tooLong = false;
        var sawAny = false;
        while (true)
        {
            if (_start == _end)
            {
                if (_eof)
                    return sawAny ? Finish(line, tooLong) : new UploadLine(UploadLineKind.Eof);
                _start = 0;
                _end = await body.ReadAsync(_buffer, ct);
                if (_end == 0) { _eof = true; continue; }
            }
            var span = _buffer.AsSpan(_start, _end - _start);
            var nl = span.IndexOf((byte)'\n');
            var part = nl < 0 ? span : span[..nl];
            sawAny = true;
            if (!tooLong)
            {
                if (line.WrittenCount + part.Length > maxLineBytes) tooLong = true;
                else line.Write(part);
            }
            _start += part.Length;
            if (nl >= 0)
            {
                _start++;
                return Finish(line, tooLong);
            }
        }
    }

    private static UploadLine Finish(ArrayBufferWriter<byte> line, bool tooLong)
    {
        if (tooLong) return new UploadLine(UploadLineKind.TooLong);
        var bytes = line.WrittenSpan;
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];
        try { return new UploadLine(UploadLineKind.Line, StrictUtf8.GetString(bytes)); }
        catch (DecoderFallbackException) { return new UploadLine(UploadLineKind.Invalid); }
    }
}
