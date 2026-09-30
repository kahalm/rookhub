using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Signiertes Lebenszeichen des Schach-Bots. Der log-watcher erkennt einen toten Bot an AUSBLEIBENDEN
/// Heartbeats in <c>rookhub-logs-*</c> und zählt dafür <c>labels.HeartbeatService = schach-bot</c>.
/// Früher kam das Signal nur über das anonyme <c>POST /api/client-log</c> (<c>kind=heartbeat_bot</c>) — jeder,
/// der das alle paar Minuten schickte, hielt einen toten Bot für den Wächter „lebendig" (S5-008). Hier entsteht
/// die Heartbeat-Zeile NUR mit gültiger Bot-Signatur: <c>X-Bot-Timestamp</c> + <c>X-Bot-Signature</c> über
/// <c>"&lt;ts&gt;./api/bot/heartbeat"</c> mit <c>SchachBot:StatsSecret</c> (== Bot-<c>ROOKHUB_STATS_SECRET</c>),
/// ±300 s — dasselbe Vertrauensmuster wie <see cref="BotStatsController"/> (<see cref="BotRequestSignature.Verify"/>).
/// <c>heartbeat_bot</c> über <c>/api/client-log</c> bleibt nur der Altpfad (Bot ohne Secret; der Bot fällt nur bei
/// 404 darauf zurück, nie bei 401).
/// </summary>
[ApiController]
[Route("api/bot")]
[AllowAnonymous]
public class BotHeartbeatController : ControllerBase
{
    /// <summary>Wert von <c>HeartbeatService</c> in der Logzeile — der Name, den der log-watcher erwartet
    /// (<c>HEARTBEAT_CHECKS</c>: <c>schach-bot=rookhub-logs-*</c>).</summary>
    public const string ServiceName = "schach-bot";

    /// <summary>Das signierte Objekt: der Pfad, genau so, wie der Bot ihn signiert (schach-bot
    /// <c>puzzle/rookhub.py</c> <c>_HEARTBEAT_PATH</c>). Fest statt <c>Request.Path</c>, damit Groß-/Kleinschreibung
    /// oder ein abschließender Schrägstrich in der angefragten URL die Prüfung nicht verändern.</summary>
    public const string SignedPath = "/api/bot/heartbeat";

    private readonly IConfiguration _config;
    private readonly ILogger<BotHeartbeatController> _logger;

    public BotHeartbeatController(IConfiguration config, ILogger<BotHeartbeatController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 204 + Heartbeat-Zeile (<see cref="HeartbeatService.LogTemplatePrefix"/>, <c>HeartbeatService = schach-bot</c>)
    /// bei gültiger Signatur; 401 ohne, mit falscher oder abgelaufener Signatur — dann KEINE Heartbeat-Zeile;
    /// 503 <c>{ reason: "not-configured" }</c>, wenn <c>SchachBot:StatsSecret</c> leer oder ein Platzhalter ist
    /// (meldet der Start einmal, <see cref="SecretConfigCheck.LogStartupFindings"/>).
    /// </summary>
    [HttpPost("heartbeat")]
    [EnableRateLimiting("anonymous-puzzle")]
    public IActionResult Post()
    {
        var secret = SecretConfigCheck.Usable(_config[BotRequestSignature.SecretConfigKey]);
        if (secret is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = BotStatsController.NotConfiguredReason });

        var provided = Request.Headers[BotRequestSignature.SignatureHeader].FirstOrDefault();
        var timestamp = Request.Headers[BotRequestSignature.TimestampHeader].FirstOrDefault();
        // Abgelehnte Aufrufe loggen hier nichts (die Request-Zeile mit 401 steht ohnehin im Log): der Endpunkt ist
        // offen, eine Zeile je Aufruf gäbe jedem Anonymen die Log-Menge in die Hand — und sie darf nie wie ein
        // Heartbeat aussehen. Ein falsch konfigurierter Bot warnt selbst (einmal je Prozess).
        if (!BotRequestSignature.Verify(secret, SignedPath, provided, timestamp))
            return Unauthorized();

        _logger.LogInformation(HeartbeatService.LogTemplatePrefix, ServiceName, "healthy");
        return NoContent();
    }
}
