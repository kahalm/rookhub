using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

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
public class FsiDirectorySweepService
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
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>();

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.EventId.Length == 0) continue;
                // Geliefert ist geliefert: die Quelle FUEHRT diese Zeile. Ob WIR sie lesen
                // koennen, ist eine andere Frage. Stand das Eintragen erst hinter den Pruefungen,
                // galt eine Zeile mit unlesbarem Termin als verschwunden und war nach zwei Laeufen
                // abgesagt — und ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle.
                delivered.Add(row.EventId);
                if (row.Start is not { } start || row.Name.Length == 0) continue;

                processed++;
                var publicId = $"it{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, "ITA", start, row.Name,
                    new ExternalDirectorySource.MatchHint(
                        DirectorySourceKind.ItalianChessFederation, row.EventId, LocationOf(row)), ct);

                if (match is not null)
                {
                    // chess-results kennt das Turnier. Die FSI fuellt nur Luecken — Bedenkzeit und
                    // Rundenzahl fehlen dort oefter, als man denkt.
                    var text = match.TimeControlText;
                    var rounds = match.Rounds;
                    var changed = ExternalDirectorySource.FillIfEmpty(row.TimeControl, ref text, 300);
                    changed |= ExternalDirectorySource.FillIfEmpty(row.Rounds, ref rounds);
                    match.TimeControlText = text;
                    match.Rounds = rounds;
                    if (changed && match.Speed == TournamentSpeed.Unknown)
                        match.Speed = TournamentSpeedClassifier.Classify(match.TimeControlText);

                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.ItalianChessFederation, row.EventId, null, now, ct);
                    matched++;
                    if (changed) updated++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("FSI: {PublicId} geht in {Target} auf ({Name})",
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
                        // Die FSI kennt keine chess-results-Nummer. Alles, was dort gebraucht wird
                        // (Rundenplan, Vereins-Aufloesung, Abo), bleibt deshalb aus.
                        ChessResultsId = null,
                        Federation = "ITA",
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

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
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
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

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "FSI: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "FSI-Kalender: {Read} gelesen, {Added} neu, {Updated} ergaenzt, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.ItalianChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

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
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/fsi-calendar?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<FsiRow>>(body, JsonOptions) ?? [];
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record FsiRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Region,
        string? Province, string? Place, string? EventType, string? TimeControl, int? Rounds);

    public sealed record CrawlerFsiEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Region,
        string? Province, string? Place, string? EventType, string? TimeControl, int? Rounds);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
