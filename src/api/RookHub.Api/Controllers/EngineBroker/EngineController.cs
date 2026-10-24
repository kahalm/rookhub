using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Accounts;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Controllers.EngineBroker;

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
/// Broker, <see cref="IEngineBroker"/>. Die Einstellungen (Token, Hintergrund-Liste, Haus-Engine) liegen in
/// <see cref="EngineSettingsService"/>, Prüfen/Klemmen der Arbeit und das Durchreichen des Streams in
/// <see cref="EngineStreamProxy"/> (Zerlegung Z12); hier bleiben Bindung, Aufruf und Statuscode.
/// </summary>
[ApiController]
[Route("api/engine")]
[Authorize]
public class EngineController : BaseApiController
{
    private readonly EngineRegistry _registry;
    private readonly IEngineBroker _broker;
    private readonly EngineActivityTracker _activity;
    private readonly ILogger<EngineController> _logger;
    private readonly EngineSettingsService _settings;

    public EngineController(AppDbContext db, EncryptionService encryption, EngineRegistry registry,
        IEngineBroker broker, EngineActivityTracker activity, ILogger<EngineController> logger)
    {
        _registry = registry;
        _broker = broker;
        _activity = activity;
        _logger = logger;
        _settings = new EngineSettingsService(db, encryption, registry);
    }

    /// <summary>Absage einer Einstellung als 400 bzw. 404 mit <c>{ message }</c>.</summary>
    private IActionResult Rejected(EngineSettingsRejection r)
        => r.NotFound ? NotFound(new { message = r.Message }) : BadRequest(new { message = r.Message });

    [HttpGet("credentials")]
    public async Task<IActionResult> GetCredentials()
        => Ok(await _settings.GetCredentialsAsync(GetUserId()));

    [HttpPost("credentials")]
    public async Task<IActionResult> SaveCredentials([FromBody] SaveLichessTokenRequest request)
    {
        var (saved, rejection) = await _settings.SaveTokenAsync(GetUserId(), request?.Token);
        return rejection is not null ? Rejected(rejection) : Ok(saved);
    }

    /// <summary>Lichess-Token löschen (siehe <see cref="EngineSettingsService.DeleteTokenAsync"/>).</summary>
    [HttpDelete("credentials")]
    public async Task<IActionResult> DeleteCredentials()
    {
        await _settings.DeleteTokenAsync(GetUserId());
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
        EngineListing listing = null!;
        if (await TryUpstreamAsync(async () => listing = await _registry.ListAsync(userId, ct),
                ex => _logger.LogWarning(ex, "Lichess-Engine-Liste fehlgeschlagen (User {UserId})", userId), ct) is { } listFailed)
            return listFailed;
        if (listing.LichessUnreachable)
            _logger.LogWarning("Lichess-Engine-Liste fehlgeschlagen (User {UserId}) — nur eigene Engines", userId);
        return Ok(EngineSettingsService.ToResponse(listing, IsAdmin));
    }

    /// <summary>Hintergrund-Engines fuer Analyseauftraege festlegen (leere Liste = keine) — siehe
    /// <see cref="EngineSettingsService.PlanBackgroundAsync"/>. Jede muss registriert sein, sonst 404.</summary>
    [HttpPut("background")]
    public async Task<IActionResult> SetBackgroundEngine([FromBody] SetBackgroundEngineRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var (plan, rejection) = await _settings.PlanBackgroundAsync(userId, request?.EngineIds, ct);
        if (rejection is not null) return Rejected(rejection);
        if (plan!.NothingToStore)
            return Ok(new { backgroundEngineIds = Array.Empty<string>() });

        if (plan.Ids.Count > 0)
        {
            var missing = false;
            if (await TryUpstreamAsync(async () => missing = !await _settings.AllRegisteredAsync(userId, plan.Ids, ct),
                    ex => _logger.LogWarning(ex, "Lichess nicht erreichbar beim Setzen der Hintergrund-Engines (User {UserId})", userId),
                    ct) is { } resolveFailed)
                return resolveFailed;
            if (missing)
                return NotFound(new { message = "Engine not found" });
        }

        return Ok(new { backgroundEngineIds = await _settings.SaveBackgroundAsync(userId, plan, ct) });
    }

