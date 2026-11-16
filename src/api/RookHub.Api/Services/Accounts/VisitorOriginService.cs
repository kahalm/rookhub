using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Schreibt die gesammelten Zähler der Besucher-Herkunft weg (Upsert je Tag/Besucher/Oberfläche) und liefert die
/// Auswertung „eindeutige Benutzer je Land“. Beschreibung: <c>docs/api/besucher-herkunft.md</c>.
/// </summary>
public class VisitorOriginService
{
    /// <summary>Zeiträume, die der Admin wählen kann (Tage); ohne Angabe = alles Erfasste.</summary>
    public static readonly IReadOnlyList<int> AllowedDays = [7, 30, 90, 365];

    private const int LookupChunk = 500;

    private readonly AppDbContext _db;

    public VisitorOriginService(AppDbContext db) => _db = db;

    /// <summary>
    /// Führt die Zähler mit dem Bestand zusammen: vorhandene Zeile → Aufrufe addieren, fehlendes Land nachtragen;
    /// sonst neue Zeile. Mehrere Zähler desselben Schlüssels in einem Schub werden vorher zusammengelegt.
    /// Der Sammler läuft in EINER API-Instanz; der eindeutige Index fängt den Rest (dann scheitert der Schub und
    /// wird im nächsten Takt wiederholt).
    /// </summary>
    public async Task<int> FlushAsync(IReadOnlyCollection<VisitorDayDelta> deltas, CancellationToken ct = default)
    {
        if (deltas.Count == 0) return 0;
        var merged = deltas
            .GroupBy(d => (d.Day, d.VisitorHash, d.Surface))
            .Select(g => new VisitorDayDelta(g.Key.Day, g.Key.VisitorHash, g.Key.Surface,
                g.Any(x => x.Country == VisitorNetworkClassifier.VpnCountry) ? VisitorNetworkClassifier.VpnCountry
                    : g.Select(x => x.Country).FirstOrDefault(c => c.Length > 0) ?? string.Empty, g.Sum(x => x.Hits)))
            .ToList();

        foreach (var dayGroup in merged.GroupBy(d => d.Day))
        {
            var day = dayGroup.Key;
            foreach (var chunk in dayGroup.Chunk(LookupChunk))
            {
                var hashes = chunk.Select(d => d.VisitorHash).Distinct().ToList();
                var existing = (await _db.VisitorDays.Where(v => v.Day == day && hashes.Contains(v.VisitorHash)).ToListAsync(ct))
                    .ToDictionary(v => (v.VisitorHash, v.Surface));
                foreach (var d in chunk)
                {
                    if (existing.TryGetValue((d.VisitorHash, d.Surface), out var row))
                    {
                        row.Hits += d.Hits;
                        // Fehlendes Land nachtragen; „VPN“ steht wie im Sammler über einem Land.
                        if (row.Country.Length == 0 || d.Country == VisitorNetworkClassifier.VpnCountry) row.Country = d.Country;
                    }
                    else
                    {
                        _db.VisitorDays.Add(new VisitorDay
                        {
                            Day = d.Day, VisitorHash = d.VisitorHash, Surface = d.Surface, Country = d.Country, Hits = d.Hits,
                        });
                    }
                }
            }
        }
        await _db.SaveChangesAsync(ct);
        return merged.Count;
    }

    internal sealed record CountryRow(string Country, int Users, long Hits);

    /// <summary>Zeilen des Zeitraums (die letzten <paramref name="days"/> Tage einschließlich heute) und der Oberfläche.</summary>
    internal IQueryable<VisitorDay> Filtered(int? days, string? surface, DateOnly today)
    {
        var q = _db.VisitorDays.AsNoTracking();
        if (days is int n)
        {
            var from = today.AddDays(-(n - 1));
            q = q.Where(v => v.Day >= from);
        }
        if (surface is not null) q = q.Where(v => v.Surface == surface);
        return q;
    }

    /// <summary>Je Land: eindeutige Pseudonyme (COUNT DISTINCT) und Summe der Aufrufe.</summary>
    internal static IQueryable<CountryRow> ByCountry(IQueryable<VisitorDay> q) =>
        q.GroupBy(v => v.Country).Select(g => new CountryRow(
            g.Key, g.Select(v => v.VisitorHash).Distinct().Count(), g.Sum(v => (long)v.Hits)));

    /// <summary>
    /// Eindeutige Benutzer (Pseudonyme) gesamt und je Land. <paramref name="days"/> <c>null</c> = alles Erfasste,
    /// <paramref name="surface"/> <c>null</c> = alle Oberflächen. <paramref name="withHits"/> liefert zusätzlich die
    /// Aufrufe und den ersten erfassten Tag (nur für Admins — normale Konten bekommen nichts Zeitbezogenes). Wer im Zeitraum aus zwei Ländern kam, zählt in beiden — in der Gesamtzahl einmal.
    /// </summary>
    public async Task<VisitorOriginsDto> GetAsync(int? days, string? surface, bool withHits, DateOnly today, CancellationToken ct = default)
    {
        var q = Filtered(days, surface, today);
        var uniqueUsers = await q.Select(v => v.VisitorHash).Distinct().CountAsync(ct);
        var rows = await ByCountry(q).ToListAsync(ct);
        // „Zählung läuft seit“: der älteste Tag des GANZEN Bestands, unabhängig vom Filter.
        var since = await _db.VisitorDays.AsNoTracking().OrderBy(v => v.Day).Select(v => (DateOnly?)v.Day).FirstOrDefaultAsync(ct);

        var known = rows.Where(r => r.Country.Length > 0)
            .OrderByDescending(r => r.Users).ThenBy(r => r.Country, StringComparer.Ordinal)
            .Select(r => new VisitorCountryDto(r.Country, r.Users, withHits ? r.Hits : null))
            .ToList();
        return new VisitorOriginsDto(
            Admin: withHits,
            Days: days,
            Surface: surface,
            UniqueUsers: uniqueUsers,
            // „VPN“ (XV) steht in der Liste, ist aber kein Land.
            Countries: known.Count(c => c.Country != VisitorNetworkClassifier.VpnCountry),
            UnknownUsers: rows.Where(r => r.Country.Length == 0).Sum(r => r.Users),
            Hits: withHits ? rows.Sum(r => r.Hits) : null,
            Since: withHits ? since : null,
            ByCountry: known);
    }
}

/// <summary>Frist der Besucher-Herkunft: Tageszeilen älter als <c>Retention:VisitorDays</c> (Vorgabe 400) gehen im
/// täglichen Aufräumlauf (<see cref="Puzzles.AnonymousDataRetentionService"/>). Übersicht: <c>docs/api/speicherdauer.md</c>.</summary>
public static class VisitorOriginRetention
{
    public static Task<int> PruneAsync(AppDbContext db, DateTime nowUtc, RetentionOptions options,
        int chunkSize = RetentionDelete.ChunkSize, CancellationToken ct = default)
    {
        var cutoff = DateOnly.FromDateTime(nowUtc).AddDays(-options.VisitorDays);
        return RetentionDelete.AllAsync(db, db.VisitorDays.Where(v => v.Day < cutoff), chunkSize, ct);
    }
}
