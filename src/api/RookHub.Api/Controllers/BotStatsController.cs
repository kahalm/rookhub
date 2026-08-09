using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Bot-only: Trainings-/Puzzle-Fortschritt eines verknüpften Spielers für den Motivations-DM des
/// Schach-Bots. Kein User-Login — authentifiziert über ein geteiltes Secret
/// (<c>SchachBot:StatsSecret</c>, identisch zum Bot-<c>ROOKHUB_STATS_SECRET</c>) per HMAC-Signatur
/// über die Discord-ID. Gleiches Vertrauensmuster wie der Solver-Webhook
/// (<see cref="SchachBotWebhookService"/>), nur in der eingehenden Richtung. Secret leer → deaktiviert.
/// </summary>
[ApiController]
[Route("api/bot")]
[AllowAnonymous]
public class BotStatsController : ControllerBase
{
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
    /// 401 bei fehlender/falscher Signatur, 404 bei nicht-verknüpfter Discord-ID oder deaktiviertem Feature.
    /// </summary>
    [HttpGet("player-progress/{discordId}")]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<BotPlayerProgressDto>> GetPlayerProgress(string discordId)
    {
        var secret = _config["SchachBot:StatsSecret"];
        if (string.IsNullOrEmpty(secret))
            return NotFound();  // Feature nicht konfiguriert → wie nicht vorhanden behandeln

        var provided = Request.Headers["X-Bot-Signature"].FirstOrDefault();
        var timestamp = Request.Headers["X-Bot-Timestamp"].FirstOrDefault();
        if (!VerifySignature(secret, discordId, provided, timestamp))
        {
            _logger.LogWarning("Bot-Stats: ungültige Signatur für Discord-ID {DiscordId}", discordId);
            return Unauthorized();
        }

        var progress = await _service.GetProgressByDiscordIdAsync(discordId);
        if (progress == null)
            return NotFound(new { message = "No RookHub account linked to this Discord ID." });

        return Ok(progress);
    }

    /// <summary>±300 s Toleranz für den Replay-Schutz (analog zum Solver-Webhook).</summary>
    private const int TimestampToleranceSeconds = 300;

    /// <summary>
    /// Prüft <c>X-Bot-Signature: sha256=&lt;hmac_hex&gt;</c> konstant-zeitig. Replay-Schutz ist PFLICHT:
    /// <paramref name="timestamp"/> (Wert des <c>X-Bot-Timestamp</c>-Headers, Unix-Sekunden) MUSS gesetzt
    /// sein, innerhalb ±<see cref="TimestampToleranceSeconds"/> liegen und geht in die HMAC über
    /// <c>"&lt;ts&gt;.&lt;discordId&gt;"</c> ein. Der frühere rückwärtskompatible Zweig (HMAC nur über
    /// die Discord-ID, unbegrenzt replaybar) ist entfernt — der Bot sendet den Timestamp seit v2.70
    /// durchgängig. Nutzt dieselbe HMAC-Hex-Berechnung wie der ausgehende Solver-Webhook.
    /// </summary>
    private static bool VerifySignature(string secret, string discordId, string? provided, string? timestamp)
    {
        if (string.IsNullOrEmpty(provided))
            return false;
        // Ohne Timestamp keine Prüfung mehr — eine alte body-only-Signatur wäre für immer replaybar.
        if (string.IsNullOrWhiteSpace(timestamp))
            return false;
        var sig = provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? provided["sha256=".Length..]
            : provided;

        if (!long.TryParse(timestamp, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ts))
            return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > TimestampToleranceSeconds)
            return false;
        var signedMessage = ts.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + discordId;

        var expected = SchachBotWebhookService.ComputeHmacHex(secret, signedMessage);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected));
    }
}
