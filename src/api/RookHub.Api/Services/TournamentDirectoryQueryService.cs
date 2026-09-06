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

public sealed record DirectorySearchResult(
    List<(TournamentDirectoryEntry Entry, double? DistanceKm)> Items, int Total, bool Truncated);

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
            var total = await filtered.CountAsync(ct);
            var items = await filtered
                .OrderBy(e => e.StartDate).ThenBy(e => e.Name)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);
            return new DirectorySearchResult(
                items.Select(e => (e, (double?)null)).ToList(), total, false);
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
            .OrderBy(x => x.Entry.StartDate).ThenBy(x => x.Distance)
            .ToList();

        return new DirectorySearchResult(
            withDistance.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => (x.Entry, (double?)x.Distance)).ToList(),
            withDistance.Count,
            truncated);
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
