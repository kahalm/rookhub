using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Kalender von Chess Scotland (chessscotland.com) als Quelle.
///
/// <para><b>Warum diese Quelle.</b> Am 2026-09-09 gemessen: 43-44 kuenftige Turniere in EINER
/// Seite bis 2028, gegen 8 auf chess-results fuer SCO im selben Zeitraum — rund 36 zusaetzliche,
/// ueberwiegend kleinere Vereins- und Jugendturniere (Allegro-/Blitz-Abende, Schulligen).</para>
///
/// <para><b>Die Bedenkzeit-KLASSE steht strukturiert dabei — der Sonderwert dieser Quelle.</b> Bei
/// jeder anderen Quelle des Projekts muss sie aus Freitext erschlossen werden
/// (<see cref="TournamentSpeedClassifier"/>); hier liefert die Zeile sie als Schlagwort:
/// „Standard", „Allegro" (FIDE-Sprache fuer Schnellschach/Rapid), „Blitz", dazu „Fide" als reiner
/// Wertungs-Vermerk ohne eigene Klasse (siehe <see cref="SpeedOf"/>). Ein Kongress kann mehrere
/// gleichzeitig anbieten — die ERNSTHAFTESTE Klasse gilt als die des Turniers.</para>
///
/// <para><b>„Junior" ist nur OHNE „Adult" eine Jugend-Angabe.</b> 25 von 43 gemessenen Terminen
/// tragen BEIDE Schlagworte zugleich (ein Kongress mit Erwachsenen- UND Jugendabteilung), nur 16
/// tragen ausschliesslich „Junior". Ein Pin auf „Nachwuchs" bei einem gemischten Kongress waere
/// falsch — siehe <see cref="ApplyYouthMark"/>.</para>
///
/// <para><b>Kein Spielort in der Liste — und nur mit Vorbehalt auf der Detailseite.</b> Die
/// Ausschreibung liegt dort als freier Rich-Text-Block ohne strukturierte Adressfelder. Gemessen
/// an allen 43 damals kuenftigen Detailseiten: 8 (19 %) enthalten eine erkennbare britische
/// Postleitzahl irgendwo im Text (der Crawler liest NUR diese eine Zeile, siehe
/// <c>ChessScotlandCalendarService.VenueOf</c>), 35 nicht. Der Detailabruf lohnt trotzdem: ohne
/// Postleitzahl-Bestand fuer Grossbritannien im Ortslexikon (<c>FideCountryCodes.ToIso2("SCO")</c>
/// bildet auf <c>GB</c> ab, siehe dort) traegt die gefundene Zeile ueberwiegend einen ECHTEN
/// Ortsnamen VOR der Postleitzahl („Dalblair Road, Ayr. KA7 1UG") — die normale
/// <see cref="GeocodingService"/>-Aufloesung ueber den Staedtenamen greift also unveraendert.</para>
///
/// <para><b>Der Detailabruf ist EINMALIG je Turnier</b> — wie bei sjakk.no wird eine gelesene Seite
/// nicht erneut geholt, AUCH WENN sie keinen Spielort ergab (<see cref="HasDetail"/> haengt nur am
/// Herkunftsvermerk-Link, nicht am Ergebnis). Ein bekannter, hingenommener Nachteil: schreibt ein
/// Veranstalter die Anschrift erst SPAETER in eine heute noch leere Ausschreibung („More details
/// to follow" — der haeufigste Fall bei weit vorausliegenden Terminen bis 2028), wird das nicht
/// nachgeholt. Dasselbe Verhalten hat die bestehende Norwegen-Quelle bereits; ein Aendern dieser
/// Regel ist bewusst NICHT Teil dieser Anbindung.</para>
///
/// <para><b>Rechtslage (2026-09-09 geprueft).</b> <c>/robots.txt</c> antwortet 404 (RFC 9309: keine
/// Einschraenkung), keine Nutzungsbedingungen gefunden.</para>
/// </summary>
public class ChessScotlandDirectorySweepService
{
    private const int SaveEvery = 25;

