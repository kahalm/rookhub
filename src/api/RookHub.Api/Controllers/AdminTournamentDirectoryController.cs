using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Wartung des Turnierverzeichnisses: Sweep von Hand ausloesen, Gazetteer importieren,
/// Geocoding-Qualitaet ansehen und einzelne Koordinaten korrigieren.
///
/// Gesamter Controller hinter <see cref="Permissions.TournamentsManage"/> - ein blosser
/// IsAdmin-Check im Methodenrumpf haette dieselbe Wirkung, aber die Berechtigung waere nicht
/// zuteilbar und der Endpunkt taeuchte in keiner Rechteuebersicht auf.
/// </summary>
[ApiController]
[Route("api/admin/tournament-directory")]
[Authorize]
[HasPermission(Permissions.TournamentsManage)]
public class AdminTournamentDirectoryController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly TournamentDirectoryService _directory;
    private readonly GazetteerImportService _gazetteer;
    private readonly GeocodingService _geocoding;

    public AdminTournamentDirectoryController(
        AppDbContext db,
        TournamentDirectoryService directory,
        GazetteerImportService gazetteer,
        GeocodingService geocoding,
        VenueDisambiguationService disambiguation,
        TournamentRoundPlanService roundPlans,
        FideDirectorySweepService fide)
    {
        _db = db;
        _directory = directory;
        _gazetteer = gazetteer;
        _geocoding = geocoding;
        _disambiguation = disambiguation;
        _roundPlans = roundPlans;
        _fide = fide;
    }

    private readonly VenueDisambiguationService _disambiguation;
    private readonly TournamentRoundPlanService _roundPlans;
    private readonly FideDirectorySweepService _fide;

    /// <summary>
    /// Nimmt die naechsten Turniere vor, deren Spielort ueber die VEREINSNAMEN aufzuloesen ist —
    /// der Abkuerzungs-Fall („St.Veit" ist Tirol ODER, gemeint, „St. Veit an der Glan" in
    /// Kaernten). Kostet EINEN Seitenabruf je Turnier und ist deshalb gedeckelt; jedes Turnier
    /// wird nur einmal versucht (`TeamHintCheckedAt`).
    /// </summary>
    [HttpPost("disambiguate")]
    public async Task<IActionResult> Disambiguate([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await _disambiguation.RunAsync(limit, ct);
        return Ok(new { result.Checked, result.Resolved, result.Failed });
    }

    /// <summary>Zustand je Foederation plus die Geocoding-Quote - der Gesundheitsblick.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
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

        return Ok(new
        {
            totalEntries = total,
            geocoded = bySource.Where(b => b.Source != GeoSource.None).Sum(b => b.Count),
            byGeoSource = bySource.ToDictionary(b => b.Source.ToString(), b => b.Count),
            gazetteerPlaces = await _db.GeoPlaces.CountAsync(ct),
            sweeps = sweeps.Select(s => new
            {
                s.Federation, s.LastSweptAt, s.LastAttemptedAt, s.LastRowCount,
                s.LastError, s.ConsecutiveFailures
            }),
        });
    }

    /// <summary>
    /// Sweep fuer die angegebenen Foederationen sofort ausfuehren. Laeuft im Anfrageweg (der
    /// Aufrufer soll das Ergebnis sehen) - bei vielen Foederationen dauert das entsprechend,
    /// weil der Crawler seine Anfragen ohnehin serialisiert.
    /// </summary>
    [HttpPost("sweep")]
    public async Task<IActionResult> Sweep([FromBody] SweepRequest request, CancellationToken ct)
    {
        var federations = (request.Federations ?? [])
            .Select(f => f.Trim().ToUpperInvariant())
            .Where(f => f.Length == 3 && f.All(char.IsAsciiLetterUpper))
            .Distinct()
            .Take(20)
            .ToList();

        if (federations.Count == 0)
            return BadRequest(new { message = "Provide 1-20 three-letter federation codes." });

        var results = await _directory.RunSweepAsync(federations, ct);
        return Ok(results);
    }

    /// <summary>Postleitzahlen eines Landes (ISO-3166-1-alpha-2) aus dem GeoNames-Export laden.</summary>
    [HttpPost("gazetteer/postal/{iso2}")]
    public async Task<IActionResult> ImportPostal(string iso2, CancellationToken ct)
    {
        var result = await _gazetteer.ImportPostalCodesAsync(iso2, ct);
        return result.Error is null ? Ok(result) : StatusCode(502, result);
    }

    /// <summary>Weltweite Ortsliste (cities15000) laden - Grundlage fuer Laender ohne PLZ-Datensatz.</summary>
    [HttpPost("gazetteer/cities")]
    public async Task<IActionResult> ImportCities(CancellationToken ct)
    {
        var result = await _gazetteer.ImportCitiesAsync(ct);
        return result.Error is null ? Ok(result) : StatusCode(502, result);
    }

    /// <summary>Die Eintraege, die der Gazetteer nicht verorten konnte - Arbeitsliste fuer Korrekturen.</summary>
    [HttpGet("ungeocoded")]
    public async Task<IActionResult> Ungeocoded([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var entries = await _db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => e.RemovedAt == null && e.Lat == null)
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(e => new { e.PublicId, e.ChessResultsId, e.Name, e.Federation, e.State, e.LocationText, e.StartDate })
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>
    /// Koordinaten von Hand setzen. Die Quelle wird auf <see cref="GeoSource.Manual"/> gesetzt -
    /// damit ueberschreibt der naechtliche Sweep die Korrektur nicht wieder.
    /// </summary>
    [HttpPut("{id}/coordinates")]
    public async Task<IActionResult> SetCoordinates(
        string id, [FromBody] CoordinateInput input, CancellationToken ct)
    {
        if (input.Lat is < -90 or > 90 || input.Lon is < -180 or > 180)
            return BadRequest(new { message = "Coordinates out of range." });

        var entry = await _db.TournamentDirectoryEntries
            .FirstOrDefaultAsync(e => e.PublicId == id, ct);
        if (entry is null) return NotFound();

        entry.Lat = input.Lat;
        entry.Lon = input.Lon;
        entry.GeoSource = GeoSource.Manual;
        // Gekappt: die Spalte ist varchar(200), und eine zu lange Eingabe waere sonst ein 500er
        // statt einer gespeicherten Korrektur.
        var placeName = input.PlaceName?.Trim();
        entry.GeoPlaceName = placeName is { Length: > 200 } ? placeName[..200] : placeName;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { entry.PublicId, entry.ChessResultsId, entry.Lat, entry.Lon, GeoSource = entry.GeoSource.ToString() });
    }

    /// <summary>
    /// Den FIDE-Kalender sofort einlesen — sonst wartet er auf den naechtlichen Lauf.
    ///
    /// <para>Ein Abruf je Jahr. Die Jahre werden im Dienst AUFSTEIGEND abgearbeitet, weil die
    /// Jahres-Zuordnung eines Ereignisses ueber den Jahreswechsel daran haengt.</para>
    /// </summary>
    [HttpPost("fide")]
    public async Task<IActionResult> Fide(
        [FromQuery] string? years = null, CancellationToken ct = default)
    {
        var list = new List<int>();
        foreach (var part in (years ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var year) || year is < 2000 or > 2100)
                return BadRequest(new { message = "years must be a comma-separated list of years between 2000 and 2100." });
            list.Add(year);
        }
        // Ohne Angabe das laufende Jahr plus die zwei naechsten — weiter voraus fuehrt FIDE
        // praktisch nichts.
        if (list.Count == 0) list = [.. Enumerable.Range(DateTime.UtcNow.Year, 3)];

        var result = await _fide.RunAsync(list, ct);
        return result.Succeeded
            ? Ok(new { result.Fetched, result.Added, result.Updated, result.MergedIntoExisting })
            : StatusCode(502, new { message = result.Error, result.Fetched, result.Added });
    }

    /// <summary>
    /// Den HERKUNFTSVERMERK fuer den Altbestand nachtragen: jeder bestehende Eintrag stammt aus
    /// der chess-results-Turniersuche, seine <c>ChessResultsId</c> ist die Nummer dort.
    ///
    /// <para>Der naechtliche Sweep vermerkt das von selbst — aber erst, wenn er die Foederation
    /// wieder vornimmt, und die Rotation braucht dafuer eine Woche. Braucht kein Netz.</para>
    /// </summary>
    [HttpPost("backfill-sources")]
    public async Task<IActionResult> BackfillSources(CancellationToken ct = default)
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
            TournamentDirectoryService.NoteSource(
                entry, DirectorySourceKind.ChessResults, entry.ChessResultsId!, now);
            var source = entry.Sources[^1];
            source.FirstSeenAt = entry.FirstSeenAt;
            source.LastSeenAt = entry.LastSeenAt;
        }

        if (entries.Count > 0) await _db.SaveChangesAsync(ct);
        return Ok(new { added = entries.Count });
    }

    /// <summary>
    /// Die SPIELTERMINE langlaufender Turniere nachtragen — von Hand auslösbar, damit der
    /// Bestand nicht auf die naechtlichen Chargen warten muss.
    ///
    /// <para>Ein Seitenabruf je Turnier (chess-results art=14), deshalb gedeckelt. Siehe
    /// <see cref="TournamentRoundPlanService"/>: Start und Ende einer Liga sagen nicht, wann
    /// gespielt wird, und der Kalender zeigte sie deshalb an rund 200 Tagen statt an ihren elf
    /// Spieltagen.</para>
    /// </summary>
    /// <param name="retryEmpty">
    /// Auch Eintraege erneut vornehmen, die als geprueft gelten, aber keinen Termin haben.
    /// Gebraucht, wenn das HOLEN kaputt war und deshalb lauter leere Plaene vermerkt wurden —
    /// ohne das bliebe die Behebung fuer den bestehenden Bestand wirkungslos.
    /// </param>
    [HttpPost("round-plans")]
    public async Task<IActionResult> RoundPlans([FromQuery] int limit = 100,
        [FromQuery] bool retryEmpty = false, CancellationToken ct = default)
    {
        var result = await _roundPlans.RunAsync(Math.Clamp(limit, 1, 1000), retryEmpty, ct);
        return Ok(new { result.Checked, result.WithPlan, result.Failed });
    }

    /// <summary>
    /// Publikum und Format des GANZEN Bestands aus den Turniernamen neu ableiten (Jugendklasse,
    /// Geschlechtsklasse, Liga).
    ///
    /// <para>Braucht kein Netz — die drei Merkmale stehen im Namen, der schon in der Datenbank
    /// liegt. Genau deshalb gibt es diesen Knopf: nach einem Deploy waeren die 4000 bestehenden
    /// Eintraege sonst bis zum naechsten naechtlichen Sweep unklassifiziert, und der Filter
    /// „nur Erwachsene" liesse eine halb leere Liste zurueck. Und wenn eine Wortliste im
    /// <see cref="TournamentClassifier"/> nachgeruestet wird (etwa „Schachrallye" = Nachwuchs),
    /// ist das der Weg, sie auf den Bestand anzuwenden.</para>
    ///
    /// <para>Die Turnier<b>art</b> (Einzel/Mannschaft) bleibt unangetastet: die kommt aus einer
    /// zweiten chess-results-Abfrage, nicht aus dem Namen — sie fuellt der naechste Sweep.</para>
    /// </summary>
    [HttpPost("classify")]
    public async Task<IActionResult> Classify(CancellationToken ct = default)
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

        return Ok(new
        {
            examined = entries.Count,
            changed,
            youth = entries.Count(e => (e.AgeGroups & TournamentClassifier.YouthMask) != TournamentAgeGroups.None),
            female = entries.Count(e => e.Gender == TournamentGender.Female),
            male = entries.Count(e => e.Gender == TournamentGender.Male),
            leagues = entries.Count(e => e.IsLeague),
            teams = entries.Count(e => e.Kind == TournamentKind.Team),
        });
    }

    /// <summary>
    /// Alle noch nicht verorteten Eintraege erneut durch den Gazetteer schicken - nach einem
    /// frischen Import der eigentliche Nutzen: die Zeilen von gestern bekommen ihre Pins.
    /// </summary>
    [HttpPost("geocode-missing")]
    public async Task<IActionResult> GeocodeMissing(
        [FromQuery] int limit = 1000, [FromQuery] bool force = false, CancellationToken ct = default)
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
                        && (force || e.Lat == null))
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 10000))
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
                // Mehrdeutig bleibt ohne Pin — aber als solches vermerkt, damit die Arbeitsliste
                // „gefunden, aber unklar" von „nichts gefunden" unterscheiden kann. Bei `force`
                // wird ein bestehender Pin dabei ENTFERNT: er stammt aus einer Regel, die wir
                // gerade als unzuverlaessig erkannt haben.
                var ambiguous = results.Any(r => r.Source == GeoSource.Ambiguous);
                var target = ambiguous ? GeoSource.Ambiguous : GeoSource.None;
                if (force && entry.Lat != null)
                {
                    entry.Lat = null;
                    entry.Lon = null;
                    entry.GeoPlaceName = null;
                    if (entry.Venues.Count > 0) _db.TournamentDirectoryVenues.RemoveRange(entry.Venues);
                    entry.Venues = [];
                    cleared++;
                }
                if (entry.GeoSource != target)
                {
                    entry.GeoSource = target;
                    entry.UpdatedAt = DateTime.UtcNow;
                }
                continue;
            }

            entry.Lat = primary.Lat;
            entry.Lon = primary.Lon;
            entry.GeoSource = primary.Source;
            entry.GeoPlaceName = primary.PlaceName;
            entry.UpdatedAt = DateTime.UtcNow;

            if (entry.Venues.Count > 0) _db.TournamentDirectoryVenues.RemoveRange(entry.Venues);
            entry.Venues = located.Count < 2 ? [] : located
                .Select((r, i) => new TournamentDirectoryVenue
                {
                    Ordinal = i, Name = r.PlaceName, SourceText = r.SourceText,
                    Lat = r.Lat, Lon = r.Lon, GeoSource = r.Source,
                })
                .ToList();
            resolved++;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { examined = entries.Count, resolved, cleared });
    }

    public class SweepRequest
    {
        public List<string>? Federations { get; set; }
    }

    public class CoordinateInput
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? PlaceName { get; set; }
    }
}
