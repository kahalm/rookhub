using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// External-Engine-Anbindung (Lichess-Client-Modus): der User hinterlegt einen Lichess-API-Token
/// (Scope <c>engine:read</c>); RookHub listet damit seine registrierten External Engines und
/// proxied Analyse-Anfragen an den Lichess-Broker — der ndjson-Stream geht 1:1 an den Browser.
/// So funktioniert im Analysebrett jede Engine, die der User für Lichess eingerichtet hat
/// (eigene Maschine via offiziellem Provider, Miet-Anbieter wie stockfishcloud), ohne CSP-/
/// CORS-Aufweichung; das clientSecret bleibt serverseitig (<see cref="LichessEngineService"/>).
/// </summary>
[ApiController]
[Route("api/engine")]
[Authorize]
public class EngineController : BaseApiController
{
    /// <summary>Obergrenzen der durchgereichten Work-Parameter — schützt Provider (und unser
    /// Proxy-Streaming) vor absurden Anfragen; die Engine-Maxima klemmen zusätzlich.</summary>
    private const int MaxDepth = 60;
    private const int MaxMovetimeMs = 300_000;
    private const long MaxNodes = 5_000_000_000;
    private const int MaxMoves = 600;

    /// <summary>Absolute Obergrenze für EINEN Analyse-Stream. Keines der Work-Limits begrenzt die
    /// Laufzeit (Tiefe 60 rechnet auf echter Hardware Stunden), und der einzige andere Abbruchgrund
    /// wäre der Browser selbst — ohne diese Schranke hielte ein Client Verbindungen beliebig lange.</summary>
    private static readonly TimeSpan MaxStreamDuration = TimeSpan.FromMinutes(10);

    /// <summary>Gleichzeitige Analyse-Streams je User (mehrere Tabs sind legitim, hundert nicht).
    /// Der globale Limiter begrenzt nur die RATE neuer Anfragen, nicht die Zahl offener Ströme.</summary>
    private const int MaxConcurrentStreamsPerUser = 4;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> ActiveStreams = new();

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly LichessEngineService _lichess;
    private readonly ILogger<EngineController> _logger;

    public EngineController(AppDbContext db, EncryptionService encryption,
        LichessEngineService lichess, ILogger<EngineController> logger)
    {
        _db = db;
        _encryption = encryption;
        _lichess = lichess;
        _logger = logger;
    }

    [HttpGet("credentials")]
    public async Task<IActionResult> GetCredentials()
    {
        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is null)
            return Ok(new LichessEngineCredentialResponse(false, null));

