using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using RookHub.Api.Services.Tournaments;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender des polnischen Verbands (chessarbiter.com) als Quelle.
///
/// <para><b>Die ergiebigste Einzelquelle des Projekts.</b> 611 kuenftige Turniere in EINEM Abruf —
/// mehr als alle uebrigen Verbandskalender zusammen, und Polen ist im chess-results-Bestand
/// duenn vertreten.</para>
///
/// <para><b>Zweistufig, wie der chess-results-Sweep selbst.</b> Die Liste kostet einen Abruf und
/// bringt Name, Termin, Ort, Woiwodschaft und Bedenkzeit-Klasse. Enddatum, Bedenkzeit,
/// Rundenzahl, System und Teilnehmerzahl stehen auf der Detailseite — ein Abruf je Turnier,
/// deshalb <b>nur fuer die, die wir noch nicht kennen</b>, gedeckelt und mit Pause. Bei 611
/// Turnieren und einem Deckel von 150 ist der Bestand nach vier Naechten vollstaendig; danach
/// kostet er nur noch, was wirklich neu ist.</para>
///
/// <para><b>Woran „schon geholt" haengt.</b> Es gibt keine Spalte dafuer, und es braucht auch
/// keine: der Herkunftsvermerk (<see cref="TournamentDirectorySource"/>) bekommt seine
/// <c>Url</c> erst, wenn die Detailseite gelesen wurde. Ein Vermerk ohne Adresse heisst also „nur
/// aus der Liste". Das ist keine Ersatzloesung, sondern genau das, was die Spalte aussagt — wir
/// tragen die Seite ein, die wir wirklich abgerufen haben.</para>
///
/// <para><b>Die Teilnehmerzahl ist eine Besonderheit.</b> Diese Quelle nennt sie auch fuer
/// GEPLANTE Turniere; chess-results und FIDE tun das grundsaetzlich nicht (dort steht sie erst
/// nach dem Turnier). Der Filter „mindestens N Teilnehmer" wirkt fuer polnische Turniere damit
/// als einziger im Bestand schon vor dem Termin.</para>
/// </summary>
/// <summary>Wie eine Detail-Abfrage ausgegangen ist — Gegenstueck zum gleichnamigen Typ im
/// Crawler. „Geholt, keine Angaben" ist endgueltig und wird vermerkt, „nicht zu holen" nicht.</summary>
internal enum ChessArbiterDetailOutcome
{
    Parsed,
    Empty,
    Unavailable,
}

public class ChessArbiterDirectorySweepService : IDirectorySourceAdapter<ChessArbiterDirectorySweepService.CrawlerChessArbiterEvent>
{
    /// <summary>Fassung des Detailabrufs — erhoehen, wenn <c>ParseDetail</c> im Crawler mehr oder
    /// anderes liest. Eintraege mit kleinerer Fassung werden genau einmal nachgefragt.</summary>
    public const int CurrentDetailVersion = 1;

    /// <summary>
    /// Pause zwischen zwei Detailabrufen. Die Quelle laeuft auf sichtbar alter Infrastruktur
    /// (PHP 5.2) — 150 Anfragen am Stueck waeren unhoeflich, auch wenn nichts sie verbietet.
    /// </summary>
    private static readonly TimeSpan DetailDelay = TimeSpan.FromMilliseconds(600);

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly int _detailBatchSize;
    private readonly ILogger<ChessArbiterDirectorySweepService> _log;

    /// <summary>Wie viele Detailseiten dieser Lauf noch holen darf.</summary>
    private int _detailBudget;

