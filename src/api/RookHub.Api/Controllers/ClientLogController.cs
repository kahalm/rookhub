using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;

namespace RookHub.Api.Controllers;

/// <summary>
/// Nimmt client-seitige Diagnose-Events entgegen (v. a. Browser-Engine-Crashes/Hänger) und loggt
/// sie strukturiert mit dem Marker „ClientLog" → landet via Serilog in Elasticsearch/Kibana, damit
/// man sieht, wie oft die Stockfish-WASM-Engine bei echten Nutzern abstürzt/hängt.
/// Offen (anonym nutzbar) + rate-limitiert; loggt nur, schreibt nichts in die DB.
/// </summary>
[ApiController]
[Route("api/client-log")]
public class ClientLogController : BaseApiController
{
    private readonly ILogger<ClientLogController> _logger;

    public ClientLogController(ILogger<ClientLogController> logger) => _logger = logger;

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public IActionResult Post([FromBody] ClientLogDto dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Kind))
            return BadRequest(new { message = "kind is required." });

        var kind = Truncate(dto.Kind, 64);
        var detail = Truncate(dto.Detail, 500);
        var url = Truncate(dto.Url, 300);
        var userAgent = Truncate(Request.Headers.UserAgent.ToString(), 300);
        int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;

        // Routine-Heartbeats auf Information; nur echte Diagnose-Events (Crash/Hänger) auf Warning,
        // sonst lösen die häufigen Heartbeats einen warn_spike im log-watcher aus (Fehlalarm).
        var level = kind.StartsWith("heartbeat", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Information
            : LogLevel.Warning;
        _logger.Log(level,
            "ClientLog {ClientLogKind}: {ClientLogDetail} (url={ClientLogUrl} user={ClientLogUserId} ua={ClientLogUserAgent})",
            kind, detail, url, userId, userAgent);

        return NoContent();
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        // Zeilenumbrüche entfernen → kein Log-Forging (gefälschte Zusatzzeilen) in der Console-Ausgabe.
        var clean = s.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= max ? clean : clean.Substring(0, max);
    }
}