    /// <summary>Wartezeit vor einem Detailabruf. Die Quelle nennt keine — Selbstbeschraenkung.</summary>
    private static readonly TimeSpan DetailDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Die Foederation ist fest: die Quelle ist der Kalender EINES Verbands.</summary>
    internal const string Federation = "SCO";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<ChessScotlandDirectorySweepService> _log;
    private readonly int _detailBatchSize;

    public ChessScotlandDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, IConfiguration configuration,
        ILogger<ChessScotlandDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
        // 50 deckt den ganzen heutigen Bestand (43-44) in einem Durchgang ab; wie bei sjakk.no
        // ist der Deckel trotzdem konfigurierbar, falls die Quelle waechst.
        _detailBatchSize = configuration.GetValue("TournamentDirectory:ChessScotlandDetailBatchSize", 50);
    }

    public async Task<ExternalSweepResult> RunAsync(int? detailLimit = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, ct);
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        var budget = Math.Max(0, detailLimit ?? _detailBatchSize);
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.Slug.Length == 0)
                    continue;

                processed++;
                var publicId = PublicIdOf(row.Slug);
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, Federation, start, row.Name, ct);

                // Hat die Turniersuche dieselbe Veranstaltung inzwischen? Dann gehoert ihr der
                // Eintrag — hier wird nur der Herkunftsvermerk gesetzt.
                if (match is not null)
                {
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.ScottishChessFederation, row.Slug, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("Chess Scotland: {PublicId} geht in {Target} auf ({Name})",
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

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.Federation = Federation;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);
                ApplyYouthMark(own, row.Categories);

                own.TimeControlText = ExternalDirectorySource.Truncate(TimeControlTextOf(row.TimeControls), 300);
                if (own.Speed == TournamentSpeed.Unknown)
                    own.Speed = SpeedOf(row.TimeControls);

                // Ein Online-Turnier hat keinen Spielort — kein Detailabruf, kein Pin.
                var isOnline = Has(row.Categories, "Online");

                var hasDetail = HasDetail(own, row.Slug);
                if (!isOnline && !hasDetail && budget > 0)
                {
                    budget--;
                    if (await LoadDetailAsync(own, row.Slug, ct))
                    {
                        updated++;
                        hasDetail = true;
                    }
                    await Task.Delay(DetailDelay, ct);
                }

                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.ScottishChessFederation, row.Slug,
                    hasDetail ? row.Url : null, now, ct);

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "Chess Scotland: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "Chess-Scotland-Kalender: {Read} gelesen, {Added} neu, {Updated} mit Spielort, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

    /// <summary>
    /// Ob die Detailseite dieses Turniers schon gelesen wurde — erkennbar an der Adresse im
    /// Herkunftsvermerk. Ein Vermerk OHNE Adresse heisst „aus der Liste, Detailseite fehlt noch".
    /// </summary>
    internal static bool HasDetail(TournamentDirectoryEntry entry, string slug) =>
        entry.Sources.Any(s => s.Kind == DirectorySourceKind.ScottishChessFederation
                               && s.ExternalId == slug && s.Url is { Length: > 0 });

    /// <summary>
    /// Die Detailseite holen und ihren Spielort eintragen. Gibt zurueck, ob sie lesbar war — ein
    /// Ausfall kostet den Spielort, nicht den Termin (den hat die Liste schon).
    /// </summary>
    private async Task<bool> LoadDetailAsync(
        TournamentDirectoryEntry entry, string slug, CancellationToken ct)
    {
        ChessScotlandDetail? detail;
        try { detail = await FetchDetailAsync(slug, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Chess Scotland: Detailseite {Slug} nicht lesbar", slug);
            return false;
        }
        if (detail is null) return false;

        if (detail.Venue is not { Length: > 0 } venue) return true;

        var locationChanged = entry.LocationText != venue;
        entry.LocationText = ExternalDirectorySource.Truncate(venue, 300);

        if ((locationChanged || entry.Lat is null) && entry.GeoSource != GeoSource.Manual)
        {
            var hit = await _geocoding.ResolveAsync(venue, null, Federation, ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                entry.Lat = hit.Lat;
                entry.Lon = hit.Lon;
                entry.GeoSource = hit.Source;
                entry.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
        return true;
    }

    /// <summary>
    /// Die Bedenkzeit-KLASSE aus den Schlagworten. Ein Kongress kann mehrere zugleich anbieten
    /// (Hauptturnier + Blitz-Abend) — die ERNSTHAFTESTE gilt als die des Turniers, dieselbe
    /// Prioritaet wie bei einer Bedenkzeit-Angabe mit mehreren Phasen ("die erste Zeitangabe
    /// zaehlt"). „Fide" ist ein reiner Wertungs-Vermerk, keine eigene Klasse.
    /// </summary>
    internal static TournamentSpeed SpeedOf(IReadOnlyCollection<string> timeControls)
    {
        if (Has(timeControls, "Standard")) return TournamentSpeed.Standard;
        if (Has(timeControls, "Allegro")) return TournamentSpeed.Rapid;
        if (Has(timeControls, "Blitz")) return TournamentSpeed.Blitz;
        return TournamentSpeed.Unknown;
    }

    internal static string? TimeControlTextOf(IReadOnlyCollection<string> timeControls) =>
        timeControls.Count == 0 ? null : string.Join(", ", timeControls);

    private static bool Has(IReadOnlyCollection<string> values, string target) =>
        values.Any(v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// „Junior" ist nur dann eine verlaessliche JUGEND-Angabe, wenn NICHT gleichzeitig „Adult"
    /// dabeisteht — 25 von 43 gemessenen Terminen tragen beide Schlagworte zugleich (ein Kongress
    /// mit Erwachsenen- UND Jugendabteilung), ein Pin auf „Nachwuchs" waere dort falsch. Sagt der
    /// NAME bereits eine Klasse, ist das die bessere Auskunft und bleibt unangetastet.
    /// </summary>
    internal static void ApplyYouthMark(TournamentDirectoryEntry entry, List<string> categories)
    {
        if (entry.AgeGroups != TournamentAgeGroups.None) return;
        if (!Has(categories, "Junior") || Has(categories, "Adult")) return;

        entry.AgeGroups = TournamentAgeGroups.YouthUnspecified;
    }

    /// <summary>
    /// Diese Quelle hat keine Nummer — der Slug ist eine echte, aber teils lange Kennung (bis 54
    /// Zeichen gemessen). <see cref="TournamentDirectoryEntry.PublicId"/> fasst nur 24 Zeichen,
    /// darum ein Hash-Kurzwert wie bei sjakk.no; der lesbare Slug bleibt vollstaendig im
    /// Herkunftsvermerk (<see cref="TournamentDirectorySource.ExternalId"/>, 60 Zeichen).
    /// </summary>
    internal static string PublicIdOf(string slug)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(slug.Trim().ToLowerInvariant()));
        return "sc" + Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessScotlandEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/chess-scotland-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<ChessScotlandRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.Slug is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessScotlandEvent(
                r.Slug!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Url,
                r.Categories ?? [], r.TimeControls ?? []))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private async Task<ChessScotlandDetail?> FetchDetailAsync(string slug, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/chess-scotland-calendar/detail?slug={Uri.EscapeDataString(slug)}", ct);

        // 404 = die Seite gibt es nicht mehr oder sie ist nicht lesbar. Kein Fehler: der Termin
        // steht schon, nur sein Spielort fehlt noch.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        return JsonSerializer.Deserialize<ChessScotlandDetail>(body, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ChessScotlandRow(
        string? Slug, string? Name, string? StartDate, string? EndDate, string? Url,
        List<string>? Categories, List<string>? TimeControls);

    private sealed record ChessScotlandDetail(string? Venue);

    public sealed record CrawlerChessScotlandEvent(
        string Slug, string Name, DateOnly? Start, DateOnly? End, string? Url,
        List<string> Categories, List<string> TimeControls);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d : null;
}
