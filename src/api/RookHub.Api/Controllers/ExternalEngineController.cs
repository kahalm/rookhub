using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;
using Serilog.Context;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der eigene Engine-Broker, Seite des PROVIDERS: dieselben Endpunkte, die der offizielle
/// Lichess-Provider (<c>example-provider.py</c>) bei Lichess anspricht — mit <c>--lichess</c> und
/// <c>--broker</c> auf RookHub gerichtet, meldet sich die Engine auf dem Rechner des Nutzers direkt
/// hier an und holt hier ihre Arbeit. Lichess ist dafür nicht mehr nötig.
///
/// <para><b>Registrierung</b> (<c>GET/POST/PUT/DELETE</c>): Bearer = RookHub-API-Token mit Scope
/// <c>engine</c>. Mit einem Browser-Login (JWT) nur Lesen und Löschen — registrieren tut
/// ausschließlich der Provider. Alle vier bleiben unter dem globalen Rate-Limiter (selten).</para>
///
/// <para><b>Arbeit</b> (<c>POST work</c> = Long-Poll, <c>POST work/{id}</c> = Upload): anonym — der Provider
/// weist sich über sein <c>providerSecret</c> bzw. die Kennung des abgeholten Auftrags aus — und
/// VOM RATE-LIMITER AUSGENOMMEN: 13 Provider pollen 78-mal je Minute, dazu die Uploads; unter dem globalen
/// Deckel (100/min je IP) bauten wir genau die Drosselung nach, deretwegen es diesen Broker gibt. Ein
/// unbekanntes Secret bekommt nach der Wartezeit dasselbe 204 wie ein bekanntes ohne Arbeit — kein 401/404
/// im Takt (log-watcher) und keine Auskunft nach außen. Solche Polls sind je Adresse gedeckelt
/// (<see cref="UnknownSelectorThrottle"/>), der Rumpf des Polls auf <see cref="MaxAcquireBodyBytes"/>.</para>
/// </summary>
[ApiController]
[Route("api/external-engine")]
[Authorize]
// Ein API-Token eines ANDEREN Scopes hat hier nichts verloren (zweite Schranke neben dem zentralen Scope-Zaun); JWT
// und die anonymen Provider-Endpunkte (kein scope-Claim) kommen durch.
[RequireTokenScope(ApiTokenService.EngineScope, Message = "API token scope 'engine' required")]
public class ExternalEngineController : BaseApiController
{
    /// <summary>Anlegen/Ändern nur mit Engine-Token: der Provider ist der Einzige, der registriert.</summary>
    private const string ProviderOnly = "Engines are registered by the provider (API token with scope 'engine')";

    private readonly ExternalEngineRegistrationService _registrations;
    private readonly LocalBrokerOptions _options;
    private readonly ILogger<ExternalEngineController> _logger;
    private readonly EngineHub _hub;
    private readonly EngineSelectorDirectory _directory;
    private readonly UnknownSelectorThrottle _unknownSelectors;

    /// <summary>Deckel für den Rumpf des Long-Polls: <c>{"providerSecret":"…"}</c> mit höchstens
    /// <see cref="ExternalEngineRegistrationService.MaxProviderSecretLength"/> Zeichen — echte Provider schicken unter
    /// 200 Byte. Ohne ihn band MVC bis zu Kestrels 30 MB in einen String, bevor die Längenprüfung griff.</summary>
    public const int MaxAcquireBodyBytes = 4096;

    public ExternalEngineController(ExternalEngineRegistrationService registrations, LocalBrokerOptions options,
        ILogger<ExternalEngineController> logger, EngineHub hub, EngineSelectorDirectory directory,
        UnknownSelectorThrottle unknownSelectors)
    {
        _registrations = registrations;
        _options = options;
        _logger = logger;
        _hub = hub;
        _directory = directory;
        _unknownSelectors = unknownSelectors;
    }

    /// <summary>Scope des API-Tokens; <c>null</c> = Browser-Login (JWT).</summary>
    private string? TokenScope => User.FindFirst("scope")?.Value;

