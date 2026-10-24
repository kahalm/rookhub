using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using RookHub.Api.Services.Tournaments;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender des italienischen Verbands (FSI) als Quelle.
///
/// <para><b>Warum diese Quelle die wichtigste Ergaenzung ist.</b> Italien faehrt sein
/// Turnierwesen auf Vega/vesus, nicht auf chess-results: von 285 Eintraegen des FSI-Kalenders
/// verlinkt <b>kein einziger</b> dorthin, und eine Namensstichprobe von 15 fand nur 3 auf
/// chess-results. Rund vier Fuenftel der italienischen Turniere fehlen dort — und das ist keine
/// Momentaufnahme, sondern die Entscheidung eines ganzen Verbands.</para>
///
/// <para><b>Ein Abruf fuer alles.</b> Name, Termin, Region, Provinz, Ort, Bedenkzeit und
/// Rundenzahl stehen inline in der Trefferliste; es gibt keine Detailseiten. Der Preis ist die
/// Groesse (rund 1,25 MB fuer 283 Turniere), nicht die Zahl der Anfragen.</para>
///
/// <para><b>Der Ort ist der schwache Punkt.</b> Die Quelle nennt <b>keine Postleitzahl</b> (0 von
/// 283) — der genaueste Weg des <see cref="GeocodingService"/> greift hier also nie. Dafuer ist
/// die PROVINZ immer da, und die loest italienische Namensgleichheit auf: „Marino" gibt es
/// mehrfach, „Marino" in der Provinz Roma nicht. Uebergeben wird deshalb „Ort, Provinz" — dieselbe
/// Form, die der Geocoder bei chess-results-Ortstexten schon sieht.</para>
/// </summary>
public class FsiDirectorySweepService : IDirectorySourceAdapter<FsiDirectorySweepService.CrawlerFsiEvent>
{
    /// <summary>
    /// Zwischenstand alle 50 Turniere. Der Abruf ist einer, die VERORTUNG aber laeuft je neuem
    /// Eintrag gegen das Lexikon — bei 283 Turnieren lohnt sich der Zwischenstand trotzdem.
    /// </summary>
    private const int SaveEvery = 50;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<FsiDirectorySweepService> _log;

    public FsiDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<FsiDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(
        int months = 18, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, today.AddMonths(months), ct);
        return await ExternalSweepRunner.RunAsync(_db, _log, this, events, ct);
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<CrawlerFsiEvent>.Kind => DirectorySourceKind.ItalianChessFederation;
    string IDirectorySourceAdapter<CrawlerFsiEvent>.LogName => "FSI";
    int IDirectorySourceAdapter<CrawlerFsiEvent>.SaveEvery => SaveEvery;
    string? IDirectorySourceAdapter<CrawlerFsiEvent>.SourceKey(CrawlerFsiEvent row) => row.EventId.Length == 0 ? null : row.EventId;
    DateOnly? IDirectorySourceAdapter<CrawlerFsiEvent>.Start(CrawlerFsiEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerFsiEvent>.Name(CrawlerFsiEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerFsiEvent>.PublicId(CrawlerFsiEvent row) => $"it{row.EventId}";
    // Die FSI kennt keine chess-results-Nummer. Alles, was dort gebraucht wird (Rundenplan,
    // Vereins-Aufloesung, Abo), bleibt fuer die eigenen Eintraege deshalb aus.
    string? IDirectorySourceAdapter<CrawlerFsiEvent>.Federation(CrawlerFsiEvent row) => "ITA";
    string? IDirectorySourceAdapter<CrawlerFsiEvent>.MatchPlace(CrawlerFsiEvent row) => LocationOf(row);
    string? IDirectorySourceAdapter<CrawlerFsiEvent>.Url(CrawlerFsiEvent row) => null;

    // chess-results kennt das Turnier. Die FSI fuellt nur Luecken — Bedenkzeit und Rundenzahl
    // fehlen dort oefter, als man denkt.
    bool IDirectorySourceAdapter<CrawlerFsiEvent>.FillMatch(TournamentDirectoryEntry match, CrawlerFsiEvent row) =>
        ExternalSweepRunner.FillTimeControlGaps(match, row.TimeControl, row.Rounds);

    async Task<bool> IDirectorySourceAdapter<CrawlerFsiEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerFsiEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        var locationChanged = own.LocationText != LocationOf(row);
        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.State = ExternalDirectorySource.Truncate(Titlecase(row.Region), 100);
        own.LocationText = ExternalDirectorySource.Truncate(LocationOf(row), 300);
        own.TimeControlText = ExternalDirectorySource.Truncate(row.TimeControl, 300);
        own.Speed = TournamentSpeedClassifier.Classify(row.TimeControl);
        own.Rounds = row.Rounds;
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.ItalianChessFederation, row.EventId, null, now, ct);

        // Verortet wird nur, wenn der Ortstext neu ist oder noch kein Pin steht — sonst
        // liefe das Lexikon jede Nacht fuer 283 unveraenderte Eintraege.
        if (locationChanged || own.Lat is null)
        {
            var hit = await _geocoding.ResolveAsync(own.LocationText, own.State, "ITA", ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                own.Lat = hit.Lat;
                own.Lon = hit.Lon;
                own.GeoSource = hit.Source;
                own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
        // „Ergaenzt" zaehlt nur an chess-results-Eintraegen (FillMatch).
        return false;
    }

    void IDirectorySourceAdapter<CrawlerFsiEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "FSI-Kalender: {Read} gelesen, {Added} neu, {Updated} ergaenzt, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);


    /// <summary>
    /// „Ort, Provinz" — die Provinz ist der einzige Unterscheider, den diese Quelle mitbringt, und
    /// italienische Ortsnamen sind haeufig mehrfach vergeben. Ohne sie waehlte die Verortung bei
    /// „Marino" die falsche von mehreren.
    /// </summary>
    internal static string? LocationOf(CrawlerFsiEvent row)
    {
        var place = row.Place?.Trim();
        var province = row.Province?.Trim();

        if (place is not { Length: > 0 }) return province;
        if (province is not { Length: > 0 }) return place;
        // Nicht doppeln: bei „Roma / Roma" steht der Ort sonst zweimal da.
        return string.Equals(place, province, StringComparison.OrdinalIgnoreCase)
            ? place
            : $"{place}, {province}";
    }

    /// <summary>„LAZIO" ist Versalschrift der Quelle, nicht der Name der Region.</summary>
    internal static string? Titlecase(string? text)
    {
        if (text is not { Length: > 0 }) return text;
        if (text.Any(char.IsLower)) return text;

        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 1
                ? w.ToUpperInvariant()
                : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerFsiEvent>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<FsiRow>>(
            $"/api/fsi-calendar?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerFsiEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate),
                r.Region, r.Province, r.Place, r.EventType, r.TimeControl, r.Rounds))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    private sealed record FsiRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Region,
        string? Province, string? Place, string? EventType, string? TimeControl, int? Rounds);

    public sealed record CrawlerFsiEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Region,
        string? Province, string? Place, string? EventType, string? TimeControl, int? Rounds);
}
