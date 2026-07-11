using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using Serilog.Context;

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
        int? userId = GetUserIdOrNull();

        // Routine-Heartbeats auf Information; nur echte Diagnose-Events (Crash/Hänger) auf Warning,
        // sonst lösen die häufigen Heartbeats einen warn_spike im log-watcher aus (Fehlalarm).
        var level = kind.StartsWith("heartbeat", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Information
            : LogLevel.Warning;

        // Domänen-Tags für den zentralen ECS-`tags`-Filter in Kibana: jedes ClientLog trägt `clientlog`;
        // Engine-Crashes/Hänger (Stockfish-WASM) zusätzlich `engine`, damit man sie isoliert filtern kann.
        var logTags = IsEngineEvent(kind, detail) ? "clientlog,engine" : "clientlog";
        using (LogContext.PushProperty("LogTags", logTags))
        {
            _logger.Log(level,
                "ClientLog {ClientLogKind}: {ClientLogDetail} (url={ClientLogUrl} user={ClientLogUserId} ua={ClientLogUserAgent})",
                kind, detail, url, userId, userAgent);
        }

        return NoContent();
    }

    /// <summary>
    /// Engine-Crash/Hänger-Heuristik fürs `engine`-Tag: kind beginnt mit "engine" ODER kind/detail
    /// enthält "stockfish"/"unreachable"/"hang"/"crash" (case-insensitive).
    /// </summary>
    internal static bool IsEngineEvent(string kind, string detail)
    {
        if (kind.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
            return true;
        string[] markers = { "stockfish", "unreachable", "hang", "crash" };
        foreach (var m in markers)
        {
            if (kind.Contains(m, StringComparison.OrdinalIgnoreCase)
                || detail.Contains(m, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        // Zeilenumbrüche entfernen → kein Log-Forging (gefälschte Zusatzzeilen) in der Console-Ausgabe.
        var clean = s.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= max ? clean : clean.Substring(0, max);
    }
}
