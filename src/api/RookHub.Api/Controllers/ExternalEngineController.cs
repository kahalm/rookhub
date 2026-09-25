using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
/// im Takt (log-watcher) und keine Auskunft nach außen.</para>
/// </summary>
[ApiController]
[Route("api/external-engine")]
[Authorize]
public class ExternalEngineController : BaseApiController
{
    private readonly ExternalEngineRegistrationService _registrations;
    private readonly LocalBrokerOptions _options;
    private readonly ILogger<ExternalEngineController> _logger;
    private readonly EngineHub _hub;
    private readonly EngineSelectorDirectory _directory;

    public ExternalEngineController(ExternalEngineRegistrationService registrations, LocalBrokerOptions options,
        ILogger<ExternalEngineController> logger, EngineHub hub, EngineSelectorDirectory directory)
    {
        _registrations = registrations;
        _options = options;
        _logger = logger;
        _hub = hub;
        _directory = directory;
    }

    /// <summary>Scope des API-Tokens; <c>null</c> = Browser-Login (JWT).</summary>
    private string? TokenScope => User.FindFirst("scope")?.Value;

    private string Username => User.Identity?.Name ?? GetUserId().ToString();

    /// <summary>Ein API-Token eines ANDEREN Scopes hat hier nichts verloren (zweite Schranke neben dem
    /// zentralen Scope-Zaun).</summary>
    private IActionResult? ForeignScope() =>
        TokenScope is { } scope && scope != ApiTokenService.EngineScope
            ? StatusCode(403, new { message = "API token scope 'engine' required" })
            : null;

    /// <summary>Anlegen/Ändern nur mit Engine-Token: der Provider ist der Einzige, der registriert.</summary>
    private IActionResult? NotAProvider() =>
        TokenScope != ApiTokenService.EngineScope
            ? StatusCode(403, new { message = "Engines are registered by the provider (API token with scope 'engine')" })
            : null;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        if (ForeignScope() is { } forbid) return forbid;
        var withSecret = TokenScope == ApiTokenService.EngineScope;
        var list = await _registrations.ListAsync(GetUserId(), ct);
        return Ok(list.Select(r => ExternalEngineRegistrationService.ToDto(r, Username, withSecret)).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExternalEngineRegistrationRequest request, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        if (NotAProvider() is { } forbid) return forbid;
        return ToResult(await _registrations.SaveAsync(GetUserId(), null, request, ct));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] ExternalEngineRegistrationRequest request, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        if (NotAProvider() is { } forbid) return forbid;
        return ToResult(await _registrations.SaveAsync(GetUserId(), id, request, ct));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        if (ForeignScope() is { } forbid) return forbid;
        return await _registrations.DeleteAsync(GetUserId(), id, ct)
            ? NoContent()
            : NotFound(new { message = "Engine not found" });
    }

    /// <summary>
    /// Long-Poll des Providers (lila-engine <c>acquire</c>): wartet bis zu
    /// <see cref="LocalBrokerOptions.AcquireWait"/> (10 s) auf einen Auftrag für den Selector des Secrets →
    /// <c>200 { id, work, engine }</c>, sonst <c>204</c>. Literal-Route VOR <c>{id}</c>.
    /// </summary>
    [HttpPost("work")]
    [AllowAnonymous]
    [DisableRateLimiting]
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
            "EngineBroker: Upload {Outcome} engine={EngineId} job={JobId} nach {Seconds:F1} s — {Emits} Zeilen, {Keepalives} Lebenszeichen",
            result.Outcome, job.EngineId, job.Id, (DateTime.UtcNow - started).TotalSeconds, result.Emits, result.Keepalives);
        return Ok();
    }

    private IActionResult ToResult(EngineRegistrationResult r) =>
        r.Engine is { } engine
            ? Ok(ExternalEngineRegistrationService.ToDto(engine, Username, withSecret: true))
            : StatusCode(r.Status, new { message = r.Error });
}
