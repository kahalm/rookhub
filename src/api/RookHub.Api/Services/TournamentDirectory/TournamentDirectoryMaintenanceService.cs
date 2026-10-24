using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>Zustand je Foederation plus die Geocoding-Quote — der Gesundheitsblick.</summary>
public sealed record DirectoryStatus(
    int TotalEntries, int Geocoded, Dictionary<string, int> ByGeoSource, int GazetteerPlaces,
    List<TournamentDirectorySweep> Sweeps);

/// <summary>Ein Eintrag der Arbeitsliste „nicht verortet".</summary>
public sealed record UngeocodedEntry(
    string PublicId, string? ChessResultsId, string Name, string? Federation, string? State,
    string? LocationText, DateOnly? StartDate);

/// <summary>Ergebnis der Neuklassifizierung des Bestands.</summary>
public sealed record ClassifyResult(
    int Examined, int Changed, int Youth, int Female, int Male, int Leagues, int Teams);

/// <summary>Ergebnis der Nachverortung.</summary>
public sealed record GeocodeMissingResult(int Examined, int Resolved, int Cleared);

/// <summary>
/// Die Pflege-Aktionen des Turnierverzeichnisses, die der Admin von Hand ausloest und die KEIN
/// Netz brauchen: Gesundheitsblick, Umschrift des Ortslexikons, Arbeitsliste, Koordinaten von
/// Hand, Herkunftsvermerke nachtragen, Neuklassifizierung und Nachverortung des Bestands.
///
/// <para>Herausgezogen aus <c>AdminTournamentDirectoryController</c> (Codereview 2026-09-29,
/// Z8): der Controller bindet nur noch, ruft auf und waehlt den Statuscode.</para>
/// </summary>
public class TournamentDirectoryMaintenanceService
{
    private readonly AppDbContext _db;
    private readonly GeocodingService _geocoding;

    public TournamentDirectoryMaintenanceService(AppDbContext db, GeocodingService geocoding)
    {
        _db = db;
        _geocoding = geocoding;
    }

    public async Task<DirectoryStatus> StatusAsync(CancellationToken ct)
    {
        var sweeps = await _db.TournamentDirectorySweeps.AsNoTracking()
            .OrderByDescending(s => s.LastAttemptedAt)
            .ToListAsync(ct);

        var entries = _db.TournamentDirectoryEntries.AsNoTracking().Where(e => e.RemovedAt == null);
        var total = await entries.CountAsync(ct);
        var bySource = await entries
            .GroupBy(e => e.GeoSource)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new DirectoryStatus(
            total,
            bySource.Where(b => b.Source != GeoSource.None).Sum(b => b.Count),
            bySource.ToDictionary(b => b.Source.ToString(), b => b.Count),
            await _db.GeoPlaces.CountAsync(ct),
            sweeps);
    }

