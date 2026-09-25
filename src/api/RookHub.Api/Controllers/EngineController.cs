using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Controllers;

/// <summary>
/// External-Engine-Anbindung, Seite des BROWSERS — für ZWEI Quellen in einer Auswahl:
/// <list type="bullet">
/// <item><b>RookHub direkt</b> (<c>rhe_…</c>): der Provider auf dem Rechner des Nutzers hat sich beim
/// eigenen Broker angemeldet (<see cref="ExternalEngineController"/>); kein Lichess-Konto nötig.</item>
/// <item><b>Lichess</b> (<c>eei_…</c>, Lichess-Client-Modus): der User hinterlegt einen Lichess-API-Token
/// (Scope <c>engine:read</c>); RookHub listet damit seine registrierten External Engines und proxied
/// Analyse-Anfragen an den Lichess-Broker — so bleiben auch Cloud-Anbieter (stockfishcloud, Chessify)
/// nutzbar, die selbst als Lichess-Provider auftreten.</item>
/// </list>
/// Der ndjson-Stream geht in beiden Fällen gleich an den Browser, ohne CSP-/CORS-Aufweichung; das
/// clientSecret bleibt serverseitig. Welche Quelle, entscheidet <see cref="EngineRegistry"/>, welcher
/// Broker, <see cref="IEngineBroker"/>.
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
    /// Der globale Limiter begrenzt nur die RATE neuer Anfragen, nicht die Zahl offener Ströme.
    /// Gezählt wird im <see cref="EngineActivityTracker"/> — der auch dem Hintergrund-Worker sagt,
    /// wann Live rechnet (Hintergrund pausieren) und seit wann Ruhe ist.</summary>
    private const int MaxConcurrentStreamsPerUser = 4;

    /// <summary>Abstand der Leerzeilen-Lebenszeichen im Analyse-Stream (siehe <see cref="NdjsonHeartbeatPump"/>).</summary>
    private static readonly TimeSpan HeartbeatInterval = NdjsonHeartbeatPump.DefaultInterval;

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly EngineRegistry _registry;
    private readonly IEngineBroker _broker;
    private readonly EngineActivityTracker _activity;
    private readonly ILogger<EngineController> _logger;

    public EngineController(AppDbContext db, EncryptionService encryption, EngineRegistry registry,
        IEngineBroker broker, EngineActivityTracker activity, ILogger<EngineController> logger)
    {
        _db = db;
        _encryption = encryption;
        _registry = registry;
        _broker = broker;
        _activity = activity;
        _logger = logger;
    }

    /// <summary>Ein Lichess-Token ist hinterlegt (die Zeile gibt es seit dem eigenen Broker auch OHNE Token:
    /// sie trägt die Hintergrund-Liste und die Haus-Engine-Freigabe).</summary>
    private static bool HasToken(LichessEngineCredential? cred) => !string.IsNullOrEmpty(cred?.EncryptedToken);

    [HttpGet("credentials")]
    public async Task<IActionResult> GetCredentials()
    {
        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (!HasToken(cred))
            return Ok(new LichessEngineCredentialResponse(false, null));

        // Robust gegen Key-Rotation/korrupte Daten: kein 500, nur keine Maske (Re-Eingabe nötig).
        var plain = _encryption.TryDecrypt(cred!.EncryptedToken);
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

    /// <summary>Lichess-Token löschen. Die Zeile verschwindet ganz, wenn danach nichts mehr an ihr hängt;
    /// stehen „RookHub direkt"-Engines in der Hintergrund-Liste, bleibt sie (ohne Token) mit genau diesen —
    /// die Lichess-Engines fallen heraus, ohne Token rechnet dort niemand.</summary>
    [HttpDelete("credentials")]
    public async Task<IActionResult> DeleteCredentials()
    {
        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is not null)
        {
            var local = cred.BackgroundEngines.Where(EngineRegistry.IsLocal).ToList();
            if (local.Count == 0)
            {
                _db.LichessEngineCredentials.Remove(cred);
            }
            else
            {
                cred.EncryptedToken = string.Empty;
                cred.SetBackgroundEngines(local);
                cred.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>Alle Engines des Kontos aus BEIDEN Quellen (ohne clientSecret): zuerst „RookHub direkt"
    /// (mit Online-Punkt), dann die des Lichess-Kontos. Immer 200: ohne Token bzw. bei abgewiesenem Token
    /// sagt die Antwort WARUM der Lichess-Teil leer ist — das Analysebrett entscheidet damit in EINEM Call,
    /// ob es einen Picker zeigt. Antwortet Lichess nicht, gibt es wie bisher 502 — außer es gibt eigene
    /// Engines; dann kommen die, mit <c>lichessUnreachable</c>.</summary>
    [HttpGet("external")]
    public async Task<IActionResult> ListExternalEngines(CancellationToken ct)
    {
        var userId = GetUserId();
        EngineListing listing;
        try
        {
            listing = await _registry.ListAsync(userId, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Lichess-Engine-Liste fehlgeschlagen (User {UserId})", userId);
            return StatusCode(502, new { message = "Lichess unreachable" });
        }
        if (listing.LichessUnreachable)
            _logger.LogWarning("Lichess-Engine-Liste fehlgeschlagen (User {UserId}) — nur eigene Engines", userId);

        var cred = listing.Credential;
        var engines = listing.Engines.Select(ToDto).ToList();
        return Ok(new ExternalEnginesResponse(HasToken(cred), listing.TokenInvalid, engines, cred?.BackgroundEngines ?? [],
            cred?.ShareAsHouseEngine ?? false, IsAdmin, listing.LichessUnreachable));
    }

    private static ExternalEngineDto ToDto(EngineRef e) => new(e.Id, e.Name, e.MaxThreads, e.MaxHash,
        e.Source == EngineSource.Local ? ExternalEngineDto.SourceRookHub : ExternalEngineDto.SourceLichess, e.Online);

    /// <summary>Hoechstzahl hinterlegbarer Hintergrund-Engines. Nicht die Welt begrenzen, aber auch
    /// nicht unbegrenzt: die Liste steht als CSV in einer Spalte, und jede Engine kostet den Worker
    /// einen eigenen Broker-Stream.
    ///
    /// <para>Seit 2026-09-11 sechzehn statt acht: ein Provider auf einer 40-Kern-Maschine meldet
    /// eine Live-Engine und zwoelf Hintergrund-Engines an, und mit acht blieb die Haelfte davon
    /// unbenutzbar, sobald die vier einer zweiten Maschine noch in der Liste standen. Sechzehn
    /// passen in die Spalte: <see cref="LichessEngineCredential.BackgroundEngineIds"/> fasst 600
    /// Zeichen, eine Kennung ist rund 17 lang (<c>eei_</c> + 12 + Komma) — 16 belegen also etwa
    /// 272. Der Worker rechnet je Engine EINEN Auftrag; der Deckel von vier gleichzeitigen Stroemen
    /// (<see cref="MaxConcurrentStreamsPerUser"/>) gilt nur dem Live-Proxy, nicht ihm.</para></summary>
    private const int MaxBackgroundEngines = 16;

    /// <summary>
    /// Hintergrund-Engines fuer Analyseauftraege festlegen (leere Liste = keine). Jede muss eine der
    /// registrierten Engines sein — sonst liefe der Worker gegen eine Wand.
    ///
    /// <para>MEHRERE sind der Sinn: der Worker rechnet je Engine genau einen Auftrag, also laufen so
    /// viele Auftraege nebeneinander, wie hier stehen. Mit einer einzigen ist die Warteschlange
    /// strikt seriell, und ein einzelner zaeher Auftrag legt alles still.</para>
    /// </summary>
    [HttpPut("background")]
    public async Task<IActionResult> SetBackgroundEngine([FromBody] SetBackgroundEngineRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);

        var ids = (request?.EngineIds ?? [])
            .Select(i => i?.Trim() ?? string.Empty)
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count > MaxBackgroundEngines)
            return BadRequest(new { message = $"At most {MaxBackgroundEngines} background engines" });
        if (ids.Any(i => i.Length > 64))
            return BadRequest(new { message = "Invalid engine id" });

        // Lichess-Engines brauchen den Token (Meldungen wie bisher); „RookHub direkt" nicht — ein Nutzer ohne
        // Lichess-Konto bekommt die Zeile dann eben ohne Token.
        var needsLichess = ids.Any(i => !EngineRegistry.IsLocal(i));
        if (needsLichess && cred is null)
            return BadRequest(new { message = "No Lichess token stored" });
        if (needsLichess && _encryption.TryDecrypt(cred!.EncryptedToken) is null)
            return BadRequest(new { message = "Stored token unreadable" });
        if (cred is null && ids.Count == 0)
            return Ok(new { backgroundEngineIds = Array.Empty<string>() });

        if (ids.Count > 0)
        {
            try
            {
                // JEDE pruefen: eine nicht registrierte Engine in der Liste hiesse, dass ein Teil der
                // Auftraege still in einer Warteschlange landet, die niemand abarbeitet.
                foreach (var id in ids)
                    if ((await _registry.ResolveAsync(userId, id, ct)).Engine is null)
                        return NotFound(new { message = "Engine not found" });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Lichess nicht erreichbar beim Setzen der Hintergrund-Engines (User {UserId})", userId);
                return StatusCode(502, new { message = "Lichess unreachable" });
            }
        }

        if (cred is null)
        {
            cred = new LichessEngineCredential { UserId = userId, EncryptedToken = string.Empty, CreatedAt = DateTime.UtcNow };
            _db.LichessEngineCredentials.Add(cred);
        }
        cred.SetBackgroundEngines(ids);
        cred.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { backgroundEngineIds = cred.BackgroundEngines });
    }

    /// <summary>
    /// Die eigenen Hintergrund-Engines auch fremden Partien oeffnen, die jemand auf der
    /// Punktepartie-Seite einwirft („Haus-Engine"). NUR ein Admin darf das setzen: hier verschenkt
    /// jemand Rechenzeit seiner Maschine an alle registrierten Nutzer, und diese Entscheidung
    /// gehoert nicht in die Hand eines beliebigen Kontos.
    ///
    /// <para>Freigegeben sind immer ALLE hinterlegten Hintergrund-Engines. Eine Auswahl davon waere
    /// eine zweite Liste neben der ersten — und zwei Listen, die dasselbe meinen, laufen
    /// auseinander. Wer weniger teilen will, hinterlegt weniger.</para>
    /// </summary>
    [HttpPut("house")]
    public async Task<IActionResult> SetHouseEngine([FromBody] SetHouseEngineRequest request, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();

        var userId = GetUserId();
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var share = request?.Share ?? false;
        // Ohne Zeile gibt es keine Hintergrund-Engine (weder Token noch „RookHub direkt"): wie bisher abweisen.
        if (cred is null)
            return BadRequest(new { message = share ? "No background engine configured" : "No Lichess token stored" });

        // Ohne hinterlegte Hintergrund-Engine gaebe die Freigabe nichts her: die Partien der anderen
        // landeten in einer Warteschlange, die niemand abarbeitet.
        if (share && cred.BackgroundEngines.Count == 0)
            return BadRequest(new { message = "No background engine configured" });

        if (cred.ShareAsHouseEngine != share)
        {
            cred.ShareAsHouseEngine = share;
            cred.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new { shareAsHouseEngine = cred.ShareAsHouseEngine });
    }

    /// <summary>
    /// Analyse über eine External Engine des Users — beim eigenen Broker (<c>rhe_</c>, in-Prozess) oder beim
    /// Lichess-Broker (<c>eei_</c>); der ndjson-Stream geht in beiden Fällen gleich an den Browser. Die Suche
    /// endet, wenn das Limit erreicht ist oder der Browser die Verbindung schließt (der Abbruch wandert über
    /// <c>RequestAborted</c> zum Broker → Provider stoppt).
    /// </summary>
    [HttpPost("external/{id}/analyse")]
    public async Task<IActionResult> Analyse(string id, [FromBody] EngineAnalyseRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var error = ValidateWork(request);
        if (error is not null)
            return BadRequest(new { message = error });

        EngineLookup lookup;
        try
        {
            lookup = await _registry.ResolveAsync(userId, id, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "External-Engine-Auflösung fehlgeschlagen (User {UserId})", userId);
            return StatusCode(502, new { message = "Lichess unreachable" });
        }
        if (lookup.Failure == EngineLookupFailure.NoToken)
            return BadRequest(new { message = "No Lichess token configured" });
        if (lookup.Engine is not { } engine)
            return NotFound(new { message = "Engine not found" });

        // Threads/Hash IMMER auf die gemeldeten Engine-Maxima klemmen
        // (Maxima defensiv auf ≥1, sonst wirft Clamp bei kaputten Upstream-Daten).
        var maxThreads = Math.Max(1, engine.MaxThreads);
        var maxHash = Math.Max(1, engine.MaxHash);
        var threads = Math.Clamp(request.Threads ?? maxThreads, 1, maxThreads);
        var hash = Math.Clamp(request.Hash ?? maxHash, 1, maxHash);
        var work = BuildWork(request, threads, hash);

        // Ein Stream hält eine Verbindung, solange die Engine rechnet — deshalb ein Deckel je User.
        // Begin() meldet dem Hintergrund-Worker zugleich „Live rechnet" → dessen Auftrag pausiert.
        if (_activity.Begin(userId, engine.Id) > MaxConcurrentStreamsPerUser)
        {
            _activity.End(userId, engine.Id);
            return StatusCode(429, new { message = "Too many concurrent analysis streams" });
        }

        // Absolute Laufzeitschranke ZUSÄTZLICH zum Browser-Abbruch (RequestAborted).
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamCts.CancelAfter(MaxStreamDuration);
        var streamCt = streamCts.Token;

        try
        {
            EngineAnalysisSession upstream;
            try
            {
                upstream = await _broker.AnalyseAsync(engine, work, streamCt);
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

            await using (upstream)
            {
                if (!upstream.IsSuccess)
                {
                    _logger.LogWarning("External-Engine-Analyse abgewiesen: {Status} (Engine {EngineId})",
                        upstream.StatusCode, id);
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
                    var stream = upstream.Ndjson!;
                    // Kein nacktes CopyToAsync: bei Funkstille des Brokers (MultiPV 5 ab Tiefe ~27
                    // vergehen Minuten zwischen zwei Zeilen) schreibt der Pump Leerzeilen, damit kein
                    // Proxy davor (NPM 60 s!) die Verbindung als tot kappt — siehe NdjsonHeartbeatPump.
                    await NdjsonHeartbeatPump.PumpAsync(stream, Response.Body, HeartbeatInterval, streamCt);
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
            _activity.End(userId, engine.Id);
        }
        return new EmptyResult();
    }

    /// <summary>Baut das Work-Objekt (oneOf depth/movetime/nodes + gemeinsame Felder; <c>variant</c> fest
    /// <c>chess</c>) — dasselbe für beide Broker.</summary>
    private static EngineWork BuildWork(EngineAnalyseRequest r, int threads, int hash) =>
        new(r.SessionId!, threads, hash, Math.Clamp(r.MultiPv, 1, 5), r.InitialFen!, r.Moves ?? [],
            r.Depth, r.Depth is null ? r.Movetime : null, r.Depth is null && r.Movetime is null ? r.Nodes : null);

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
