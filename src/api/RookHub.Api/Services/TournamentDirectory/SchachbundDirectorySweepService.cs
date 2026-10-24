using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RookHub.Api.Services.Tournaments;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Die Turnierdatenbank des Deutschen Schachbunds (schachbund.de) als Quelle.
///
/// <para><b>Ihr Wert ist nicht die Menge, sondern die ART der Turniere.</b> Deutschland ist ueber
/// chess-results teilweise abgedeckt; die Frage war nie „gibt es Turniere", sondern „gibt es
/// welche, die dort fehlen". Diese Datenbank ist ein reines MELDE-System — der Veranstalter
/// traegt seinen Termin selbst ein, ohne Swiss-Manager und ohne Ergebnismeldung. Deshalb stehen
/// hier Vereins-Abendturniere, Jugend-Cups, <b>Fernschach</b>, <b>Problemschach</b>, Online und
/// Schach960: Kategorien, die chess-results praktisch nie fuehrt. Rund 104 kuenftige Eintraege
/// ueber 25 Regionen.</para>
///
/// <para><b>Die REGION sagt, in welchem Land das Turnier liegt</b> — und zwar nur ungefaehr. Die
/// Bundeslaender bedeuten Deutschland, „oesterreich" Oesterreich, aber „europa" und „welt"
/// bedeuten gar nichts Bestimmtes (dort standen zuletzt Kreta, Lettland, Suedtirol und ein
/// Kreuzfahrtschiff). Diese bekommen deshalb KEINE Foederation: eine falsche waere schlechter als
/// keine — sie landete im Laenderfilter unter Deutschland und im Geocoder mit dem falschen
/// Lexikon.</para>
///
/// <para><b>Fernschach hat keinen Spielort</b>, und „Online" oder „BdF-Server" im Ortsfeld ist
/// keiner. Solche Eintraege bekommen keinen Pin.</para>
/// </summary>
public class SchachbundDirectorySweepService : IDirectorySourceAdapter<SchachbundDirectorySweepService.CrawlerSchachbundEvent>
{

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<SchachbundDirectorySweepService> _log;

    public SchachbundDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<SchachbundDirectorySweepService> log)
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
        return await ExternalSweepRunner.RunAsync(_db, _log, this, events, ct);
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<CrawlerSchachbundEvent>.Kind => DirectorySourceKind.GermanChessFederation;
    string IDirectorySourceAdapter<CrawlerSchachbundEvent>.LogName => "schachbund";
    string? IDirectorySourceAdapter<CrawlerSchachbundEvent>.SourceKey(CrawlerSchachbundEvent row) => row.EventId.Length == 0 ? null : PublicIdOf(row.EventId);
    DateOnly? IDirectorySourceAdapter<CrawlerSchachbundEvent>.Start(CrawlerSchachbundEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerSchachbundEvent>.Name(CrawlerSchachbundEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerSchachbundEvent>.PublicId(CrawlerSchachbundEvent row) => PublicIdOf(row.EventId);
    string? IDirectorySourceAdapter<CrawlerSchachbundEvent>.Federation(CrawlerSchachbundEvent row) => FederationOf(row.Region);
    string? IDirectorySourceAdapter<CrawlerSchachbundEvent>.MatchPlace(CrawlerSchachbundEvent row) => row.Place;
    string? IDirectorySourceAdapter<CrawlerSchachbundEvent>.Url(CrawlerSchachbundEvent row) => row.Url;
    bool IDirectorySourceAdapter<CrawlerSchachbundEvent>.FillMatch(TournamentDirectoryEntry match, CrawlerSchachbundEvent row) =>
        ExternalSweepRunner.FillTimeControlGaps(match, row.TimeControl, row.Rounds);

    async Task<bool> IDirectorySourceAdapter<CrawlerSchachbundEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerSchachbundEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        var federation = FederationOf(row.Region);
        var location = HasVenue(row.Place) ? row.Place!.Trim() : null;
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.Federation = federation;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        own.TimeControlText = ExternalDirectorySource.Truncate(row.TimeControl, 300);
        own.Rounds = row.Rounds;
        own.Speed = TournamentSpeedClassifier.Classify(row.TimeControl);
        own.System = row.System switch
        {
            "swiss" => TournamentSystem.Swiss,
            "roundRobin" => TournamentSystem.RoundRobin,
            _ => TournamentSystem.Unknown,
        };
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.GermanChessFederation, PublicIdOf(row.EventId), row.Url, now, ct);

        // „Ergaenzt" zaehlt nur an chess-results-Eintraegen (FillMatch).
        await ExternalSweepRunner.LocateAsync(_geocoding, own, location, null, federation, locationChanged, ct);
        return false;
    }

    void IDirectorySourceAdapter<CrawlerSchachbundEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "schachbund-Turnierdatenbank: {Read} gelesen, {Added} neu, {Updated} ergaenzt, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);


    /// <summary>
    /// Die Foederation aus der Region. „europa" und „welt" bekommen KEINE — dort standen zuletzt
    /// Kreta, Lettland, Suedtirol und ein Kreuzfahrtschiff, und „Deutschland" waere fuer jedes
    /// davon falsch. Ebenso die Sonderkategorien Fernschach und Online: sie sagen nichts ueber
    /// ein Land.
    /// </summary>
    internal static string? FederationOf(string? region) => region?.Trim().ToLowerInvariant() switch
    {
        "oesterreich" => "AUT",
        "europa" or "welt" or "fernschachbund" or "onlineschach" => null,
        null or "" => "GER",
        _ => "GER",
    };

    /// <summary>
    /// Ob der Ortstext ein SPIELORT ist. Fernschach und Online nennen dort ihren Server
    /// („BdF-Server", „Online") — das ist kein Ort und darf keinen Pin bekommen.
    /// </summary>
    internal static bool HasVenue(string? place)
    {
        if (place is not { Length: > 0 }) return false;

        var value = place.Trim();
        return !value.StartsWith("online", StringComparison.OrdinalIgnoreCase)
               && !value.Contains("server", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("internet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Diese Quelle hat keine Nummer — ihre Kennung ist der Adressbestandteil der Detailseite
    /// („ccm-monatliches-rapidturnier-10-september-2026-12-3", 51 Zeichen). Die Spalte fasst 24,
    /// also ein Kurzwert; der lesbare Slug steht vollstaendig im Herkunftsvermerk.
    /// <para>Derselbe Kurzwert ist auch die KENNUNG im Herkunftsvermerk — der Slug der Quelle ist
    /// laenger als die 60 Zeichen von <see cref="Models.TournamentDirectorySource.ExternalId"/>, und
    /// die Quelle warf deshalb jede Nacht nach 283 s hoeflichen Crawlens den ganzen Durchgang weg
    /// (2026-09-09 gefunden, 0 Eintraege im Bestand).</para>
    /// </summary>
    internal static string PublicIdOf(string slug)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(slug.Trim().ToLowerInvariant()));
        return "de" + Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerSchachbundEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<SchachbundRow>>(
            $"/api/schachbund-calendar?from={from:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerSchachbundEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Region,
                r.Place, r.TimeControl, r.Rounds, r.System, r.Url))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    private sealed record SchachbundRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Region,
        string? Place, string? TimeControl, int? Rounds, string? System, string? Url);

    public sealed record CrawlerSchachbundEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Region, string? Place,
        string? TimeControl, int? Rounds, string? System, string? Url);
}