    /// <summary>
    /// Die eigenen Hintergrund-Engines als „Haus-Engine" freigeben (<see cref="EngineSettingsService.SetHouseEngineAsync"/>).
    /// NUR ein Admin darf das setzen: hier verschenkt jemand Rechenzeit seiner Maschine an alle registrierten Nutzer,
    /// und diese Entscheidung gehoert nicht in die Hand eines beliebigen Kontos.
    /// </summary>
    [HttpPut("house")]
    public async Task<IActionResult> SetHouseEngine([FromBody] SetHouseEngineRequest request, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        var (shared, rejection) = await _settings.SetHouseEngineAsync(GetUserId(), request?.Share ?? false, ct);
        return rejection is not null ? Rejected(rejection) : Ok(new { shareAsHouseEngine = shared });
    }

    /// <summary>
    /// Analyse über eine External Engine des Users — beim eigenen Broker (<c>rhe_</c>, in-Prozess) oder beim
    /// Lichess-Broker (<c>eei_</c>); der ndjson-Stream geht in beiden Fällen gleich an den Browser
    /// (<see cref="EngineStreamProxy.PumpToClientAsync"/>). Die Suche endet, wenn das Limit erreicht ist oder der
    /// Browser die Verbindung schließt (der Abbruch wandert über <c>RequestAborted</c> zum Broker → Provider stoppt).
    /// </summary>
    [HttpPost("external/{id}/analyse")]
    public async Task<IActionResult> Analyse(string id, [FromBody] EngineAnalyseRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var error = EngineStreamProxy.ValidateWork(request);
        if (error is not null)
            return BadRequest(new { message = error });

        EngineLookup lookup = null!;
        if (await TryUpstreamAsync(async () => lookup = await _registry.ResolveAsync(userId, id, ct),
                ex => _logger.LogWarning(ex, "External-Engine-Auflösung fehlgeschlagen (User {UserId})", userId), ct) is { } lookupFailed)
            return lookupFailed;
        if (lookup.Failure == EngineLookupFailure.NoToken)
            return BadRequest(new { message = "No Lichess token configured" });
        if (lookup.Engine is not { } engine)
            return NotFound(new { message = "Engine not found" });

        var work = EngineStreamProxy.WorkFor(request, engine);

        // Ein Stream hält eine Verbindung, solange die Engine rechnet — deshalb ein Deckel je User.
        // Begin() meldet dem Hintergrund-Worker zugleich „Live rechnet" → dessen Auftrag pausiert.
        if (_activity.Begin(userId, engine.Id) > EngineStreamProxy.MaxConcurrentStreamsPerUser)
        {
            _activity.End(userId, engine.Id);
            return StatusCode(429, new { message = "Too many concurrent analysis streams" });
        }

        // Absolute Laufzeitschranke ZUSÄTZLICH zum Browser-Abbruch (RequestAborted).
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamCts.CancelAfter(EngineStreamProxy.MaxStreamDuration);
        var streamCt = streamCts.Token;

        try
        {
            EngineAnalysisSession upstream = null!;
            if (await TryUpstreamAsync(async () => upstream = await _broker.AnalyseAsync(engine, work, streamCt),
                    ex => _logger.LogWarning(ex, "External-Engine-Analyse nicht erreichbar (Engine {EngineId})", id), ct,
                    "Engine broker unreachable") is { } analyseFailed)
                return analyseFailed;

            await using (upstream)
            {
                if (!upstream.IsSuccess)
                {
                    _logger.LogWarning("External-Engine-Analyse abgewiesen: {Status} (Engine {EngineId})",
                        upstream.StatusCode, id);
                    return StatusCode(502, new { message = "Engine broker rejected the request" });
                }
                await EngineStreamProxy.PumpToClientAsync(HttpContext, upstream, id, _logger, streamCt);
            }
        }
        finally
        {
            _activity.End(userId, engine.Id);
        }
        return new EmptyResult();
    }

    /// <summary>
    /// EINE Fehlerregel für die Aufrufe an Lichess bzw. den Broker (Codereview A4-008 — vorher vier catch-Blöcke,
    /// nur einer nahm den Browser-Abbruch heraus). Bricht der Browser ab (<paramref name="ct"/> =
    /// <c>RequestAborted</c>), ist das der NORMALE Weg bei jedem Stellungswechsel und kein Fehler: leere Antwort,
    /// kein Log. Sonst wird ein <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/> (Timeout)
    /// mit dem Log des Aufrufers zu 502. <c>null</c> = der Aufruf ging durch.
    /// </summary>
    private async Task<IActionResult?> TryUpstreamAsync(Func<Task> call, Action<Exception> logFailure,
        CancellationToken ct, string failureMessage = "Lichess unreachable")
    {
        try
        {
            await call();
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logFailure(ex);
            return StatusCode(502, new { message = failureMessage });
        }
    }
}