        // Robust gegen Key-Rotation/korrupte Daten: kein 500, nur keine Maske (Re-Eingabe nötig).
        var plain = _encryption.TryDecrypt(cred.EncryptedToken);
        return Ok(new LichessEngineCredentialResponse(true, plain is null ? null : Mask(plain)));
    }

    [HttpPost("credentials")]
    public async Task<IActionResult> SaveCredentials([FromBody] SaveLichessTokenRequest request)
    {
        var token = request?.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Token is required" });
        if (token.Length > 200)
            return BadRequest(new { message = "Token too long" });

        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        var now = DateTime.UtcNow;
        if (cred is null)
        {
            cred = new LichessEngineCredential { UserId = userId, CreatedAt = now };
            _db.LichessEngineCredentials.Add(cred);
        }
        cred.EncryptedToken = _encryption.Encrypt(token);
        cred.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return Ok(new LichessEngineCredentialResponse(true, Mask(token)));
    }

    [HttpDelete("credentials")]
    public async Task<IActionResult> DeleteCredentials()
    {
        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is not null)
        {
            _db.LichessEngineCredentials.Remove(cred);
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>Auf dem Lichess-Konto registrierte External Engines (ohne clientSecret).
    /// Immer 200: ohne Token bzw. bei abgewiesenem Token sagt die Antwort WARUM die Liste leer
    /// ist — das Analysebrett entscheidet damit in EINEM Call, ob es einen Picker zeigt.</summary>
    [HttpGet("external")]
    public async Task<IActionResult> ListExternalEngines(CancellationToken ct)
    {
        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var token = cred is null ? null : _encryption.TryDecrypt(cred.EncryptedToken);
        if (token is null)
            return Ok(new ExternalEnginesResponse(cred is not null, false, []));

        try
        {
            var result = await _lichess.ListEnginesAsync(userId, token, ct);
            var engines = result.Engines
                .Select(e => new ExternalEngineDto(e.Id, e.Name, e.MaxThreads, e.MaxHash))
                .ToList();
            return Ok(new ExternalEnginesResponse(true, result.Unauthorized, engines));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Lichess-Engine-Liste fehlgeschlagen (User {UserId})", userId);
            return StatusCode(502, new { message = "Lichess unreachable" });
        }
    }

    /// <summary>
    /// Analyse über eine External Engine des Users — proxied den ndjson-Stream des Lichess-Brokers
    /// 1:1 durch. Die Suche endet, wenn das Limit erreicht ist oder der Browser die Verbindung
    /// schließt (der Abbruch wandert über <c>RequestAborted</c> zum Broker → Provider stoppt).
    /// </summary>
    [HttpPost("external/{id}/analyse")]
    public async Task<IActionResult> Analyse(string id, [FromBody] EngineAnalyseRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var error = ValidateWork(request);
        if (error is not null)
            return BadRequest(new { message = error });

        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var token = cred is null ? null : _encryption.TryDecrypt(cred.EncryptedToken);
        if (token is null)
            return BadRequest(new { message = "No Lichess token configured" });

        LichessExternalEngine? engine;
        try
        {
            engine = await _lichess.ResolveEngineAsync(userId, token, id, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "External-Engine-Auflösung fehlgeschlagen (User {UserId})", userId);
            return StatusCode(502, new { message = "Lichess unreachable" });
        }
        if (engine is null)
            return NotFound(new { message = "Engine not found" });

        // Threads/Hash IMMER auf die von Lichess gemeldeten Engine-Maxima klemmen
        // (Maxima defensiv auf ≥1, sonst wirft Clamp bei kaputten Upstream-Daten).
        var maxThreads = Math.Max(1, engine.MaxThreads);
        var maxHash = Math.Max(1, engine.MaxHash);
        var threads = Math.Clamp(request.Threads ?? maxThreads, 1, maxThreads);
        var hash = Math.Clamp(request.Hash ?? maxHash, 1, maxHash);
        object work = BuildWork(request, threads, hash);

        // Ein Stream hält eine Verbindung, solange die Engine rechnet — deshalb ein Deckel je User.
        if (ActiveStreams.AddOrUpdate(userId, 1, (_, n) => n + 1) > MaxConcurrentStreamsPerUser)
        {
            ReleaseStream(userId);
            return StatusCode(429, new { message = "Too many concurrent analysis streams" });
        }

        // Absolute Laufzeitschranke ZUSÄTZLICH zum Browser-Abbruch (RequestAborted).
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamCts.CancelAfter(MaxStreamDuration);
        var streamCt = streamCts.Token;

        try
        {
            HttpResponseMessage upstream;
            try
            {
                upstream = await _lichess.AnalyseAsync(engine, work, streamCt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Der Browser hat abgebrochen, bevor der Broker antwortete — das ist der NORMALE
                // Weg bei jedem Stellungswechsel und kein Fehler. Ohne diesen Zweig landete er im
                // catch darunter und erschien als 502 „Broker nicht erreichbar" im Log.
                return new EmptyResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "External-Engine-Analyse nicht erreichbar (Engine {EngineId})", id);
                return StatusCode(502, new { message = "Engine broker unreachable" });
            }

            using (upstream)
            {
                if (!upstream.IsSuccessStatusCode)
                {
                    _logger.LogWarning("External-Engine-Analyse abgewiesen: {Status} (Engine {EngineId})",
                        (int)upstream.StatusCode, id);
                    return StatusCode(502, new { message = "Engine broker rejected the request" });
                }

                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "application/x-ndjson";
                Response.Headers.CacheControl = "no-cache";
                // nginx: diese Antwort NICHT puffern — die info-Zeilen müssen live beim Browser ankommen.
                Response.Headers["X-Accel-Buffering"] = "no";
                HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                try
                {
                    await using var stream = await upstream.Content.ReadAsStreamAsync(streamCt);
                    await stream.CopyToAsync(Response.Body, streamCt);
                }
                catch (OperationCanceledException)
                {
                    // Browser hat abgebrochen (Stellungswechsel/Seite zu) ODER die Laufzeitschranke
                    // hat gegriffen — beides sind reguläre Stopp-Wege, der Client hat seine Zeilen.
                }
                catch (IOException ex)
                {
                    // Upstream mitten im Stream weg (Provider offline): Stream endet einfach; der
                    // Client wertet aus, was er hat. Status ist längst gesendet.
                    _logger.LogWarning(ex, "External-Engine-Stream abgerissen (Engine {EngineId})", id);
                }
            }
        }
        finally
        {
            ReleaseStream(userId);
        }
        return new EmptyResult();
    }

    /// <summary>Zähler des Users freigeben; auf 0 die Zeile ganz entfernen (kein Wachstum über die Zeit).</summary>
    private static void ReleaseStream(int userId)
    {
        if (ActiveStreams.AddOrUpdate(userId, 0, (_, n) => n - 1) <= 0)
            ActiveStreams.TryRemove(userId, out _);
    }

    /// <summary>Baut das Lichess-Work-Objekt (oneOf depth/movetime/nodes + gemeinsame Felder).</summary>
    private static object BuildWork(EngineAnalyseRequest r, int threads, int hash)
    {
        var sessionId = r.SessionId!;
        var initialFen = r.InitialFen!;
        var moves = r.Moves ?? [];
        var multiPv = Math.Clamp(r.MultiPv, 1, 5);
        if (r.Depth is int depth)
            return new { sessionId, threads, hash, multiPv, variant = "chess", initialFen, moves, depth };
        if (r.Movetime is int movetime)
            return new { sessionId, threads, hash, multiPv, variant = "chess", initialFen, moves, movetime };
        return new { sessionId, threads, hash, multiPv, variant = "chess", initialFen, moves, nodes = r.Nodes!.Value };
    }

    private static string? ValidateWork(EngineAnalyseRequest? r)
    {
        if (r is null) return "Request body is required";
        if (string.IsNullOrWhiteSpace(r.SessionId) || r.SessionId.Length > 64) return "Invalid sessionId";
        if (string.IsNullOrWhiteSpace(r.InitialFen) || r.InitialFen.Length > 120) return "Invalid initialFen";
        if (r.Moves is { Count: > MaxMoves }) return "Too many moves";
        if (r.Moves is not null && r.Moves.Any(m => string.IsNullOrWhiteSpace(m) || m.Length > 5)) return "Invalid move";

        var limits = new[] { r.Depth.HasValue, r.Movetime.HasValue, r.Nodes.HasValue }.Count(x => x);
        if (limits != 1) return "Exactly one of depth/movetime/nodes is required";
        if (r.Depth is < 1 or > MaxDepth) return "Invalid depth";
        if (r.Movetime is < 1 or > MaxMovetimeMs) return "Invalid movetime";
        if (r.Nodes is < 1 or > MaxNodes) return "Invalid nodes";
        return null;
    }

    private static string Mask(string value)
    {
        // Nur die letzten 4 Zeichen zur Wiedererkennung zeigen (wie beim Chessable-Bearer).
        if (value.Length <= 4) return new string('*', value.Length);
        return new string('*', Math.Min(20, value.Length - 4)) + value[^4..];
    }
}
