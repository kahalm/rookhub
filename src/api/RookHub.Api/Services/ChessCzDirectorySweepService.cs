using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Terminkalender des tschechischen Verbands (chess.cz) als Quelle.
///
/// <para><b>Sein Volumen ist klein und das ist bekannt.</b> 89 Eintraege, davon 18 gar keine
/// Turniere und 33 Runden von drei Mannschaftsmeisterschaften; es bleiben rund 38 echte Turniere,
/// 13 davon nicht auf chess-results. Dort stehen fuer CZE im selben Zeitraum 146 — die Abdeckung
/// ist viermal dichter.</para>
///
/// <para><b>Der Grund, diese Quelle trotzdem zu lesen, sind die 33 Ligarunden.</b> Sie sind
/// SPIELTERMINE, und Spieltermine sind hier teuer: <see cref="TournamentRoundPlanService"/> holt
/// sie sonst mit einem eigenen Seitenabruf JE TURNIER (chess-results <c>art=14</c>, rund sechs
/// Sekunden hinter dem Rate-Limiter). Hier stehen sie in derselben Antwort, und 22 nennen die
/// chess-results-Nummer ihrer Meisterschaft gleich mit — sie landen also direkt am richtigen
/// Eintrag.</para>
///
/// <para><b>Geschrieben werden nur FEHLENDE Runden.</b> Was der Rundenplan-Dienst schon geholt
/// hat, bleibt stehen: chess-results ist der gepflegte Bestand. Umgekehrt gilt dasselbe — der
/// Rundenplan-Dienst ersetzt einen Plan nur, wenn er selbst einen findet (eine LEERE Antwort
/// laesst die hier eingetragenen Termine unangetastet). Und <c>RoundPlanCheckedAt</c> wird
/// bewusst NICHT gesetzt: dieser Vermerk beantwortet die Frage „auf chess-results nachgesehen",
/// und das hat hier niemand.</para>
///
/// <para><b>Die Kennung dieser Quelle ist ein Wort, keine Nummer.</b> Im Markup steht nirgends
/// eine Id; die Identitaet ist der Adressbestandteil der Detailseite („turnovsky-granat-5"), und
/// der wird bis zu 57 Zeichen lang. <see cref="TournamentDirectoryEntry.PublicId"/> fasst 24 —
/// gespeichert wird deshalb ein KURZWERT daraus, und der lesbare Slug steht vollstaendig im
/// Herkunftsvermerk.</para>
/// </summary>
public class ChessCzDirectorySweepService
{
    private const int SaveEvery = 25;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<ChessCzDirectorySweepService> _log;

