using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

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
public class ChessArbiterDirectorySweepService
{
    private const int SaveEvery = 25;

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
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var budget = Math.Max(0, detailLimit ?? _detailBatchSize);
        var now = DateTime.UtcNow;
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = $"pl{row.Year}-{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, "POL", start, row.Name, ct);

                if (match is not null)
                {
                    ExternalDirectorySource.NoteSource(match,
                        DirectorySourceKind.PolishChessFederation, row.Key, row.Url, now);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("chessarbiter: {PublicId} geht in {Target} auf ({Name})",
                            own!.PublicId, match.PublicId, match.Name);
                    }
                    if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
                    continue;
                }

                var isNew = own is null;
                if (own is null)
                {
                    own = new TournamentDirectoryEntry
                    {
                        PublicId = publicId,
                        ChessResultsId = null,
                        Federation = "POL",
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                var location = row.Place?.Trim();
                var locationChanged = own.LocationText != location;

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.StartDate = start;
                own.EndDate = own.EndDate is { } end && end > start && !isNew ? end : start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.State = ExternalDirectorySource.Truncate(row.Region, 100);
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.Speed = SpeedOf(row.SpeedText);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);

                // Der Detailabruf lohnt nur einmal je Turnier. „Schon geholt" steht in der
                // Adresse des Herkunftsvermerks — sie wird erst dabei gesetzt.
                var hasDetail = HasDetail(own, row.Key);
                if (!hasDetail && budget > 0)
                {
                    budget--;
                    if (await LoadDetailAsync(own, row, ct))
                    {
                        updated++;
                        hasDetail = true;
                        locationChanged = true;   // die Anschrift ist genauer als der Listenort
                    }
                    await Task.Delay(DetailDelay, ct);
                }

                ExternalDirectorySource.NoteSource(own, DirectorySourceKind.PolishChessFederation,
                    row.Key, hasDetail ? row.Url : null, now);

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

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "chessarbiter: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "chessarbiter-Kalender: {Read} gelesen, {Added} neu, {Updated} mit Detailseite, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

    /// <summary>
    /// Ob die Detailseite dieses Turniers schon gelesen wurde — erkennbar an der Adresse im
    /// Herkunftsvermerk.
    /// </summary>
    internal static bool HasDetail(TournamentDirectoryEntry entry, string externalId) =>
        entry.Sources.Any(s => s.Kind == DirectorySourceKind.PolishChessFederation
                               && s.ExternalId == externalId
                               && s.Url is { Length: > 0 });

    private async Task<bool> LoadDetailAsync(
        TournamentDirectoryEntry entry, CrawlerChessArbiterEvent row, CancellationToken ct)
    {
        var detail = await FetchDetailAsync(row.Year, row.EventId, ct);
        if (detail is null) return false;

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
        return true;
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
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/chess-arbiter-calendar?today={today:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<ChessArbiterRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Year is { Length: > 0 }
                        && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessArbiterEvent(
                r.Year!, r.EventId!, r.Name!, ParseDate(r.StartDate), r.Place, r.Region,
                r.SpeedText, r.Url))
            .Where(e => e.Start is not null)
            .ToList();
    }

    /// <summary>
    /// Die Detailseite EINES Turniers. Ein Fehlschlag ist kein Grund, den Durchgang abzubrechen —
    /// der Eintrag steht dann eben nur mit dem, was die Liste hergibt, und wird beim naechsten
    /// Durchgang erneut versucht.
    /// </summary>
    private async Task<CrawlerChessArbiterDetail?> FetchDetailAsync(
        string year, string id, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
            using var response = await client.GetAsync(
                $"/api/chess-arbiter-calendar/detail?year={Uri.EscapeDataString(year)}" +
                $"&id={Uri.EscapeDataString(id)}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            var row = JsonSerializer.Deserialize<ChessArbiterDetailRow>(body, JsonOptions);
            return row is null
                ? null
                : new CrawlerChessArbiterDetail(ParseDate(row.StartDate), ParseDate(row.EndDate),
                    row.Place, row.TimeControl, row.Rounds, row.System, row.PlayerCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "chessarbiter: Detailseite {Year}/{Id} nicht lesbar", year, id);
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
