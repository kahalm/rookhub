using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record DirectorySearchQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public int? RadiusKm { get; init; }
    public string? Federation { get; init; }
    public TournamentSpeed? Speed { get; init; }
    public string? Text { get; init; }
    public bool WeekendOnly { get; init; }
    public int? MinPlayers { get; init; }
    public bool IncludeCancelled { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Ein Listeneintrag. <paramref name="Members"/> sind die weiteren Gruppen desselben Turniers
/// (A/B/C) — der Haupteintrag steht in <paramref name="Entry"/>, die Liste enthaelt ALLE Gruppen
/// inklusive ihm, damit die Anzeige sie beschriften und verlinken kann.
/// </summary>
public sealed record DirectoryGroupItem(
    TournamentDirectoryEntry Entry,
    double? DistanceKm,
    IReadOnlyList<TournamentDirectoryEntry> Members);

public sealed record DirectorySearchResult(
    List<DirectoryGroupItem> Items, int Total, bool Truncated);

/// <summary>
/// Lesende Abfragen aufs Turnierverzeichnis.
///
/// Die Umkreissuche laeuft zweistufig: die Datenbank liefert ueber den Lat/Lon-Index nur die
/// Bounding-Box, die exakte Distanz und die Sortierung entstehen danach im Speicher. Grund ist
/// nicht Bequemlichkeit - <c>Math.Acos</c>/<c>Cos</c>/<c>Sin</c> uebersetzt der MySQL-Provider nicht
/// verlaesslich, und die Unit-Tests laufen gegen EF InMemory, wo so etwas nie auffaellt.
/// </summary>
public class TournamentDirectoryQueryService
{
    /// <summary>
    /// Obergrenze der Bounding-Box-Treffer, die in den Speicher geholt werden. Wird sie erreicht,
    /// meldet das Ergebnis <c>Truncated</c> - die Anzeige kann dann zum Verkleinern des Radius raten,
    /// statt eine stillschweigend unvollstaendige Liste zu zeigen.
    /// </summary>
    internal int MaxMaterialized { get; set; } = 5000;

    private readonly AppDbContext _db;

    public TournamentDirectoryQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DirectorySearchResult> SearchAsync(DirectorySearchQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var filtered = ApplyFilters(_db.TournamentDirectoryEntries.AsNoTracking(), query);

        var radius = query.RadiusKm;
        if (query.Lat is not { } lat || query.Lon is not { } lon || radius is not > 0)
        {
            // Gruppieren IN SQL, damit die Seitennavigation ueber Turniere zaehlt und nicht ueber
            // Gruppen: ein viergruppiges Open darf eine Seite nicht zu einem Viertel fuellen.
            // Zeilen aus der Zeit vor der Gruppierung haben noch keinen Schluessel. Sie duerfen
            // NICHT alle in einen Topf fallen (NULL == NULL waere genau das) — bis der naechste
            // Sweep sie nachtraegt, steht jede fuer sich.
            var groups = filtered
                .GroupBy(e => e.GroupKey ?? "id:" + e.Id)
                .Select(g => new { Key = g.Key, Start = g.Min(x => x.StartDate), PrimaryId = g.Min(x => x.Id) });

            var total = await groups.CountAsync(ct);
            var keys = await groups
                .OrderBy(g => g.Start).ThenBy(g => g.PrimaryId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            var items = await LoadGroupsAsync(filtered, keys.Select(k => k.Key).ToList(),
                keys.Select(k => k.PrimaryId).ToList(), null, ct);
            return new DirectorySearchResult(items, total, false);
        }

        var box = GeoDistance.BoundingBox(lat, lon, radius.Value);
        var candidates = await filtered
            .Where(e => e.Lat != null && e.Lon != null
                        && e.Lat >= box.MinLat && e.Lat <= box.MaxLat
                        && e.Lon >= box.MinLon && e.Lon <= box.MaxLon)
            .Take(MaxMaterialized + 1)
            .ToListAsync(ct);

        var truncated = candidates.Count > MaxMaterialized;
        if (truncated) candidates.RemoveAt(candidates.Count - 1);

        var withDistance = candidates
            .Select(e => (Entry: e, Distance: GeoDistance.Haversine(lat, lon, e.Lat!.Value, e.Lon!.Value)))
            .Where(x => x.Distance <= radius.Value)
            .ToList();

        // Im Umkreis liegen die Zeilen ohnehin im Speicher — hier ist Gruppieren in C# billiger
        // als eine zweite Abfrage, und die Distanz haengt schon an jeder Zeile.
        var grouped = withDistance
            .GroupBy(x => x.Entry.GroupKey ?? $"id:{x.Entry.Id}")
            .Select(g =>
            {
                var members = g.OrderBy(x => x.Entry.ChessResultsId, StringComparer.Ordinal)
                    .Select(x => x.Entry).ToList();
                return new DirectoryGroupItem(members[0], Math.Round(g.Min(x => x.Distance), 1), members);
            })
            .OrderBy(x => x.Entry.StartDate).ThenBy(x => x.DistanceKm)
            .ToList();

        return new DirectorySearchResult(
            grouped.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            grouped.Count,
            truncated);
    }

    /// <summary>
    /// Laedt zu den ausgewaehlten Gruppen-Schluesseln ALLE Mitglieder in EINER Abfrage (nicht je
    /// Gruppe eine) und ordnet sie den Haupteintraegen zu.
    /// </summary>
    private async Task<List<DirectoryGroupItem>> LoadGroupsAsync(
        IQueryable<TournamentDirectoryEntry> filtered, List<string> keys, List<int> primaryIds,
        Func<TournamentDirectoryEntry, double?>? distance, CancellationToken ct)
    {
        if (keys.Count == 0) return [];

        var rows = await filtered
            .Where(e => keys.Contains(e.GroupKey ?? "id:" + e.Id))
            .ToListAsync(ct);
        var byKey = rows.GroupBy(e => e.GroupKey ?? "id:" + e.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ChessResultsId, StringComparer.Ordinal).ToList());

        var items = new List<DirectoryGroupItem>(primaryIds.Count);
        foreach (var (key, primaryId) in keys.Zip(primaryIds))
        {
            if (!byKey.TryGetValue(key, out var members) || members.Count == 0) continue;
            var primary = members.FirstOrDefault(m => m.Id == primaryId) ?? members[0];
            items.Add(new DirectoryGroupItem(primary, distance?.Invoke(primary), members));
        }
        return items;
    }

    /// <summary>
    /// Kartenmarker: nur die Zeilen mit Koordinaten in der sichtbaren Box, hart gedeckelt. Ohne
    /// Deckel wuerde ein herausgezoomtes Europa zehntausende Pins in eine Antwort packen.
    /// </summary>
    public async Task<List<TournamentDirectoryEntry>> MapPinsAsync(
        DirectorySearchQuery query, double minLat, double maxLat, double minLon, double maxLon,
        int limit = 2000, CancellationToken ct = default)
    {
        return await ApplyFilters(_db.TournamentDirectoryEntries.AsNoTracking(), query)
            .Where(e => e.Lat != null && e.Lon != null
                        && e.Lat >= minLat && e.Lat <= maxLat
                        && e.Lon >= minLon && e.Lon <= maxLon)
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToListAsync(ct);
    }

    public async Task<TournamentDirectoryEntry?> GetAsync(string chessResultsId, CancellationToken ct = default) =>
        await _db.TournamentDirectoryEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ChessResultsId == chessResultsId, ct);

    /// <summary>Ortsvorschlaege fuers Suchprofil-Formular: Postleitzahl oder Ortsname.</summary>
    public async Task<List<GeoPlace>> SuggestPlacesAsync(string term, int limit = 10, CancellationToken ct = default)
    {
        term = term.Trim();
        if (term.Length < 2) return [];

        // Reine Ziffern koennen nur eine PLZ sein, alles andere ein Ortsname - das haelt die
        // Abfrage auf einem der beiden Indizes statt auf einem OR ueber beide.
        if (term.All(char.IsAsciiDigit))
        {
            return await _db.GeoPlaces.AsNoTracking()
                .Where(g => g.PostalCode != null && g.PostalCode.StartsWith(term))
                .OrderBy(g => g.PostalCode).ThenBy(g => g.Name)
                .Take(limit)
                .ToListAsync(ct);
        }

        var normalized = GeoTextNormalizer.Normalize(term);
        return await _db.GeoPlaces.AsNoTracking()
            .Where(g => g.NameNormalized.StartsWith(normalized))
            // Exakte Treffer zuerst, danach die groessten Orte - "Wien" soll nicht hinter
            // "Wiener Neudorf" landen.
            .OrderByDescending(g => g.NameNormalized == normalized)
            .ThenByDescending(g => g.Population)
            .ThenBy(g => g.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    private static IQueryable<TournamentDirectoryEntry> ApplyFilters(
        IQueryable<TournamentDirectoryEntry> source, DirectorySearchQuery query)
    {
        if (!query.IncludeCancelled)
            source = source.Where(e => e.RemovedAt == null);

        // Ueberlappung statt Enthaltensein: ein zehntaegiges Open, das in den Zeitraum
        // hineinragt, gehoert in den Kalender - auch wenn es davor begann.
        if (query.From is { } from)
            source = source.Where(e => (e.EndDate ?? e.StartDate) == null || (e.EndDate ?? e.StartDate) >= from);
        if (query.To is { } to)
            source = source.Where(e => (e.StartDate ?? e.EndDate) == null || (e.StartDate ?? e.EndDate) <= to);

        if (!string.IsNullOrWhiteSpace(query.Federation))
        {
            var fed = query.Federation.Trim().ToUpperInvariant();
            source = source.Where(e => e.Federation == fed);
        }

        if (query.Speed is { } speed)
            source = source.Where(e => e.Speed == speed);

        if (query.MinPlayers is { } min)
            source = source.Where(e => e.PlayerCount >= min);

        if (query.WeekendOnly)
            source = source.Where(e => e.StartsOnWeekend);

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            source = source.Where(e => e.Name.Contains(text)
                                       || (e.LocationText != null && e.LocationText.Contains(text))
                                       || (e.Organizer != null && e.Organizer.Contains(text)));
        }

        return source;
    }
}