    /// <summary>
    /// Fuellt die zweite Schreibweise (<c>GeoPlace.NameTranscribed</c>) fuer den vorhandenen
    /// Bestand nach. Mehrfach ausfuehrbar: gefuellte Zeilen bleiben unberuehrt.
    /// </summary>
    /// <returns>Die Zahl der geschriebenen Zeilen.</returns>
    public async Task<int> TranscribeGazetteerAsync(CancellationToken ct)
    {
        const int batch = 5000;
        var written = 0;
        var lastId = 0;

        // Durchgezaehlt wird ueber die Id, NICHT ueber „Spalte noch leer". Eine Zeile, deren Name
        // in einer Schrift steht, die die Umschrift nicht abdeckt (Georgisch, Armenisch,
        // Hebraeisch), bleibt naemlich zu Recht leer — genau das heisst „nichts Vergleichbares",
        // und eine Schleife ueber die leeren Zeilen kaeme nie zum Ende.
        while (!ct.IsCancellationRequested)
        {
            var rows = await _db.GeoPlaces
                .Where(g => g.Id > lastId)
                .OrderBy(g => g.Id)
                .Take(batch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;
            lastId = rows[^1].Id;

            foreach (var row in rows)
            {
                var transcribed = GeoTextNormalizer.NormalizeTranscribed(row.Name, row.Country);
                if (transcribed.Length > 200) transcribed = transcribed[..200];
                if (transcribed == row.NameTranscribed) continue;
                row.NameTranscribed = transcribed;
                written++;
            }
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        return written;
    }

    /// <summary>Die Eintraege, die der Gazetteer nicht verorten konnte - Arbeitsliste fuer Korrekturen.</summary>
    public Task<List<UngeocodedEntry>> UngeocodedAsync(int limit, CancellationToken ct) =>
        _db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => e.RemovedAt == null && e.Lat == null)
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(e => new UngeocodedEntry(
                e.PublicId, e.ChessResultsId, e.Name, e.Federation, e.State, e.LocationText, e.StartDate))
            .ToListAsync(ct);

    /// <summary>
    /// Koordinaten von Hand setzen. Die Quelle wird auf <see cref="GeoSource.Manual"/> gesetzt -
    /// damit ueberschreibt der naechtliche Sweep die Korrektur nicht wieder. Den Wertebereich
    /// prueft der Aufrufer.
    /// </summary>
    /// <returns>Der geaenderte Eintrag, oder <c>null</c>, wenn es ihn nicht gibt.</returns>
    public async Task<TournamentDirectoryEntry?> SetCoordinatesAsync(
        string publicId, double lat, double lon, string? placeName, CancellationToken ct)
    {
        var entry = await _db.TournamentDirectoryEntries
            .FirstOrDefaultAsync(e => e.PublicId == publicId, ct);
        if (entry is null) return null;

        entry.Lat = lat;
        entry.Lon = lon;
        entry.GeoSource = GeoSource.Manual;
        // Gekappt: die Spalte ist varchar(200), und eine zu lange Eingabe waere sonst ein 500er
        // statt einer gespeicherten Korrektur.
        var trimmed = placeName?.Trim();
        entry.GeoPlaceName = trimmed is { Length: > 200 } ? trimmed[..200] : trimmed;
        entry.UpdatedAt = DateTime.UtcNow;
        // UI-Sweep 2026-10-11 (r2-t-dup-cards): der Gruppenschluessel liest den verorteten Ort.
        DirectoryKeys.ApplyGrouping(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>
    /// Den HERKUNFTSVERMERK fuer den Altbestand nachtragen: jeder bestehende Eintrag stammt aus
    /// der chess-results-Turniersuche, seine <c>ChessResultsId</c> ist die Nummer dort.
    /// </summary>
    /// <returns>Die Zahl der nachgetragenen Vermerke.</returns>
    public async Task<int> BackfillSourcesAsync(CancellationToken ct)
    {
        // Nur Eintraege, die WIRKLICH von chess-results kommen — ein FIDE-Eintrag hat dort
        // keine Nummer und bekommt seinen Vermerk von seinem eigenen Durchgang.
        var entries = await _db.TournamentDirectoryEntries
            .Include(e => e.Sources)
            .Where(e => e.ChessResultsId != null
                        && !e.Sources.Any(s => s.Kind == DirectorySourceKind.ChessResults))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            // FirstSeenAt des Eintrags, nicht „jetzt": der Vermerk soll sagen, seit wann die
            // Quelle das Turnier fuehrt, und das ist bekannt.
            await TournamentDirectoryService.NoteSourceAsync(_db,
                entry, DirectorySourceKind.ChessResults, entry.ChessResultsId!, now);
            var source = entry.Sources[^1];
            source.FirstSeenAt = entry.FirstSeenAt;
            source.LastSeenAt = entry.LastSeenAt;
        }

        if (entries.Count > 0) await _db.SaveChangesAsync(ct);
        return entries.Count;
    }

    /// <summary>
    /// Publikum und Format des GANZEN Bestands aus den Turniernamen neu ableiten (Jugendklasse,
    /// Geschlechtsklasse, Liga). Die Turnier<b>art</b> (Einzel/Mannschaft) bleibt unangetastet.
    /// </summary>
    public async Task<ClassifyResult> ClassifyAsync(CancellationToken ct)
    {
        var entries = await _db.TournamentDirectoryEntries.ToListAsync(ct);
        var changed = 0;

        foreach (var entry in entries)
        {
            var ageGroups = TournamentClassifier.AgeGroupsOf(entry.Name);
            var gender = TournamentClassifier.GenderOf(entry.Name);
            var isLeague = TournamentClassifier.LooksLikeLeague(
                entry.Name, entry.Kind, entry.StartDate, entry.EndDate);

            if (entry.AgeGroups == ageGroups && entry.Gender == gender && entry.IsLeague == isLeague)
                continue;

            entry.AgeGroups = ageGroups;
            entry.Gender = gender;
            entry.IsLeague = isLeague;
            changed++;
        }

        if (changed > 0) await _db.SaveChangesAsync(ct);

        return new ClassifyResult(
            entries.Count,
            changed,
            entries.Count(e => (e.AgeGroups & TournamentClassifier.YouthMask) != TournamentAgeGroups.None),
            entries.Count(e => e.Gender == TournamentGender.Female),
            entries.Count(e => e.Gender == TournamentGender.Male),
            entries.Count(e => e.IsLeague),
            entries.Count(e => e.Kind == TournamentKind.Team));
    }

    /// <summary>
    /// Alle noch nicht verorteten Eintraege erneut durch den Gazetteer schicken. Ohne
    /// <paramref name="limit"/> der GANZE Bestand (kein Netz, nichts zu portionieren);
    /// <paramref name="force"/> nimmt auch schon verortete Eintraege vor.
    /// </summary>
    public async Task<GeocodeMissingResult> GeocodeMissingAsync(int limit, bool force, CancellationToken ct)
    {
        // `force` nimmt auch SCHON verortete Eintraege vor. Gebraucht wird das, wenn sich die
        // Verortungs-REGELN aendern: der naechtliche Sweep verortet einen bestehenden Eintrag nur
        // neu, wenn sich sein Ortstext geaendert hat (sonst liefe jede Nacht der ganze Bestand
        // durch den Gazetteer). Ein falscher Pin aus einer alten Regel bliebe damit fuer immer
        // stehen. Von Hand gesetzte Koordinaten bleiben in JEDEM Fall unberuehrt.
        var entries = await _db.TournamentDirectoryEntries
            .Include(e => e.Venues)
            .Where(e => e.RemovedAt == null
                        && e.GeoSource != GeoSource.Manual
                        // Von der Quelle mitgelieferte Koordinaten sind genauer als alles, was das
                        // Ortslexikon daraus machen kann — sie bleiben wie die von Hand gesetzten.
                        && e.GeoSource != GeoSource.SourceProvided
                        // Ebenso der ueber die VEREINSNAMEN aufgeloeste Spielort: dort hat ein
                        // Seitenabruf BEWIESEN, welcher von mehreren gleichnamigen Orten gemeint
                        // ist (`VenueDisambiguationService`) — genau der Fall, den die Namensregel
                        // per Definition nicht loesen kann. Am 2026-09-09 auf Dev vorgefuehrt: ein
                        // `force`-Lauf machte aus allen 17 solchen Pins wieder „mehrdeutig, kein
                        // Pin", und weil `TeamHintCheckedAt` einen zweiten Abruf verhindert, waere
                        // die Arbeit fuer immer weg gewesen.
                        && e.GeoSource != GeoSource.TeamHint
                        && (force || e.Lat == null))
            .OrderBy(e => e.StartDate)
            // KEIN Deckel als Vorgabe: dieser Lauf braucht kein Netz. Der Geocoder rechnet gegen
            // das lokale Ortslexikon, der ganze Bestand (6598 Eintraege) war in 14 Sekunden durch.
            // Ein Deckel taeuschte hier Portionen vor, die es nicht gibt: die Auswahl hat keine
            // Fortschrittsmarke, ein zweiter Aufruf saehe also WIEDER dieselben ersten N. Am
            // 2026-09-09 live vorgefuehrt — sieben Aufrufe mit `limit=1000` meldeten je „1000
            // geprueft, 0 verortet", waehrend 1876 Eintraege dahinter nie an die Reihe kamen.
            // `limit > 0` bleibt als Notbremse fuer den Fall, dass ein Lauf doch einmal zu lange
            // dauert; dann gilt aber dieselbe Einschraenkung, und der Aufrufer muss es wissen.
            .Take(limit > 0 ? Math.Min(limit, 100_000) : int.MaxValue)
            .ToListAsync(ct);

        var resolved = 0;
        var cleared = 0;
        foreach (var entry in entries)
        {
            // Alle Spielorte, nicht nur einen: bei Ligen nennt der Ortstext mehrere.
            var results = await _geocoding.ResolveManyAsync(
                entry.LocationText, entry.State, entry.Federation, ct);
            var located = results.Where(r => r.Source != GeoSource.Ambiguous).ToList();
            var primary = located.FirstOrDefault();

            if (primary is null)
            {
                if (ClearPin(entry, results, force)) cleared++;
                continue;
            }

            ApplyPin(entry, primary, located);
            resolved++;
        }
        await _db.SaveChangesAsync(ct);

        return new GeocodeMissingResult(entries.Count, resolved, cleared);
    }

    /// <summary>
    /// Kein verlaesslicher Treffer. Mehrdeutig bleibt ohne Pin — aber als solches vermerkt, damit
    /// die Arbeitsliste „gefunden, aber unklar" von „nichts gefunden" unterscheiden kann. Bei
    /// <paramref name="force"/> wird ein bestehender Pin dabei ENTFERNT: er stammt aus einer
    /// Regel, die wir gerade als unzuverlaessig erkannt haben.
    /// </summary>
    /// <returns>Ob ein Pin entfernt wurde.</returns>
    private bool ClearPin(TournamentDirectoryEntry entry, List<GeocodeResult> results, bool force)
    {
        var ambiguous = results.Any(r => r.Source == GeoSource.Ambiguous);
        var target = ambiguous ? GeoSource.Ambiguous : GeoSource.None;
        var cleared = false;
        if (force && entry.Lat != null)
        {
            entry.Lat = null;
            entry.Lon = null;
            entry.GeoPlaceName = null;
            if (entry.Venues.Count > 0) _db.TournamentDirectoryVenues.RemoveRange(entry.Venues);
            entry.Venues = [];
            cleared = true;
        }
        if (entry.GeoSource != target)
        {
            entry.GeoSource = target;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        // UI-Sweep 2026-10-11 (r2-t-dup-cards): ohne Ort zaehlt wieder der Ortstext.
        DirectoryKeys.ApplyGrouping(entry);
        return cleared;
    }

    private void ApplyPin(TournamentDirectoryEntry entry, GeocodeResult primary, List<GeocodeResult> located)
    {
        entry.Lat = primary.Lat;
        entry.Lon = primary.Lon;
        entry.GeoSource = primary.Source;
        // Gekuerzt wie im Sweep (`DirectoryGeocoder.GeocodeAsync`): ein ueberlanger Teilort liesse
        // sonst das EINE SaveChanges am Ende des ganzen Laufs scheitern.
        entry.GeoPlaceName = ExternalDirectorySource.Truncate(primary.PlaceName, 200);
        entry.UpdatedAt = DateTime.UtcNow;
        // UI-Sweep 2026-10-11 (r2-t-dup-cards): der Gruppenschluessel liest den verorteten Ort.
        DirectoryKeys.ApplyGrouping(entry);

        if (entry.Venues.Count > 0) _db.TournamentDirectoryVenues.RemoveRange(entry.Venues);
        entry.Venues = located.Count < 2 ? [] : located
            .Select((r, i) => new TournamentDirectoryVenue
            {
                Ordinal = i, Name = ExternalDirectorySource.Truncate(r.PlaceName, 200)!,
                SourceText = ExternalDirectorySource.Truncate(r.SourceText, 300),
                Lat = r.Lat, Lon = r.Lon, GeoSource = r.Source,
            })
            .ToList();
    }
}
