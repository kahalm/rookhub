using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Uebernimmt den FIDE-Kalender ins Verzeichnis — die zweite Quelle.
///
/// <para><b>Warum.</b> Das Verzeichnis lebt aus der chess-results-Turniersuche, und die grossen
/// internationalen Turniere stehen dort nicht oder erst spaet. Am 2026-09-07 gemessen: von 139
/// FIDE-Ereignissen des Jahres 2026 fanden sich <b>132 nicht</b> im Bestand — Tata Steel, Rilton
/// Cup, Prague Masters, Aeroflot Open, das Frauen-Kandidatenturnier, die Freestyle-WM.</para>
///
/// <para><b>Die Jahre werden AUFSTEIGEND abgefragt.</b> Die FIDE-Jahresansicht nennt nur Tag und
/// Monat; ein Ereignis ueber den Jahreswechsel („27 Dec - 05 Jan") erscheint in zwei Ansichten,
/// und nur die fruehere liest es richtig. Der Crawler legt es als „beginnt im abgefragten Jahr"
/// aus — wer aufsteigend geht und den ERSTEN Treffer je Ereignisnummer behaelt, bekommt die
/// richtigen Daten.</para>
///
/// <para><b>Zusammenfuehren statt verdoppeln — aber nur mit Beleg.</b> Ein Teil der FIDE-Turniere
/// steht auch auf chess-results. Ein falsches Zusammenfuehren macht aus zwei Turnieren eines und
/// ist schlimmer als ein Duplikat, deshalb muessen Startdatum UND Name (bzw. Ort) zusammenpassen;
/// im Zweifel entsteht ein eigener Eintrag. Bei 132 von 139 ohne Gegenstueck faellt die Genauigkeit
/// dieser Regel ohnehin kaum ins Gewicht — die Vorsicht kostet also nichts.</para>
/// </summary>
public class FideDirectorySweepService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<FideDirectorySweepService> _log;

    public FideDirectorySweepService(
        AppDbContext db, IHttpClientFactory httpClientFactory, GeocodingService geocoding,
        ILogger<FideDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
    }

    /// <summary>Vorsilbe der Identitaet eines FIDE-Eintrags — siehe <c>TournamentDirectoryEntry.PublicId</c>.</summary>
    internal const string PublicIdPrefix = "f";

    /// <summary>Wie viele Tage Abstand ein Zusammenfuehren noch erlaubt. 0 = das Datum muss stimmen.</summary>
    internal int MatchDayTolerance { get; set; } = 1;

    public sealed record FideSweepResult(
        int Fetched, int Added, int Updated, int MergedIntoExisting, int Retired, string? Error)
    {
        public bool Succeeded => Error is null;
    }

    /// <summary>
    /// Nimmt die genannten Jahre vor. Aufsteigend sortiert, weil die Jahres-Zuordnung daran
    /// haengt (siehe Klassenkommentar).
    /// </summary>
    public async Task<FideSweepResult> RunAsync(IReadOnlyList<int> years, CancellationToken ct = default)
    {
        var fetched = 0;
        var added = 0;
        var updated = 0;
        var merged = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten. Bewusst
        // getrennt von `seen`: das entscheidet, welche Jahresansicht ein Ereignis VERARBEITET
        // (die frueheste gewinnt), und eine unlesbare Zeile darf die spaetere nicht verdraengen.
        var delivered = new List<string>();

        foreach (var year in years.Distinct().OrderBy(y => y))
        {
            List<CrawlerFideEvent> events;
            try
            {
                var fetch = await FetchYearAsync(year, ct);
                events = fetch.Rows;
                delivered.AddRange(fetch.Rows.Select(e => e.EventId));
                delivered.AddRange(fetch.Unreadable);
            }
            // Ein HttpClient-TIMEOUT kommt als TaskCanceledException, also als
            // OperationCanceledException, obwohl der Aufrufer nichts abgebrochen hat.
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "FIDE-Kalender {Year} nicht erreichbar", year);
                // Ein gescheitertes Jahr heisst: der Lauf ist unvollstaendig. Es wird NICHTS
                // zurueckgezogen — der Rueckweg unten ist damit uebersprungen.
                return new FideSweepResult(fetched, added, updated, merged, 0, ex.Message);
            }

            foreach (var ev in events)
            {
                // Der ERSTE Treffer je Ereignisnummer gewinnt: bei einem Ereignis ueber den
                // Jahreswechsel ist das die frueherere Ansicht, und nur die liest es richtig.
                if (!seen.Add(ev.EventId)) continue;
                fetched++;

                var outcome = await ApplyAsync(ev, ct);
                if (outcome == Outcome.Added) added++;
                else if (outcome == Outcome.Merged) merged++;
                else updated++;
            }
            await _db.SaveChangesAsync(ct);
        }

        // Was der Kalender nicht mehr fuehrt, wird zurueckgezogen — mit denselben Schranken wie bei
        // den 15 Verbandskalendern (nur kuenftige Eintraege, nur ohne chess-results-Nummer, nur
        // wenn dieser Kalender der einzige Herkunftsvermerk ist, nur bis zum Horizont des Laufs,
        // zwei Naechte Karenz). Die Kennungen des FIDE-Kalenders sind stabile Ereignisnummern,
        // deshalb ist „fehlt" hier von „umbenannt" zu unterscheiden — anders als beim
        // chess-results-ANKUENDIGUNGSkalender, der aus genau diesem Grund nichts zurueckzieht.
        // Der Horizont traegt hier besonders viel: laeuft der Durchgang nur ueber das laufende
        // Jahr, darf er ueber das naechste nichts sagen.
        var retired = await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.Fide, delivered, DateTime.UtcNow, _log, ct);

        _log.LogInformation(
            "FIDE-Durchgang: {Fetched} Ereignisse, {Added} neu, {Updated} aktualisiert, "
            + "{Merged} bestehenden zugeordnet, {Retired} zurueckgezogen",
            fetched, added, updated, merged, retired);

        return new FideSweepResult(fetched, added, updated, merged, retired, null);
    }

    private enum Outcome { Added, Updated, Merged }

    private async Task<Outcome> ApplyAsync(CrawlerFideEvent ev, CancellationToken ct)
    {
        var publicId = PublicIdPrefix + ev.EventId;
        var now = DateTime.UtcNow;

        var entry = await _db.TournamentDirectoryEntries
            .Include(e => e.Sources)
            .Include(e => e.Venues)
            .FirstOrDefaultAsync(e => e.PublicId == publicId, ct);

        if (entry is not null)
        {
            Apply(entry, ev, now);
            await TournamentDirectoryService.NoteSourceAsync(_db, entry, DirectorySourceKind.Fide, ev.EventId, now);
            return Outcome.Updated;
        }

        // Steht dasselbe Turnier schon als chess-results-Eintrag da? Dann bekommt ES den
        // Herkunftsvermerk — ein zweiter Eintrag waere sichtbares Rauschen im Kalender.
        var existing = await FindSameTournamentAsync(ev, ct);
        if (existing is not null)
        {
            await TournamentDirectoryService.NoteSourceAsync(_db, existing, DirectorySourceKind.Fide, ev.EventId, now);
            return Outcome.Merged;
        }

        entry = new TournamentDirectoryEntry
        {
            PublicId = publicId,
            // Bewusst NULL: das Turnier ist auf chess-results nicht ausgeschrieben. Daran haengt,
            // dass die Anzeige „merken" und „Ergebnisse holen" nicht anbietet.
            ChessResultsId = null,
            FirstSeenAt = now,
            CreatedAt = now,
        };
        Apply(entry, ev, now);
        await GeocodeAsync(entry, ev, ct);
        await TournamentDirectoryService.NoteSourceAsync(_db, entry, DirectorySourceKind.Fide, ev.EventId, now);
        _db.TournamentDirectoryEntries.Add(entry);
        return Outcome.Added;
    }

    private static void Apply(TournamentDirectoryEntry entry, CrawlerFideEvent ev, DateTime now)
    {
        entry.Name = Truncate(ev.Name, 500);
        entry.StartDate = ev.Start;
        entry.EndDate = ev.End;
        entry.StartsOnWeekend = ev.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        // Der Ortstext ist alles, was FIDE nennt — Stadt und Land, keine Adresse.
        entry.LocationText = Truncate(
            ev.City is null ? ev.Country : $"{ev.City}, {ev.Country}", 500);
        entry.Federation = Truncate(ev.Country, 3);
        entry.ChangeHash = TournamentDirectoryService.ComputeChangeHash(ev.Start, ev.End, entry.LocationText);

        // FIDE nennt weder Bedenkzeit noch Turnierart. Publikum und Format stehen wie ueberall im
        // NAMEN („FIDE World Youth U14, U16 & U18 Championships", „Norway Chess Women").
        entry.AgeGroups = TournamentClassifier.AgeGroupsOf(entry.Name);
        entry.Gender = TournamentClassifier.GenderOf(entry.Name);
        entry.IsLeague = TournamentClassifier.LooksLikeLeague(
            entry.Name, entry.Kind, entry.StartDate, entry.EndDate);

        TournamentDirectoryService.ApplyGrouping(entry);
        entry.LastSeenAt = now;
        entry.UpdatedAt = now;
        // Wieder aufgetaucht: ein Eintrag, den FIDE erneut fuehrt, ist nicht abgesagt.
        entry.MissedSweeps = 0;
        entry.RemovedAt = null;
    }

    /// <summary>
    /// Verortet einen FIDE-Eintrag. Andere Ausgangslage als bei chess-results: dort ist der Ort
    /// ein Freitext mit Adresse, hier sind Stadt und Land getrennt und sauber — der Gazetteer
    /// braucht also keine Zerlegung, nur den Namen und das Land.
    ///
    /// <para>Online-Ereignisse („ONL") bekommen KEINE Koordinaten: ein Pin fuer ein Turnier, das
    /// nirgends stattfindet, waere eine Falschaussage auf der Karte.</para>
    /// </summary>
    private async Task GeocodeAsync(
        TournamentDirectoryEntry entry, CrawlerFideEvent ev, CancellationToken ct)
    {
        if (ev.City is null || ev.Country is null
            || string.Equals(ev.Country, "ONL", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var located = await _geocoding.ResolveAsync(ev.City, null, ev.Country, ct);
        if (located is null || located.Source == GeoSource.Ambiguous) return;

        entry.Lat = located.Lat;
        entry.Lon = located.Lon;
        entry.GeoSource = located.Source;
        entry.GeoPlaceName = Truncate(located.PlaceName, 200);
    }

    /// <summary>
    /// Steht dieses Turnier schon als Eintrag einer ANDEREN Quelle da?
    ///
    /// <para>Verlangt wird, dass Startdatum und Name (bzw. Ort) zusammenpassen. Ein falsches
    /// Zusammenfuehren macht aus zwei Turnieren eines und ist schlimmer als ein Duplikat —
    /// deshalb im Zweifel NEIN. Die Namen unterscheiden sich zwischen den Quellen erheblich
    /// („Tata Steel Chess 2026 – Masters" gegen „Tata Steel Masters"), also wird ueber
    /// unterscheidende Woerter verglichen, nicht ueber Gleichheit.</para>
    /// </summary>
    private async Task<TournamentDirectoryEntry?> FindSameTournamentAsync(
        CrawlerFideEvent ev, CancellationToken ct)
    {
        var from = ev.Start.AddDays(-MatchDayTolerance);
        var to = ev.Start.AddDays(MatchDayTolerance);

        var candidates = await _db.TournamentDirectoryEntries
            .Include(e => e.Sources)
            .Where(e => e.ChessResultsId != null
                        && e.StartDate != null && e.StartDate >= from && e.StartDate <= to)
            .ToListAsync(ct);
        if (candidates.Count == 0) return null;

        // Die Haeufigkeit wird im LAND des Ereignisses erhoben, nicht global: unter allen
        // Foederationen zusammen waere ein italienisches Allerweltswort wieder selten.
        // Kein Land oder zu wenige Namen: dann greift nur die feste Liste.
        var filler = await ExternalDirectorySource.CorpusFillerAsync(_db, ev.Country, ct);
        var words = DistinctiveWords(ev.Name, filler);
        var city = GeoTextNormalizer.Normalize(ev.City);

        foreach (var candidate in candidates)
        {
            var shared = words.Intersect(DistinctiveWords(candidate.Name, filler)).Count();
            var sameCity = city.Length > 0
                           && GeoTextNormalizer.Normalize(candidate.LocationText).Contains(city, StringComparison.Ordinal);

            // Zwei unterscheidende Woerter, oder eines plus derselbe Ort. Ein Wort allein reicht
            // nicht — „Open" und „Masters" sind schon weggefiltert, aber „Prague" trifft auch
            // das andere Prager Turnier derselben Woche.
            if (ExternalDirectorySource.HasOtherNoteOfSameKind(candidate,
                    new ExternalDirectorySource.MatchHint(
                        DirectorySourceKind.Fide, ev.EventId, ev.City)))
                continue;
            // Zwei Woerter reichen nur, solange die Ortsangaben sich nicht WIDERSPRECHEN: der
            // italienische Fall vom 2026-09-09 (drei fremde Turniere in einem Bozener Eintrag)
            // haette hier genauso zugeschlagen. Und eine Quelle fuehrt dasselbe Turnier nicht
            // zweimal - traegt der Kandidat schon einen FIDE-Vermerk mit anderer Kennung,
            // gehoert dieses Ereignis nicht dorthin (Pruefung oben).
            if (shared >= 2 && ExternalDirectorySource.PlacesAgree(city, candidate.LocationText))
                return candidate;
            if (shared >= 1 && sameCity) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Die unterscheidenden Woerter eines Turniernamens. Was in jedem zweiten Namen steht,
    /// unterscheidet nichts und wuerde beim Zusammenfuehren zu Fehlgriffen fuehren.
    /// </summary>
    internal static HashSet<string> DistinctiveWords(string? name) => DistinctiveWords(name, null);

    /// <param name="corpusFiller">
    /// Zusaetzliche Fuellwoerter, aus der Worthaeufigkeit der Foederation erhoben — die feste Liste
    /// unten ist englisch und deutsch und laesst „torneo", „scacchi", „turniej", „szach" durch.
    /// Siehe <see cref="ExternalDirectorySource.CorpusFillerAsync"/>.
    /// </param>
    internal static HashSet<string> DistinctiveWords(string? name, IReadOnlySet<string>? corpusFiller) =>
        GeoTextNormalizer.Normalize(name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !NameFiller.Contains(w)
                        && (corpusFiller is null || !corpusFiller.Contains(w)))
            .ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> NameFiller = new(StringComparer.Ordinal)
    {
        "chess", "open", "tournament", "championship", "championships", "international", "fide",
        "cup", "trophy", "festival", "memorial", "masters", "challengers", "turnier", "schach",
        "women", "womens", "men", "mens", "youth", "junior", "juniors", "team", "teams",
        "rapid", "blitz", "standard", "classic", "series", "grand", "world", "european",
        "und", "and", "der", "die", "das", "the", "of", "for",
    };

    // ----- Crawler ----------------------------------------------------------

    /// <summary>Die verwertbaren Ereignisse — und die Kennungen der unlesbaren.</summary>
    internal sealed record FideFetch(List<CrawlerFideEvent> Rows, List<string> Unreadable);

    private async Task<FideFetch> FetchYearAsync(int year, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/fide-calendar?year={year}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<FideEventRow>>(body, JsonOptions) ?? [];
        var parsed = rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerFideEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.City, r.Country))
            .ToList();

        // Zeilen ohne lesbaren Termin fallen aus der Verarbeitung, ihre Kennungen aber NICHT aus
        // der Liefer-Liste: der Kalender FUEHRT sie ja. Sonst sammelten sie Fehlschlaege und waeren
        // nach zwei Naechten abgesagt — und ein geaendertes Datumsformat trifft nicht eine Zeile,
        // sondern alle. Dieselbe Regel wie bei den 15 Verbandskalendern; hier braucht es einen
        // eigenen Rueckweg, weil `CrawlerFideEvent` einen festen Termin traegt.
        return new FideFetch(
            [.. parsed.Where(e => e.Start != default && e.End != default)],
            [.. parsed.Where(e => e.Start == default || e.End == default).Select(e => e.EventId)]);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record FideEventRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? City, string? Country);

    internal sealed record CrawlerFideEvent(
        string EventId, string Name, DateOnly Start, DateOnly End, string? City, string? Country);

    private static DateOnly ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var date)
            ? date : default;

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
