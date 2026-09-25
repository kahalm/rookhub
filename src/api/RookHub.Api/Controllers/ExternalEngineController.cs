using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

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
/// </summary>
[ApiController]
[Route("api/external-engine")]
[Authorize]
public class ExternalEngineController : BaseApiController
{
    private readonly ExternalEngineRegistrationService _registrations;
    private readonly LocalBrokerOptions _options;
    private readonly ILogger<ExternalEngineController> _logger;

    public ExternalEngineController(ExternalEngineRegistrationService registrations, LocalBrokerOptions options,
        ILogger<ExternalEngineController> logger)
    {
        _registrations = registrations;
        _options = options;
        _logger = logger;
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

    private IActionResult ToResult(EngineRegistrationResult r) =>
        r.Engine is { } engine
            ? Ok(ExternalEngineRegistrationService.ToDto(engine, Username, withSecret: true))
            : StatusCode(r.Status, new { message = r.Error });
}
