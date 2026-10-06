using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Welche Hintergrund-Engines dürfen JETZT Arbeit bekommen (0.679.0)? Wunsch 2026-10-06: „wenn der client
/// betriebszeiten meldet halte ich mich an die, wenn nicht nehm ich die voreingestellten von rookhub".
///
/// <list type="bullet">
/// <item><b>Mit Meldung</b> (<see cref="EngineClientSchedule"/>, Schlüssel = Engine-Name): genau die Engines, die der
/// Client nach seinem Zeitplan gerade laufen hat — für JEDE Art Arbeit, denn eine abgeschaltete Engine nimmt keine an.</item>
/// <item><b>Ohne Meldung</b>: wie bisher. Normale Aufträge immer; der Stapel (Meister-, Vereins-, Ligapartien) nur
/// außerhalb der Sperrzeiten von RookHub (<see cref="QuietHours"/>).</item>
/// </list>
///
/// <para>Lichess-Engines (<c>eei_…</c>) haben keine Registrierung bei uns und damit nie eine Meldung — für sie gilt immer
/// der zweite Fall.</para>
/// </summary>
public static class EngineAvailability
{
    /// <summary>Die Teilmenge von <paramref name="engines"/>, die jetzt Arbeit bekommen darf — Reihenfolge bleibt.</summary>
    public static async Task<List<string>> UsableAsync(AppDbContext db, int ownerId, IReadOnlyList<string> engines,
        bool batch, QuietHours? quiet, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (engines.Count == 0) return [];
        var schedules = await SchedulesByEngineIdAsync(db, ownerId, engines, ct);
        var quietNow = quiet?.IsQuiet(nowUtc) == true;

        var usable = new List<string>(engines.Count);
        foreach (var id in engines)
        {
            if (schedules.TryGetValue(id, out var s))
            {
                if (IsOpen(s, nowUtc)) usable.Add(id);
            }
            else if (!batch || !quietNow)
            {
                usable.Add(id);
            }
        }
        return usable;
    }

    /// <summary>Läuft die Engine nach ihrem gemeldeten Zeitplan gerade? Eine Meldung, die sich (inzwischen) nicht mehr
    /// lesen lässt, zählt wie keine Einschränkung — lieber einmal einen 503 als eine Engine, die nie wieder Arbeit
    /// bekommt. Gültige Meldungen prüft der Endpunkt beim Annehmen.</summary>
    public static bool IsOpen(EngineClientSchedule s, DateTimeOffset nowUtc)
    {
        try
        {
            var rules = EngineScheduleRules.Parse(s.Rule);
            var zone = EngineScheduleRules.ResolveZone(s.TimeZone)
                       ?? TimeZoneInfo.FindSystemTimeZoneById(QuietHours.DefaultTimeZone);
            var local = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
            return EngineScheduleRules.IsOpen(s.Slot, s.Total, s.Scope, rules, local);
        }
        catch (FormatException)
        {
            return true;
        }
    }

    /// <summary>Gemeldete Zeitpläne, nach Engine-KENNUNG: die Meldung kommt mit dem Namen, die Hintergrund-Liste trägt
    /// Kennungen — die Registrierung verbindet beides.</summary>
    private static async Task<Dictionary<string, EngineClientSchedule>> SchedulesByEngineIdAsync(AppDbContext db,
        int ownerId, IReadOnlyList<string> engines, CancellationToken ct)
    {
        var local = engines.Where(e => e.StartsWith(ExternalEngineRegistration.IdPrefix, StringComparison.Ordinal)).ToList();
        if (local.Count == 0) return new();
        var names = await db.ExternalEngineRegistrations.AsNoTracking()
            .Where(r => r.UserId == ownerId && local.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(ct);
        if (names.Count == 0) return new();
        var nameList = names.Select(n => n.Name).ToList();
        var rows = await db.EngineClientSchedules.AsNoTracking()
            .Where(s => s.UserId == ownerId && nameList.Contains(s.EngineName))
            .ToListAsync(ct);
        var byName = rows.ToDictionary(r => r.EngineName, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, EngineClientSchedule>(StringComparer.Ordinal);
        foreach (var n in names)
            if (byName.TryGetValue(n.Name, out var s)) result[n.Id] = s;
        return result;
    }
}
