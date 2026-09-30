using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Bot-only: Trainings-/Puzzle-Fortschritt eines verknüpften Spielers für den Motivations-DM des
/// Schach-Bots. Kein User-Login — authentifiziert über ein geteiltes Secret
/// (<c>SchachBot:StatsSecret</c>, identisch zum Bot-<c>ROOKHUB_STATS_SECRET</c>) per HMAC-Signatur
/// über die Discord-ID. Gleiches Vertrauensmuster wie der Solver-Webhook
/// (<see cref="SchachBotWebhookService"/>), nur in der eingehenden Richtung. Secret leer → deaktiviert (503).
/// </summary>
[ApiController]
[Route("api/bot")]
[AllowAnonymous]
public class BotStatsController : ControllerBase
{
    /// <summary>Maschinenlesbarer Grund im 503-Body: Feature serverseitig nicht konfiguriert.</summary>
    public const string NotConfiguredReason = "not-configured";
    /// <summary>Maschinenlesbarer Grund im 404-Body: kein RookHub-Konto mit dieser Discord-ID verknüpft.</summary>
    public const string NotLinkedReason = "not-linked";

    private readonly BotStatsService _service;
    private readonly IConfiguration _config;
    private readonly ILogger<BotStatsController> _logger;

    public BotStatsController(BotStatsService service, IConfiguration config, ILogger<BotStatsController> logger)
    {
        _service = service;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Fortschritt eines über die Discord-ID verknüpften Spielers.
    /// 401 bei fehlender/falscher Signatur, 404 <c>{ reason: "not-linked" }</c> bei nicht-verknüpfter Discord-ID,
    /// 503 <c>{ reason: "not-configured" }</c> bei deaktiviertem Feature (Secret leer oder Platzhalter).
    /// Zwei Statuscodes, weil es zwei verschiedene Aussagen sind: „dieser Nutzer ist nicht verknüpft" gegen
    /// „der Server kann gerade niemanden beurteilen". Vorher war beides 404 — ein leeres Secret ließ den Bot
    /// JEDEN verknüpften Abonnenten als unverknüpft behandeln (Registrier-DM statt Motivation, am Ende Abmeldung).
    /// Der Bot wertet 503 schon heute als „Fortschritt nicht verfügbar" (nichts senden, später erneut).
    /// </summary>
    [HttpGet("player-progress/{discordId}")]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<BotPlayerProgressDto>> GetPlayerProgress(string discordId)
    {
        // Platzhalter aus den Beispiel-Dateien zählt wie „leer" (SecretConfigCheck): sonst liest jeder,
        // der das öffentliche Repo kennt, den Trainingsstand verknüpfter Spieler.
        var secret = SecretConfigCheck.Usable(_config["SchachBot:StatsSecret"]);
        if (secret is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = NotConfiguredReason });

        var provided = Request.Headers["X-Bot-Signature"].FirstOrDefault();
        var timestamp = Request.Headers["X-Bot-Timestamp"].FirstOrDefault();
        if (!VerifySignature(secret, discordId, provided, timestamp))
        {
            _logger.LogWarning("Bot-Stats: ungültige Signatur für Discord-ID {DiscordId}", discordId);
            return Unauthorized();
        }

        var progress = await _service.GetProgressByDiscordIdAsync(discordId);
        if (progress == null)
            return NotFound(new { reason = NotLinkedReason, message = "No RookHub account linked to this Discord ID." });

        return Ok(progress);
    }

    /// <summary>
    /// Prüft <c>X-Bot-Signature: sha256=&lt;hmac_hex&gt;</c> über <c>"&lt;ts&gt;.&lt;discordId&gt;"</c>;
    /// Timestamp (<c>X-Bot-Timestamp</c>) ist PFLICHT und ±300 s. Der frühere rückwärtskompatible Zweig
    /// (HMAC nur über die Discord-ID, unbegrenzt replaybar) ist entfernt — der Bot sendet den Timestamp
    /// seit v2.70 durchgängig. Eine Implementierung mit den signierten Ergebnis-GETs:
    /// <see cref="BotRequestSignature.Verify"/>.
    /// </summary>
    private static bool VerifySignature(string secret, string discordId, string? provided, string? timestamp)
        => BotRequestSignature.Verify(secret, discordId, provided, timestamp);
}
