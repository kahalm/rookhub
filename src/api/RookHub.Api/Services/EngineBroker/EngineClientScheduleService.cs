using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Nimmt die Zeitplan-Meldungen der Engine-Clients an (0.679.0) und legt sie je Engine-Name ab
/// (<see cref="EngineClientSchedule"/>). Ausgewertet werden sie in <see cref="EngineAvailability"/>.
/// </summary>
public class EngineClientScheduleService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EngineClientScheduleService> _logger;

    public EngineClientScheduleService(AppDbContext db, ILogger<EngineClientScheduleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Ergebnis: <c>Error</c> gesetzt = 400 mit diesem Satz; sonst wie viele Engines jetzt einen Zeitplan
    /// tragen (<c>Stored</c>) bzw. ihn verloren haben (<c>Cleared</c>).</summary>
    public sealed record Result(string? Error, int Stored, int Cleared);

    public async Task<Result> ReportAsync(int userId, EngineScheduleReport? report, CancellationToken ct = default)
    {
        var engines = report?.Engines ?? [];
        if (engines.Count is 0 or > ExternalEngineRegistrationService.MaxEnginesPerUser)
            return new($"1 bis {ExternalEngineRegistrationService.MaxEnginesPerUser} Engines melden.", 0, 0);
        var names = new List<string>(engines.Count);
        foreach (var e in engines)
        {
            var name = e.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > ExternalEngineRegistrationService.MaxNameLength)
                return new("Jede Engine braucht einen Namen (höchstens 200 Zeichen).", 0, 0);
            if (e.Slot < 1 || e.Slot > engines.Count)
                return new($"Platz {e.Slot} liegt nicht zwischen 1 und {engines.Count}.", 0, 0);
            names.Add(name);
        }
        if (engines.Select(e => e.Slot).Distinct().Count() != engines.Count)
            return new("Jeder Platz darf nur einmal vorkommen.", 0, 0);
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return new("Jeder Engine-Name darf nur einmal vorkommen.", 0, 0);

        var existing = await _db.EngineClientSchedules.Where(s => s.UserId == userId && names.Contains(s.EngineName)).ToListAsync(ct);

        // Kein Zeitplan: die Meldung dieser Engines faellt weg, und RookHub rechnet wieder nach seinen Sperrzeiten.
        if (string.IsNullOrWhiteSpace(report!.Rule))
        {
            _db.EngineClientSchedules.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);
            if (existing.Count > 0)
                _logger.LogInformation("Engine-Zeitplan: {Count} Engine(s) ohne Zeitplan gemeldet, Meldung entfernt (User {UserId})",
                    existing.Count, userId);
            return new(null, 0, existing.Count);
        }

        try { EngineScheduleRules.Parse(report.Rule); }
        catch (FormatException ex) { return new(ex.Message, 0, 0); }

        var scope = string.IsNullOrWhiteSpace(report.Scope) ? EngineScheduleRules.ScopeBackground : report.Scope.Trim().ToLowerInvariant();
        if (!EngineScheduleRules.IsValidScope(scope))
            return new($"Scope „{report.Scope}\" gibt es nicht — „background\" oder „all\".", 0, 0);

        var zone = string.IsNullOrWhiteSpace(report.TimeZone) ? QuietHours.DefaultTimeZone : report.TimeZone.Trim();
        if (zone.Length > 64 || EngineScheduleRules.ResolveZone(zone) is null)
            return new($"Zeitzone „{report.TimeZone}\" ist unbekannt (z. B. „Europe/Vienna\").", 0, 0);

        var now = DateTime.UtcNow;
        var byName = existing.ToDictionary(s => s.EngineName, StringComparer.OrdinalIgnoreCase);
        foreach (var e in engines)
        {
            var name = e.Name!.Trim();
            if (!byName.TryGetValue(name, out var row))
            {
                row = new EngineClientSchedule { UserId = userId, EngineName = name };
                _db.EngineClientSchedules.Add(row);
            }
            row.Slot = e.Slot;
            row.Total = engines.Count;
            row.Scope = scope;
            row.Rule = report.Rule.Trim();
            row.TimeZone = zone;
            row.ReportedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Engine-Zeitplan gemeldet: {Count} Engine(s), Scope {Scope}, Zone {Zone}, Regel {Rule} (User {UserId})",
            engines.Count, scope, zone, report.Rule.Trim(), userId);
        return new(null, engines.Count, 0);
    }
}
