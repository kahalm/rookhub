using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>Was ein Durchgang getan hat.</summary>
public record CalendarSweepResult(int Read, int Added, int Matched, int Merged);

/// <summary>
/// Der ANKUENDIGUNGS-Kalender von chess-results als zusaetzliche Quelle.
///
/// <para><b>Warum, obwohl wir chess-results schon lesen.</b> Die Seite hat ZWEI Datenbestaende.
/// Die Turniersuche fuellt sich, wenn der Veranstalter seine Swiss-Manager-Datei hochlaedt —
/// typisch Tage bis Wochen vorher. Der Kalender wird VORAB gepflegt. Fuer AUT am 2026-09-07
/// gemessen: die Suche kannte 8 im November beginnende Turniere und 7 im Dezember, der Kalender
/// 23 und 16; von 143 kuenftigen Kalendereintraegen fehlten <b>93</b> in der Suche. Die Suche
/// bricht nach zwei Monaten ein, der Kalender traegt gleichmaessig ueber 15 Monate — also genau
/// ueber das Fenster, das dieses Verzeichnis abdecken will.</para>
///
/// <para><b>Ein Abruf fuer alles.</b> Der Kalender kennt 16 Foederationen und den Sammelwert
/// „alle": 209 kuenftige Eintraege in einem Aufruf (AUT 143, GER 21, SUI 21, CZE 7, ITA 4,
/// POL 3). Er ersetzt die Turniersuche nicht — die deckt 261 Foederationen ab.</para>
///
/// <para><b>Was er NICHT liefert:</b> Ort, Bedenkzeit, Rundenzahl, Teilnehmerzahl. Ein neu
/// angelegter Eintrag bleibt deshalb ohne Koordinaten, bis die Turniersuche ihn einholt. Das ist
/// beabsichtigt: ein Turnier ohne Pin ist in Liste und Kalender trotzdem auffindbar, ein
/// geratener Pin waere schlechter als keiner.</para>
/// </summary>
public class TournamentCalendarSweepService
{
    /// <summary>
    /// Wie weit Startdatum und Kalendereintrag auseinanderliegen duerfen, um noch dasselbe
    /// Turnier zu sein. Dieselbe Toleranz wie beim FIDE-Abgleich.
    /// </summary>
    internal int MatchDayTolerance { get; set; } = 1;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TournamentCalendarSweepService> _log;

