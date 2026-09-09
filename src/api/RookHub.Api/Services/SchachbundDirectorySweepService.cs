using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

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
public class SchachbundDirectorySweepService
{
    private const int SaveEvery = 25;

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
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

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
                var publicId = PublicIdOf(row.EventId);
                var federation = FederationOf(row.Region);
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, federation, start, row.Name, ct);

                if (match is not null)
                {
                    var text = match.TimeControlText;
                    var rounds = match.Rounds;
                    var changed = ExternalDirectorySource.FillIfEmpty(row.TimeControl, ref text, 300);
                    changed |= ExternalDirectorySource.FillIfEmpty(row.Rounds, ref rounds);
                    match.TimeControlText = text;
                    match.Rounds = rounds;
                    if (changed && match.Speed == TournamentSpeed.Unknown)
                        match.Speed = TournamentSpeedClassifier.Classify(match.TimeControlText);

                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.GermanChessFederation, PublicIdOf(row.EventId), row.Url, now, ct);
                    matched++;
                    if (changed) updated++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("schachbund: {PublicId} geht in {Target} auf ({Name})",
                            own!.PublicId, match.PublicId, match.Name);
                    }
                    if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
                    continue;
                }

                if (own is null)
                {
                    own = new TournamentDirectoryEntry
                    {
                        PublicId = publicId,
                        ChessResultsId = null,
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

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
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.GermanChessFederation, PublicIdOf(row.EventId), row.Url, now, ct);

                if (location is { Length: > 0 } && (locationChanged || own.Lat is null))
                {
                    var hit = await _geocoding.ResolveAsync(location, null, federation, ct);
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
            catch (Exception ex) { _log.LogWarning(ex, "schachbund: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "schachbund-Turnierdatenbank: {Read} gelesen, {Added} neu, {Updated} ergaenzt, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

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
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/schachbund-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<SchachbundRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerSchachbundEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Region,
                r.Place, r.TimeControl, r.Rounds, r.System, r.Url))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SchachbundRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Region,
        string? Place, string? TimeControl, int? Rounds, string? System, string? Url);

    public sealed record CrawlerSchachbundEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Region, string? Place,
        string? TimeControl, int? Rounds, string? System, string? Url);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