    private string Username => User.Identity?.Name ?? GetUserId().ToString();

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        var withSecret = TokenScope == ApiTokenService.EngineScope;
        var list = await _registrations.ListAsync(GetUserId(), ct);
        return Ok(list.Select(r => ExternalEngineRegistrationService.ToDto(r, Username, withSecret)).ToList());
    }

    [HttpPost]
    [RequireTokenScope(ApiTokenService.EngineScope, AllowJwt = false, Message = ProviderOnly)]
    public async Task<IActionResult> Create([FromBody] ExternalEngineRegistrationRequest request, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        return ToResult(await _registrations.SaveAsync(GetUserId(), null, request, ct));
    }

    /// <summary>
    /// Zeitplan-Meldung eines Engine-Clients (0.679.0): „wenn der client betriebszeiten meldet halte ich mich an die,
    /// wenn nicht nehm ich die voreingestellten von rookhub". Der Client schickt seinen Zeitplan samt Platz jeder Engine;
    /// eine leere Regel loescht die Meldung wieder. Literal-Route VOR <c>{id}</c>.
    /// </summary>
    [HttpPut("schedule")]
    [RequireTokenScope(ApiTokenService.EngineScope, AllowJwt = false, Message = ProviderOnly)]
    public async Task<IActionResult> ReportSchedule([FromBody] EngineScheduleReport report,
        [FromServices] EngineClientScheduleService schedules, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        var result = await schedules.ReportAsync(GetUserId(), report, ct);
        return result.Error is { } error
            ? BadRequest(new { message = error })
            : Ok(new { stored = result.Stored, cleared = result.Cleared });
    }

    [HttpPut("{id}")]
    [RequireTokenScope(ApiTokenService.EngineScope, AllowJwt = false, Message = ProviderOnly)]
    public async Task<IActionResult> Update(string id, [FromBody] ExternalEngineRegistrationRequest request, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        return ToResult(await _registrations.SaveAsync(GetUserId(), id, request, ct));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        return await _registrations.DeleteAsync(GetUserId(), id, ct)
            ? NoContent()
            : NotFound(new { message = "Engine not found" });
    }

    /// <summary>
    /// Long-Poll des Providers (lila-engine <c>acquire</c>): wartet bis zu
    /// <see cref="LocalBrokerOptions.AcquireWait"/> (10 s) auf einen Auftrag für den Selector des Secrets →
    /// <c>200 { id, work, engine }</c>, sonst <c>204</c>. Literal-Route VOR <c>{id}</c>. Ein unbekannter Selector wird
    /// ebenso lange gehalten, aber je Adresse gedeckelt (<see cref="UnknownSelectorThrottle"/>, darüber sofort 429).
    /// </summary>
    [HttpPost("work")]
    [AllowAnonymous]
    [DisableRateLimiting]
    [RequestSizeLimit(MaxAcquireBodyBytes)]
    public async Task<IActionResult> Acquire([FromBody] EngineAcquireRequest? request, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        var secret = request?.ProviderSecret;
        if (string.IsNullOrEmpty(secret) || secret.Length > ExternalEngineRegistrationService.MaxProviderSecretLength)
            return BadRequest(new { message = "providerSecret is required" });

        var selector = ProviderSecrets.Selector(secret);
        try
        {
            if (!await _directory.IsKnownAsync(selector, ct))
            {
                if (!_unknownSelectors.TryEnter(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
                    return StatusCode(StatusCodes.Status429TooManyRequests);
                await Task.Delay(_options.AcquireWait, ct);
                return NoContent();
            }
            _directory.MarkSeen(selector);
            var job = await _hub.AcquireAsync(selector, _options.AcquireWait, ct);
            _directory.MarkSeen(selector);
            if (job is null) return NoContent();
            return Ok(new EngineAcquireResponse(job.Id!, job.Work.ToJson(), job.EngineJson));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Der Provider hat aufgelegt (Neustart, Netz) — kein Fehler, niemand liest die Antwort.
            return new EmptyResult();
        }
    }

    /// <summary>
    /// Upload der Suche (lila-engine <c>submit</c>): chunked, eine UCI-Zeile je <c>\n</c>, bis <c>bestmove</c>.
    /// Antwort <c>200</c> am Ende — auch ohne <c>bestmove</c> und SOFORT, wenn der Anfragende weg ist (der
    /// Provider stoppt dann die Engine); <c>404</c> für eine unbekannte oder schon eingelöste Kennung.
    ///
    /// <para><b>Kestrel-Fallen:</b> kein Größen-Deckel (der Upload läuft, solange die Engine rechnet) und KEINE
    /// Mindest-Datenrate — Kestrels Vorgabe (240 Byte/s nach 5 s Gnade) kappte einen Upload, der bei tiefer
    /// MultiPV-Suche minutenlang nur alle 15 s 19 Byte Lebenszeichen schickt. Der Rumpf wird zeilenweise
    /// gelesen, nie gepuffert (kein <c>EnableBuffering</c>).</para>
    /// </summary>
    [HttpPost("work/{id}")]
    [AllowAnonymous]
    [DisableRateLimiting]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Submit(string id)
    {
        if (!_options.Enabled) return NotFound();
        // Unter TestServer gibt es das Feature nicht — unter Kestrel immer.
        if (HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>() is { } minRate) minRate.MinDataRate = null;

        using var _ = LogContext.PushProperty("LogTags", LocalEngineBroker.LogTags);
        var job = _hub.TakeOngoing(id);
        if (job is null) return NotFound(new { message = "work not found or cancelled or expired" });
        if (!job.IsValid)
        {
            _hub.Stats.Note(job.EngineId, c => c.RequesterGone++);
            return Ok();
        }

        var started = DateTime.UtcNow;
        var result = await EngineUploadPump.RunAsync(job, Request.Body, HttpContext.RequestAborted, _logger);
        _hub.Stats.Note(job.EngineId, c =>
        {
            switch (result.Outcome)
            {
                case UploadOutcome.Completed: c.Completed++; break;
                case UploadOutcome.WithoutBestmove: c.WithoutBestmove++; break;
                case UploadOutcome.RequesterGone: c.RequesterGone++; break;
                case UploadOutcome.ProviderGone: c.ProviderGone++; break;
            }
        });
        _logger.LogInformation(
            "EngineBroker: Upload {Outcome} engine={EngineId} job={JobId} nach {Seconds:F1} s — {Emits} Zeilen, {Keepalives} Lebenszeichen{Error}",
            result.Outcome, job.EngineId, job.Id, (DateTime.UtcNow - started).TotalSeconds, result.Emits, result.Keepalives,
            result.Error is null ? "" : " — " + result.Error);
        return Ok();
    }

    private IActionResult ToResult(EngineRegistrationResult r) =>
        r.Engine is { } engine
            ? Ok(ExternalEngineRegistrationService.ToDto(engine, Username, withSecret: true))
            : StatusCode(r.Status, new { message = r.Error });
}