    public TournamentCalendarSweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        ILogger<TournamentCalendarSweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    /// <param name="federation">Dreibuchstabiger Code, oder <c>-</c> fuer alle 16 auf einmal.</param>
    public async Task<CalendarSweepResult> RunAsync(
        string federation = "-", CancellationToken ct = default)
    {
        var entries = await FetchAsync(federation, ct);
        if (entries.Count == 0) return new CalendarSweepResult(0, 0, 0, 0);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        int added = 0, matched = 0, merged = 0;

        foreach (var row in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (row.Start is not { } start || row.Name.Length == 0) continue;
            // Vergangenes bringt hier nichts: der Wert dieser Quelle ist der VORLAUF, und ein
            // gespielter Termin steht ohnehin laengst in der Turniersuche.
            if ((row.End ?? start) < today) continue;

            // Der eigene frueher angelegte Eintrag, falls es einen gibt.
            var ownPublicId = row.CalendarId is { Length: > 0 } id ? $"k{id}" : null;
            var own = ownPublicId is null
                ? null
                : await _db.TournamentDirectoryEntries
                    .FirstOrDefaultAsync(e => e.PublicId == ownPublicId, ct);

            var match = await FindSameTournamentAsync(row, start, ct);

            if (match is not null)
            {
                // Der haeufige Fall: die Turniersuche kennt das Turnier inzwischen auch. Nur den
                // Herkunftsvermerk setzen — der Kalender hat kein Feld, das die Suche nicht
                // besser fuehrt.
                await NoteSourceAsync(match, row, now, ct);
                matched++;

                // Und wenn wir es frueher SELBST angelegt hatten, ist das jetzt ein Duplikat:
                // dieselbe Veranstaltung unter zwei Kennungen. Der eigene Eintrag wird
                // zurueckgezogen, statt beide nebeneinander stehen zu lassen.
                if (own is not null && own.Id != match.Id && own.RemovedAt is null)
                {
                    own.RemovedAt = now;
                    merged++;
                    _log.LogInformation(
                        "Kalender: {PublicId} geht in {Target} auf ({Name})",
                        own.PublicId, match.PublicId, match.Name);
                }
                continue;
            }

            if (own is not null)
            {
                // Schon von uns angelegt: Termin und Name nachziehen, mehr gibt es nicht.
                own.Name = Truncate(row.Name, 500)!;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                await NoteSourceAsync(own, row, now, ct);
                continue;
            }

            // Ohne Kalender-Nummer wird NICHTS angelegt. Sie ist die einzige stabile Kennung, die
            // diese Quelle hergibt (an der echten Seite gemessen: 147 von 209 kuenftigen tragen
            // eine, 62 nicht). Ohne sie liesse sich „neues Turnier" nicht von „umbenanntes
            // Turnier" unterscheiden, und der Bestand bekaeme jede Nacht ein Duplikat mehr — das
            // waere schlimmer als die fehlenden 62 einer ZUSATZquelle.
            if (ownPublicId is null) continue;

            var entry = new TournamentDirectoryEntry
            {
                PublicId = ownPublicId,
                // Der Kalender kennt keine Turniernummer. Alles, was chess-results wirklich
                // braucht (Rundenplan, Vereins-Aufloesung, Abo), bleibt deshalb aus — bis die
                // Turniersuche das Turnier einholt und der Abgleich oben es zusammenfuehrt.
                ChessResultsId = null,
                Name = Truncate(row.Name, 500)!,
                Federation = Truncate(row.Federation, 3),
                StartDate = start,
                EndDate = row.End ?? start,
                StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                FirstSeenAt = now,
                LastSeenAt = now,
            };

            // Publikum und Format stehen auch hier nur im Namen — dieselbe Ableitung wie beim
            // Sweep. Die Turnierart bleibt `Unknown`: der Kalender sagt nichts darueber, und
            // Raten waere schlechter als Schweigen.
            entry.AgeGroups = TournamentClassifier.AgeGroupsOf(entry.Name);
            entry.Gender = TournamentClassifier.GenderOf(entry.Name);
            entry.IsLeague = TournamentClassifier.LooksLikeLeague(
                entry.Name, entry.Kind, entry.StartDate, entry.EndDate);

            _db.TournamentDirectoryEntries.Add(entry);
            await NoteSourceAsync(entry, row, now, ct);
            added++;
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation(
            "Kalender {Federation}: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Merged} zusammengefuehrt",
            federation, entries.Count, added, matched, merged);
        return new CalendarSweepResult(entries.Count, added, matched, merged);
    }

    /// <summary>
    /// Denselben Eintrag in der Datenbank finden.
    ///
    /// <para>Ueber Termin und Namen, NICHT ueber die Turniernummer: die traegt fast kein
    /// Kalendereintrag (an AUT gemessen 6 von 146). Ein falsches Zusammenfuehren macht aus zwei
    /// Turnieren eines und ist schlimmer als ein Duplikat — deshalb im Zweifel NEIN.</para>
    /// </summary>
    private async Task<TournamentDirectoryEntry?> FindSameTournamentAsync(
        CrawlerCalendarEntry row, DateOnly start, CancellationToken ct)
    {
        // Die mitgelieferte Turniernummer ist selten, aber wenn sie da ist, ist sie eindeutig.
        if (row.ChessResultsId is { Length: > 0 } tnr)
        {
            var byId = await _db.TournamentDirectoryEntries
                .FirstOrDefaultAsync(e => e.ChessResultsId == tnr, ct);
            if (byId is not null) return byId;
        }

        var from = start.AddDays(-MatchDayTolerance);
        var to = start.AddDays(MatchDayTolerance);
        var fed = row.Federation;

        var candidates = await _db.TournamentDirectoryEntries
            .Where(e => e.ChessResultsId != null
                        && e.RemovedAt == null
                        && (fed == null || e.Federation == fed)
                        && e.StartDate != null && e.StartDate >= from && e.StartDate <= to)
            .ToListAsync(ct);
        if (candidates.Count == 0) return null;

        var words = FideDirectorySweepService.DistinctiveWords(row.Name);
        if (words.Count == 0) return null;

        // Zwei unterscheidende Woerter. Eines allein reicht nicht — „Open" und „Meisterschaft"
        // sind schon weggefiltert, aber ein Ortsname trifft auch das andere Turnier derselben
        // Woche am selben Ort.
        return candidates.FirstOrDefault(c =>
            words.Intersect(FideDirectorySweepService.DistinctiveWords(c.Name)).Count() >= 2);
    }

    /// <summary>
    /// Herkunftsvermerk setzen. Der Schluessel ist die Kalender-Nummer; fehlt sie, wird nichts
    /// vermerkt — ein Vermerk ohne Kennung liesse sich beim naechsten Durchgang nicht
    /// wiedererkennen und legte jede Nacht eine neue Zeile an.
    /// </summary>
    /// <para><b>Diese Quelle zieht bewusst NICHTS zurueck</b> (anders als die 15 Verbandskalender,
    /// siehe <see cref="ExternalDirectorySource.RetireVanishedAsync"/>): nur rund 70 % ihrer Zeilen
    /// tragen ueberhaupt eine Kalender-Nummer, und ohne stabile Kennung ist ein fehlender Eintrag
    /// nicht von einem umbenannten zu unterscheiden. Ein Turnier faelschlich abzusagen ist der
    /// teurere Fehler.</para>
    /// <para>Laeuft ueber den GEMEINSAMEN Helfer: der eindeutige Index auf (Kind, ExternalId) gilt
    /// ueber den ganzen Bestand, und diese eigene Fassung sah nur die Vermerke des uebergebenen
    /// Eintrags. Verschob sich die Zuordnung einer Kalender-Nummer auf einen anderen Eintrag,
    /// starb der Lauf an „Duplicate entry" — am 2026-09-09 mit `4-10814` genau so passiert,
    /// waehrend die 15 anderen Quellen ueber den Helfer schon umhaengten.</para>
    private Task NoteSourceAsync(TournamentDirectoryEntry entry, CrawlerCalendarEntry row,
        DateTime now, CancellationToken ct)
    {
        if (row.CalendarId is not { Length: > 0 } id) return Task.CompletedTask;

        return ExternalDirectorySource.NoteSourceAsync(_db, entry,
            DirectorySourceKind.ChessResultsCalendar, id, row.Url, now, ct);
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerCalendarEntry>> FetchAsync(string federation, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/tournament-search/calendar?fed={Uri.EscapeDataString(federation)}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<CalendarRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.Name is { Length: > 0 })
            .Select(r => new CrawlerCalendarEntry(
                r.Name!, r.Federation, ParseDate(r.StartDate), ParseDate(r.EndDate),
                r.CalendarId, r.Url, r.ChessResultsId))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record CalendarRow(
        string? Name, string? Federation, string? StartDate, string? EndDate,
        string? CalendarId, string? Url, string? ChessResultsId);

    internal sealed record CrawlerCalendarEntry(
        string Name, string? Federation, DateOnly? Start, DateOnly? End,
        string? CalendarId, string? Url, string? ChessResultsId);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