    public ChessCzDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<ChessCzDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, ct);
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        var counts = new Counts();

        try
        {
            // Erst die Einzeltermine, dann die Meisterschaften: eine Meisterschaft, die auch als
            // gewoehnlicher Eintrag im Kalender steht, soll ihre Runden am selben Eintrag bekommen.
            foreach (var row in events.Where(e => e.Round is null))
            {
                ct.ThrowIfCancellationRequested();
                await ApplySingleAsync(row, now, counts, ct);
                if (counts.Processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }

            foreach (var group in events
                         .Where(e => e.Round is > 0 && e.Series is { Length: > 0 })
                         .GroupBy(e => e.Series!, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                await ApplySeriesAsync(group.Key, [.. group], now, counts, ct);
                await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "chess.cz: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "chess.cz-Kalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen, {Rounds} Spieltermine ergaenzt",
            events.Count, counts.Added, counts.Matched, counts.Retired, counts.Rounds);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz, Bremse
        // gegen halbe Laeufe — siehe RetireVanishedAsync).
        var retired = counts.Retired + await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.CzechChessFederation, counts.Delivered, now, _log, ct);
        return new ExternalSweepResult(events.Count, counts.Added, counts.Rounds, counts.Matched,
            retired);
    }

    private sealed class Counts
    {
        /// <summary>Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung.
        /// Steht hier und nicht in RunAsync, weil die Unterroutinen die Vermerke schreiben.</summary>
        public List<string> Delivered { get; } = [];

        public int Added, Matched, Retired, Rounds, Processed;
    }

    // ----- Einzeltermine ----------------------------------------------------

    private async Task ApplySingleAsync(
        CrawlerChessCzEvent row, DateTime now, Counts counts, CancellationToken ct)
    {
        if (row.EventId.Length == 0) return;
        // Geliefert ist geliefert: die Quelle FUEHRT diese Zeile. Ob WIR sie lesen koennen,
        // ist eine andere Frage. Stand das Eintragen erst hinter den Pruefungen, galt eine
        // Zeile mit unlesbarem Termin als verschwunden und war nach zwei Laeufen abgesagt —
        // und ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle.
        counts.Delivered.Add(row.EventId);
        if (row.Start is not { } start || row.Name.Length == 0) return;

        counts.Processed++;
        var publicId = PublicIdOf(row.EventId);
        var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);

        // Schulung, Trainingslager, Sitzung: nichts anlegen, Bestehendes zurueckziehen.
        if (row.NonTournament)
        {
            if (own is not null && own.RemovedAt is null)
            {
                own.RemovedAt = now;
                counts.Retired++;
            }
            return;
        }

        var match = await ExternalDirectorySource.FindByChessResultsIdAsync(_db, row.ChessResultsId, ct)
                    ?? await ExternalDirectorySource.FindMatchAsync(_db, row.Federation, start, row.Name,
                        new ExternalDirectorySource.MatchHint(
                            DirectorySourceKind.CzechChessFederation, row.EventId, row.Place), ct);

        if (match is not null)
        {
            await ExternalDirectorySource.NoteSourceAsync(_db, match,
                DirectorySourceKind.CzechChessFederation, row.EventId, row.Url, now, ct);
            counts.Matched++;

            if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
            {
                counts.Retired++;
                _log.LogInformation("chess.cz: {PublicId} geht in {Target} auf ({Name})",
                    own!.PublicId, match.PublicId, match.Name);
            }
            return;
        }

        if (own is null)
        {
            own = new TournamentDirectoryEntry
            {
                PublicId = publicId,
                ChessResultsId = row.ChessResultsId,
                FirstSeenAt = now,
            };
            _db.TournamentDirectoryEntries.Add(own);
            counts.Added++;
        }
        else if (own.ChessResultsId is null && row.ChessResultsId is { Length: > 0 })
        {
            own.ChessResultsId = row.ChessResultsId;
        }

        var location = row.Place?.Trim();
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.Federation = row.Federation;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        own.LastSeenAt = now;
        own.MissedSweeps = 0;
        own.RemovedAt = null;
        ExternalDirectorySource.ApplyClassification(own);
        ApplyYouthMark(own, row.Youth);
        counts.Delivered.Add(row.EventId);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.CzechChessFederation, row.EventId, row.Url, now, ct);

        if (location is { Length: > 0 } && (locationChanged || own.Lat is null))
        {
            var hit = await _geocoding.ResolveAsync(location, null, own.Federation, ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                own.Lat = hit.Lat;
                own.Lon = hit.Lon;
                own.GeoSource = hit.Source;
                own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
    }

    // ----- Meisterschaften mit Runden ---------------------------------------

    /// <summary>
    /// Eine Meisterschaft und ihre Runden. Aus elf Kalenderzeilen wird EIN Eintrag mit elf
    /// Spielterminen — nicht elf Eintraege: „2. ligy – 1. kolo" bis „– 11. kolo" sind dieselbe
    /// Veranstaltung, und als elf Turniere im Kalender waeren sie schlicht falsch.
    /// </summary>
    private async Task ApplySeriesAsync(string series, List<CrawlerChessCzEvent> rows,
        DateTime now, Counts counts, CancellationToken ct)
    {
        var rounds = rows
            .Where(r => r.Start is not null && r.Round is > 0)
            .GroupBy(r => r.Round!.Value)
            .Select(g => g.OrderBy(r => r.Start).First())
            .OrderBy(r => r.Round)
            .ToList();
        if (rounds.Count == 0) return;

        counts.Processed++;
        var start = rounds.Min(r => r.Start!.Value);
        var end = rounds.Max(r => r.End ?? r.Start!.Value);
        var chessResultsId = rounds.Select(r => r.ChessResultsId).FirstOrDefault(id => id is { Length: > 0 });
        var publicId = PublicIdOf(series);

        var entry = await FindWithRoundsAsync(chessResultsId, publicId, ct);
        var isOwn = entry is null || entry.PublicId == publicId;

        if (entry is null)
        {
            entry = new TournamentDirectoryEntry
            {
                PublicId = publicId,
                ChessResultsId = chessResultsId,
                Federation = rounds[0].Federation,
                FirstSeenAt = now,
            };
            _db.TournamentDirectoryEntries.Add(entry);
            counts.Added++;
        }
        else if (!isOwn)
        {
            counts.Matched++;
        }

        if (isOwn)
        {
            // Eine Ligarunde nennt keinen Ort — die Meisterschaft bekommt deshalb keinen Pin.
            entry.Name = ExternalDirectorySource.Truncate(series, 500)!;
            entry.Federation = rounds[0].Federation;
            entry.StartDate = start;
            entry.EndDate = end;
            entry.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            entry.LastSeenAt = now;
            entry.MissedSweeps = 0;
            entry.RemovedAt = null;
            entry.ChessResultsId ??= chessResultsId;
            ExternalDirectorySource.ApplyClassification(entry);
            ApplyYouthMark(entry, rounds.Any(r => r.Youth));
        }

        counts.Delivered.Add(SeriesKey(series, entry.PublicId));
        await ExternalDirectorySource.NoteSourceAsync(_db, entry, DirectorySourceKind.CzechChessFederation,
            SeriesKey(series, entry.PublicId), null, now, ct);
        counts.Rounds += AddMissingRounds(entry, rounds);
    }

    /// <summary>
    /// Fehlende Spieltermine ergaenzen — vorhandene bleiben, wie sie sind. Ein von chess-results
    /// geholter Termin ist der genauere (er traegt auch die Uhrzeit), und ein Wettlauf zwischen
    /// zwei Quellen um dieselbe Zeile waere jede Nacht ein anderes Ergebnis.
    /// </summary>
    private int AddMissingRounds(TournamentDirectoryEntry entry, List<CrawlerChessCzEvent> rounds)
    {
        var known = entry.RoundDates.Select(r => r.Number).ToHashSet();
        var added = 0;

        foreach (var round in rounds)
        {
            if (!known.Add(round.Round!.Value)) continue;

            entry.RoundDates.Add(new TournamentDirectoryRound
            {
                TournamentDirectoryEntryId = entry.Id,
                Number = round.Round!.Value,
                Date = round.Start!.Value,
            });
            added++;
        }
        return added;
    }

    private async Task<TournamentDirectoryEntry?> FindWithRoundsAsync(
        string? chessResultsId, string publicId, CancellationToken ct)
    {
        var entry = chessResultsId is { Length: > 0 }
            ? await _db.TournamentDirectoryEntries
                .Include(e => e.RoundDates)
                .FirstOrDefaultAsync(e => e.ChessResultsId == chessResultsId && e.RemovedAt == null, ct)
            : null;

        return entry ?? await _db.TournamentDirectoryEntries
            .Include(e => e.RoundDates)
            .FirstOrDefaultAsync(e => e.PublicId == publicId, ct);
    }

    // ----- Hilfen -----------------------------------------------------------

    /// <summary>
    /// Steht der Termin in der Jugend-Lasche, ist er ein Jugendtermin — aber nur, wenn der Name
    /// nichts Genaueres sagt. „Mistrovství ČR mládeže U16" traegt seine Klasse selbst, und die
    /// ist die bessere Auskunft.
    /// </summary>
    private static void ApplyYouthMark(TournamentDirectoryEntry entry, bool youth)
    {
        if (youth && entry.AgeGroups == TournamentAgeGroups.None)
            entry.AgeGroups = TournamentAgeGroups.YouthUnspecified;
    }

    /// <summary>
    /// Aus dem bis zu 57 Zeichen langen Slug einen Schluessel machen, der in die 24 Zeichen von
    /// <see cref="TournamentDirectoryEntry.PublicId"/> passt. Gekuerzt wird NICHT: „1-ligy-1-kolo"
    /// und „1-ligy-10-kolo" unterscheiden sich am Ende, und zwei Turniere zu einem zu machen ist
    /// der teuerste Fehler, den diese Stelle machen kann.
    /// </summary>
    internal static string PublicIdOf(string slug) => "cz" + ShortHash(slug);

    /// <summary>
    /// Der Herkunftsvermerk einer Meisterschaft — sie hat keinen eigenen Slug.
    ///
    /// <para><b>Je EINTRAG, nicht je Serie.</b> Eine Meisterschaft der Quelle ist bei
    /// chess-results MEHRERE Turniere: die „2. ligy" sind die Gruppen A bis F, also sechs
    /// Eintraege, und alle bekommen dieselben Spieltermine. Ein Schluessel je Serie kann aber nur
    /// an EINEM haengen — der eindeutige Index liegt auf (Kind, ExternalId). Am 2026-09-09
    /// scheiterte die Quelle deshalb mit „Duplicate entry '9-serie:2. ligy'", sobald sie die
    /// zweite Gruppe vornahm.</para>
    ///
    /// <para>Gehasht statt zusammengesetzt: Serienname plus Kennung sprengen die 60 Zeichen der
    /// Spalte, und einen SCHLUESSEL zu kuerzen ist die schlechteste Wahl (siehe
    /// <see cref="ExternalDirectorySource.NoteSourceAsync"/>).</para>
    /// </summary>
    internal static string SeriesKey(string series, string publicId) =>
        "czs" + ShortHash($"{series}|{publicId}");

    private static string ShortHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessCzEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/chess-cz-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<ChessCzRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessCzEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Place,
                FederationOf(r.Country), r.ChessResultsId, UrlOf(r.EventId!), r.Youth,
                r.NonTournament, r.RoundNumber, r.SeriesName))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    /// <summary>
    /// Die Zeile fuehrt ihr Land als Faehnchen. Fehlt es oder ist es unbekannt, gilt Tschechien —
    /// es ist der tschechische Kalender. Ein falsch gesetztes Faehnchen wird dabei durchgereicht
    /// statt korrigiert (der European Club Cup in Herceg Novi traegt „MN", also die Mongolei):
    /// was die Quelle sagt, steht im Bestand, und ein Ratespiel darueber waere keine bessere
    /// Auskunft.
    /// </summary>
    private static string FederationOf(string? iso2) => FideCountryCodes.FromIso2(iso2) ?? "CZE";

    private static string UrlOf(string slug) => $"https://www.chess.cz/akce/{slug}/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ChessCzRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Place,
        string? Country, string? ChessResultsId, bool Youth, bool NonTournament,
        int? RoundNumber, string? SeriesName);

    public sealed record CrawlerChessCzEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place,
        string Federation, string? ChessResultsId, string? Url, bool Youth, bool NonTournament,
        int? Round, string? Series);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