    public ChessArbiterDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, IConfiguration configuration,
        ILogger<ChessArbiterDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _detailBatchSize = configuration.GetValue("TournamentDirectory:ChessArbiterDetailBatchSize", 150);
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(int? detailLimit = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchListAsync(today, ct);
        _detailBudget = Math.Max(0, detailLimit ?? _detailBatchSize);
        return await ExternalSweepRunner.RunAsync(_db, _log, this, events, ct);
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<CrawlerChessArbiterEvent>.Kind => DirectorySourceKind.PolishChessFederation;
    string IDirectorySourceAdapter<CrawlerChessArbiterEvent>.LogName => "chessarbiter";

    /// <summary>Jahr und Nummer — <see cref="CrawlerChessArbiterEvent.Key"/>; leer, wenn die Nummer fehlt.</summary>
    string? IDirectorySourceAdapter<CrawlerChessArbiterEvent>.SourceKey(CrawlerChessArbiterEvent row) => row.EventId.Length == 0 ? null : row.Key;

    DateOnly? IDirectorySourceAdapter<CrawlerChessArbiterEvent>.Start(CrawlerChessArbiterEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerChessArbiterEvent>.Name(CrawlerChessArbiterEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerChessArbiterEvent>.PublicId(CrawlerChessArbiterEvent row) => $"pl{row.Year}-{row.EventId}";
    string? IDirectorySourceAdapter<CrawlerChessArbiterEvent>.Federation(CrawlerChessArbiterEvent row) => "POL";
    string? IDirectorySourceAdapter<CrawlerChessArbiterEvent>.MatchPlace(CrawlerChessArbiterEvent row) => row.Place;
    string? IDirectorySourceAdapter<CrawlerChessArbiterEvent>.Url(CrawlerChessArbiterEvent row) => row.Url;

    async Task<bool> IDirectorySourceAdapter<CrawlerChessArbiterEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerChessArbiterEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        var location = row.Place?.Trim();
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.StartDate = start;
        // Ein schon geholtes Ende bleibt stehen — es kam von der Detailseite. (Ein neuer Eintrag
        // hat noch kein Ende; vorher stand hier zusaetzlich „und nicht neu", was dasselbe sagt.)
        own.EndDate = own.EndDate is { } end && end > start ? end : start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.State = ExternalDirectorySource.Truncate(row.Region, 100);
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        own.Speed = SpeedOf(row.SpeedText);
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);

        // Der Detailabruf lohnt nur einmal je Turnier — und „schon gefragt" heisst hier
        // ausdruecklich AUCH „gefragt, die Seite traegt nichts". Vorher stand das nur in
        // der Adresse des Herkunftsvermerks, und die wird ausschliesslich bei ERFOLG
        // gesetzt: die rund 480 polnischen Turniere ohne Datenseite blieben damit fuer
        // immer Kandidat und verbrauchten jede Nacht das ganze Budget an sich selbst.
        var hasDetail = HasDetail(own, row.Key);
        // Traegt der Vermerk schon eine Adresse, wurde die Seite frueher GELESEN (nicht
        // bloss gefragt) — dann bleibt sie stehen.
        var detailRead = HasReadDetail(own, row.Key);
        // „Aktualisiert" heisst hier: die Detailseite war lesbar.
        var parsed = false;
        if (!hasDetail && _detailBudget > 0)
        {
            _detailBudget--;
            var outcome = await LoadDetailAsync(own, row, ct);
            if (outcome != ChessArbiterDetailOutcome.Unavailable)
            {
                // Auch die leere Seite ist eine Auskunft: Fassung setzen, nicht wiederholen.
                own.ChessArbiterDetailVersion = CurrentDetailVersion;
            }
            if (outcome == ChessArbiterDetailOutcome.Parsed)
            {
                parsed = true;
                detailRead = true;
                locationChanged = true;   // die Anschrift ist genauer als der Listenort
            }
            await Task.Delay(DetailDelay, ct);
        }

        // Die Adresse im Vermerk bleibt der Herkunftsbeleg — sie steht nur da, wenn die
        // Seite auch etwas hergab (`ChessArbiterDetailVersion` sagt, dass gefragt wurde).
        await ExternalDirectorySource.NoteSourceAsync(_db, own, DirectorySourceKind.PolishChessFederation,
            row.Key, detailRead ? row.Url : null, now, ct);

        if (own.LocationText is { Length: > 0 } && (locationChanged || own.Lat is null))
        {
            var hit = await _geocoding.ResolveAsync(own.LocationText, own.State, "POL", ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                own.Lat = hit.Lat;
                own.Lon = hit.Lon;
                own.GeoSource = hit.Source;
                own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
        return parsed;
    }

    void IDirectorySourceAdapter<CrawlerChessArbiterEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "chessarbiter-Kalender: {Read} gelesen, {Added} neu, {Updated} mit Detailseite, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);

    /// <summary>Wurde die Detailseite dieses Turniers schon mit der AKTUELLEN Fassung gefragt?
    /// Die Adresse im Herkunftsvermerk zaehlt weiter mit — Eintraege aus der Zeit vor der Spalte
    /// haben sie, und die sollen nicht alle noch einmal gefragt werden.</summary>
    internal static bool HasDetail(TournamentDirectoryEntry entry, string externalId) =>
        entry.ChessArbiterDetailVersion >= CurrentDetailVersion || HasReadDetail(entry, externalId);

    /// <summary>Wurde die Detailseite je mit ERGEBNIS gelesen? Die Adresse im Herkunftsvermerk ist
    /// der Beleg; sie wird nur bei Erfolg gesetzt und ist zugleich die Bruecke fuer den Altbestand
    /// aus der Zeit vor <see cref="TournamentDirectoryEntry.ChessArbiterDetailVersion"/>.</summary>
    internal static bool HasReadDetail(TournamentDirectoryEntry entry, string externalId) =>
        entry.Sources.Any(s => s.Kind == DirectorySourceKind.PolishChessFederation
                               && s.ExternalId == externalId
                               && s.Url is { Length: > 0 });

    private async Task<ChessArbiterDetailOutcome> LoadDetailAsync(
        TournamentDirectoryEntry entry, CrawlerChessArbiterEvent row, CancellationToken ct)
    {
        var (outcome, detail) = await FetchDetailAsync(row.Year, row.EventId, ct);
        if (detail is null) return outcome;

        // Das Detail-Startdatum ist das genauere: die Liste nennt kein Jahr, es wird aus der
        // Reihenfolge erschlossen. Weicht es ab, gilt die Seite.
        if (detail.Start is { } start) entry.StartDate = start;
        if (detail.End is { } end && entry.StartDate is { } from && end >= from) entry.EndDate = end;
        if (entry.StartDate is { } begin)
            entry.StartsOnWeekend = begin.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        if (detail.Place is { Length: > 0 })
            entry.LocationText = ExternalDirectorySource.Truncate(detail.Place, 300);
        if (detail.TimeControl is { Length: > 0 })
            entry.TimeControlText = ExternalDirectorySource.Truncate(detail.TimeControl, 300);
        if (detail.Rounds is > 0) entry.Rounds = detail.Rounds;
        if (detail.PlayerCount is > 0) entry.PlayerCount = detail.PlayerCount;

        entry.System = detail.System switch
        {
            "swiss" => TournamentSystem.Swiss,
            "roundRobin" => TournamentSystem.RoundRobin,
            _ => entry.System,
        };

        // Die Bedenkzeit-KLASSE der Liste ist grob („klasyczne"); der Rohtext ist genauer.
        if (entry.Speed == TournamentSpeed.Unknown && detail.TimeControl is { Length: > 0 })
            entry.Speed = TournamentSpeedClassifier.Classify(detail.TimeControl);

        ExternalDirectorySource.ApplyClassification(entry);
        return ChessArbiterDetailOutcome.Parsed;
    }

    /// <summary>
    /// Die Bedenkzeit-Klasse der Quelle. „inne" heisst „anderes" und ist keine Klasse —
    /// <see cref="TournamentSpeed.Unknown"/> ist dafuer die richtige Antwort.
    /// </summary>
    internal static TournamentSpeed SpeedOf(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "klasyczne" or "klasyczne fide" or "klasyczne pzszach" => TournamentSpeed.Standard,
        "szybkie" => TournamentSpeed.Rapid,
        "blitz" or "błyskawiczne" or "blyskawiczne" => TournamentSpeed.Blitz,
        _ => TournamentSpeed.Unknown,
    };

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessArbiterEvent>> FetchListAsync(DateOnly today, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<ChessArbiterRow>>(
            $"/api/chess-arbiter-calendar?today={today:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Year is { Length: > 0 }
                        && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessArbiterEvent(
                r.Year!, r.EventId!, r.Name!, ParseDate(r.StartDate), r.Place, r.Region,
                r.SpeedText, r.Url))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    /// <summary>
    /// Die Detailseite EINES Turniers. Ein Fehlschlag ist kein Grund, den Durchgang abzubrechen —
    /// der Eintrag steht dann eben nur mit dem, was die Liste hergibt, und wird beim naechsten
    /// Durchgang erneut versucht.
    ///
    /// <para><b>204 ist nicht 404.</b> Der Crawler antwortet mit 204, wenn er die Seite geholt hat
    /// und sie keine Angaben traegt — eine endgueltige Auskunft, die der Aufrufer sich merken darf.
    /// 404 heisst „nicht zu holen" und wird wiederholt. Vorher war beides <c>null</c>, und die rund
    /// 480 polnischen Turniere ohne Datenseite wurden jede Nacht erneut gefragt.</para>
    /// </summary>
    private async Task<(ChessArbiterDetailOutcome Outcome, CrawlerChessArbiterDetail? Detail)>
        FetchDetailAsync(string year, string id, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
            using var response = await client.GetAsync(
                $"/api/chess-arbiter-calendar/detail?year={Uri.EscapeDataString(year)}" +
                $"&id={Uri.EscapeDataString(id)}", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return (ChessArbiterDetailOutcome.Empty, null);
            if (!response.IsSuccessStatusCode)
                return (ChessArbiterDetailOutcome.Unavailable, null);

            var body = await response.Content.ReadAsStringAsync(ct);
            var row = JsonSerializer.Deserialize<ChessArbiterDetailRow>(body, DirectoryCrawlerClient.JsonOptions);
            // Leerer Rumpf trotz 200: wie 204 zu behandeln waere geraten — der Crawler sagt es
            // ausdruecklich, alles andere ist eine unerwartete Antwort und wird wiederholt.
            return row is null
                ? (ChessArbiterDetailOutcome.Unavailable, null)
                : (ChessArbiterDetailOutcome.Parsed,
                   new CrawlerChessArbiterDetail(ParseDate(row.StartDate), ParseDate(row.EndDate),
                       row.Place, row.TimeControl, row.Rounds, row.System, row.PlayerCount));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "chessarbiter: Detailseite {Year}/{Id} nicht lesbar", year, id);
            return (ChessArbiterDetailOutcome.Unavailable, null);
        }
    }

    private sealed record ChessArbiterRow(
        string? Year, string? EventId, string? Name, string? StartDate, string? Place,
        string? Region, string? SpeedText, string? Url);

    private sealed record ChessArbiterDetailRow(
        string? StartDate, string? EndDate, string? Place, string? TimeControl, int? Rounds,
        string? System, int? PlayerCount, string? Organizer);

    public sealed record CrawlerChessArbiterEvent(
        string Year, string EventId, string Name, DateOnly? Start, string? Place, string? Region,
        string? SpeedText, string? Url)
    {
        /// <summary>Jahr und Nummer bilden die Kennung bei der Quelle — „2026/ti_291".</summary>
        public string Key => $"{Year}/ti_{EventId}";
    }

    public sealed record CrawlerChessArbiterDetail(
        DateOnly? Start, DateOnly? End, string? Place, string? TimeControl, int? Rounds,
        string? System, int? PlayerCount);
}
