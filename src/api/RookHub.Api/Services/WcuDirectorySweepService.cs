using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Saisonkalender der Welsh Chess Union (welshchessunion.uk) als Quelle.
///
/// <para><b>Ein kleiner, aber lueckenschliessender Ertrag.</b> chess-results fuehrt fuer WLS 5
/// Eintraege im Zeitraum, dieser Kalender EIN Abruf, 38 Turniere — vom Landesmeister-Kalender
/// („Welsh Championship", „Welsh Junior Championships") bis zu Vereinsturnieren
/// (Pembrokeshire, Haverfordwest, Fishguard) und den zwei Vereinsligen WCPL/WJCPL. Kein einziger
/// Eintrag ist eine Sitzung oder ein Lehrgang — anders als beim deutschen und tschechischen
/// Verbandskalender ist hier nichts vorab auszusortieren.</para>
///
/// <para><b>Die Foederation ist FEST</b> — anders als beim deutschen Verbandskalender (der auch
/// Oesterreich, Fernschach und ein „Europa"/„Welt"-Sammelbecken traegt) ist diese Seite
/// ausschliesslich der walisische Saisonkalender. Jeder gelesene Eintrag bekommt <c>"WLS"</c>.
/// <see cref="FideCountryCodes"/> bildet <c>WLS</c> auf <c>GB</c> ab (zusammen mit ENG und SCO —
/// mehrere Foederationen zeigen bewusst auf dasselbe Land); das Ortslexikon fuehrt fuer
/// Grossbritannien nachgemessen NUR Staedte, KEINE Postleitzahlen. Eine gelieferte britische
/// Postleitzahl (30 von 38 Eintraegen tragen eine) wird trotzdem in den Ortstext uebernommen —
/// ein spaeterer GB-Postleitzahl-Import macht sie nutzbar, verlassen kann sich diese Quelle
/// heute aber nur auf die Ortsnamen-Aufloesung.</para>
///
/// <para><b>Diese Quelle hat KEINE stabile Kennung</b> — keine Nummer, kein URL-Slug, keine
/// Detailseite je Turnier (siehe <see cref="WcuCalendarService"/> im Crawler). Die dortige
/// <c>EventId</c> ist bereits der aus Termin und Anschrift gebildete Schluessel; hier wird nur
/// noch der Kurzwert daraus gemacht, wie bei den uebrigen Quellen ohne eigene Nummer
/// (<see cref="SchachbundDirectorySweepService.PublicIdOf"/>,
/// <see cref="ChessCzDirectorySweepService.PublicIdOf"/>).</para>
/// </summary>
public class WcuDirectorySweepService
{
    /// <summary>Die einzige Foederation, die diese Quelle je liefert.</summary>
    internal const string Federation = "WLS";

    private const int SaveEvery = 25;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<WcuDirectorySweepService> _log;

    public WcuDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<WcuDirectorySweepService> log)
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
        int added = 0, matched = 0, retired = 0, processed = 0;

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = PublicIdOf(row.EventId);
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, Federation, start, row.Name, ct);

                if (match is not null)
                {
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.WelshChessUnion, PublicIdOf(row.EventId), row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("wcu: {PublicId} geht in {Target} auf ({Name})",
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
                own.Federation = Federation;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.WelshChessUnion, PublicIdOf(row.EventId), row.Url, now, ct);

                if (location is { Length: > 0 } && (locationChanged || own.Lat is null))
                {
                    var hit = await _geocoding.ResolveAsync(location, null, Federation, ct);
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
            catch (Exception ex) { _log.LogWarning(ex, "wcu: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "WCU-Saisonkalender: {Read} gelesen, {Added} neu, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, matched, retired);
        return new ExternalSweepResult(events.Count, added, 0, matched, retired);
    }

    /// <summary>
    /// Ob der Ortstext ein SPIELORT ist. Ein Turnier, dessen Ausschreibung noch nicht steht,
    /// nennt statt einer Anschrift einen Platzhalter ("(More details to follow)", "TBC") — der
    /// bekommt keinen Pin.
    /// </summary>
    internal static bool HasVenue(string? place)
    {
        if (place is not { Length: > 0 }) return false;

        var value = place.Trim();
        return !value.Contains("more details", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("tbc", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("tba", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Diese Quelle hat keine Nummer und keinen Slug — die Kennung des Crawlers ist bereits ein
    /// aus Termin und Anschrift gebildeter Schluessel (siehe <c>WcuCalendarService.EventKeyOf</c>
    /// im Crawler). Hier wird daraus nur noch der Kurzwert fuer die 24 Zeichen von
    /// <see cref="TournamentDirectoryEntry.PublicId"/>.
    /// <para>Derselbe Kurzwert ist auch die KENNUNG im Herkunftsvermerk. Der Schluessel der
    /// Quelle ist eine Zeile aus Termin und Anschrift und damit laenger als die 60 Zeichen von
    /// <see cref="Models.TournamentDirectorySource.ExternalId"/> — die Quelle scheiterte deshalb
    /// jede Nacht an „Data too long for column" (2026-09-09 gefunden, 0 Eintraege im Bestand).</para>
    /// </summary>
    internal static string PublicIdOf(string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim().ToLowerInvariant()));
        return "wl" + Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerWcuEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/wcu-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<WcuRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerWcuEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Place, r.Url))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record WcuRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Place, string? Url);

    public sealed record CrawlerWcuEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place, string? Url);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
