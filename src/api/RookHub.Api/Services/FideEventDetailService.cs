using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>Was ein Durchgang getan hat. <c>Checked</c> zaehlt die VERSUCHTEN.</summary>
public record FideDetailResult(int Checked, int WithDetails, int Geocoded);

/// <summary>
/// Traegt die Detailangaben der FIDE-Eintraege nach — Bedenkzeit, Turniersystem, Runden- und
/// Teilnehmerzahl und vor allem die ANSCHRIFT des Spielorts.
///
/// <para><b>Warum ein eigener Nachlauf.</b> Der FIDE-Sweep liest die Jahresansicht, und die traegt
/// je Ereignis genau einen Textabschnitt („01 May - 07 May / Malmo (SWE)"). Am Dev-Stand gemessen
/// heisst das: von 144 FIDE-eigenen Eintraegen hatte KEIN EINZIGER eine Bedenkzeit, eine
/// Speed-Klasse, eine Rundenzahl oder eine Teilnehmerzahl, waehrend die chess-results-Eintraege
/// daneben 4444 von 4923 mit Bedenkzeit fuehren. Die Angaben gibt es, sie kosten nur einen Abruf
/// je Ereignis — dieselbe Kostenklasse wie der Rundenplan, und wie dort einmalig: ein
/// abgeschlossenes Ereignis aendert sich nicht mehr.</para>
///
/// <para><b>Der eigentliche Gewinn ist die Verortung.</b> Das Detail nennt die Anschrift des
/// Spielorts, und die traegt oft eine POSTLEITZAHL („Via Iberica, 69, 77, 50012 Zaragoza, Spain").
/// Der <see cref="GeocodingService"/> hat mit einer PLZ seinen genauesten Weg, und der greift bei
/// FIDE-Eintraegen sonst nie — von 144 sind 81 verortet, und die ueber Ortsnamen.</para>
///
/// <para><b>Was NICHT von hier kommt: Einzel gegen Mannschaft.</b> Das Feld „Tournament system"
/// trennt Rundenturnier von Schweizer System, nicht Einzel von Mannschaft — die
/// 46. Schacholympiade steht dort auf „Other". <see cref="TournamentDirectoryEntry.Kind"/> bleibt
/// bei FIDE-Eintraegen also <c>Unknown</c>, und das ist die richtige Antwort statt einer
/// geratenen.</para>
/// </summary>
public class FideEventDetailService
{
    /// <summary>
    /// Fassung dieses Abrufs. **Erhoehen, sobald sich `ParseEventAsync` im Crawler oder der Weg
    /// zum Detail-Fragment aendert** — der naechtliche Durchgang holt dann jedes aeltere Ereignis
    /// genau EINMAL nach, ohne dass jemand `retryEmpty` von Hand ausloest.
    ///
    /// <para>1 = Stand 2026-09-07 (erste Fassung des Detail-Nachlaufs).</para>
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Zwischenstand alle 25 Ereignisse — dieselbe Lehre wie beim Rundenplan: ein Durchgang ueber
    /// 150 Ereignisse laeuft Minuten, und wurde erst am Ende geschrieben, verwarf ein Abbruch,
    /// ein API-Neustart oder ein Deploy die ganze Arbeit.
    /// </summary>
    private const int SaveEvery = 25;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<FideEventDetailService> _log;

    public FideEventDetailService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<FideEventDetailService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
    }

    /// <param name="limit">Hoechstens so viele Ereignisse je Durchgang (ein Abruf je Ereignis).</param>
    /// <param name="retryEmpty">
    /// Auch Eintraege vornehmen, die als geprueft gelten und dennoch keine Bedenkzeit tragen.
    /// Bewusst opt-in: ein Ereignis ohne gepflegte Angaben ist der HAEUFIGE Fall (die
    /// 46. Schacholympiade nennt weder Runden- noch Teilnehmerzahl), und jeder erneut vorgenommene
    /// Eintrag kostet wieder einen Abruf.
    /// </param>
    public async Task<FideDetailResult> RunAsync(
        int limit, bool retryEmpty = false, CancellationToken ct = default)
    {
        if (limit <= 0) return new FideDetailResult(0, 0, 0);

        // Kandidat ist, wer eine FIDE-Herkunft hat — nicht, wem die chess-results-Nummer fehlt:
        // ein Turnier kann auf BEIDEN Quellen stehen, und auch dort ist die ausgeschriebene
        // Bedenkzeit von FIDE eine Bereicherung.
        var candidates = await _db.TournamentDirectoryEntries
            .Where(e => e.RemovedAt == null
                        && (e.FideDetailCheckedAt == null
                            || e.FideDetailVersion < CurrentVersion
                            || (retryEmpty && (e.TimeControlText == null || e.TimeControlText == "")))
                        && _db.TournamentDirectorySources.Any(s =>
                            s.TournamentDirectoryEntryId == e.Id && s.Kind == DirectorySourceKind.Fide))
            // Wie beim Rundenplan: der am laengsten nicht gepruefte zuerst (nie geprueft gewinnt,
            // weil NULL aufsteigend vorn steht). Sortiert nach Termin naehme jeder
            // Wiederholungslauf wieder dieselben vordersten Ereignisse.
            .OrderBy(e => e.FideDetailCheckedAt)
            .ThenBy(e => e.StartDate)
            .Take(limit)
            .ToListAsync(ct);

        if (candidates.Count == 0) return new FideDetailResult(0, 0, 0);

        // Die FIDE-Nummer steht in der Herkunft, nicht am Eintrag.
        var ids = candidates.Select(e => e.Id).ToList();
        var fideIds = await _db.TournamentDirectorySources
            .Where(s => ids.Contains(s.TournamentDirectoryEntryId) && s.Kind == DirectorySourceKind.Fide)
            .ToDictionaryAsync(s => s.TournamentDirectoryEntryId, s => s.ExternalId, ct);

        var attempted = 0;
        var withDetails = 0;
        var geocoded = 0;
        var now = DateTime.UtcNow;

        try
        {
            foreach (var entry in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (!fideIds.TryGetValue(entry.Id, out var fideId)) continue;

                attempted++;
                CrawlerFideDetail? detail;
                try
                {
                    detail = await FetchAsync(fideId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // Ein NETZfehler laesst den Vermerk leer — sonst gaelte das Ereignis als
                    // nachgesehen, obwohl niemand hingesehen hat.
                    _log.LogWarning(ex, "FIDE-Detail {FideId} konnte nicht geholt werden", fideId);
                    continue;
                }

                entry.FideDetailCheckedAt = now;
                entry.FideDetailVersion = CurrentVersion;
                if (detail is null) continue;

                if (Apply(entry, detail)) withDetails++;
                if (await TryGeocodeAsync(entry, detail, ct)) geocoded++;

                if (attempted % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            // CancellationToken.None: ein abgebrochener Token wuerde genau den Schreibvorgang
            // verhindern, der die bisherige Arbeit rettet.
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "FIDE-Details: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation("FIDE-Details: {Checked} geprueft, {WithDetails} mit Angaben, {Geocoded} neu verortet",
            attempted, withDetails, geocoded);
        return new FideDetailResult(attempted, withDetails, geocoded);
    }

    /// <summary>
    /// Uebernimmt die Angaben. Ein FEHLENDES Feld laesst den gespeicherten Wert stehen — die
    /// Jahresansicht und der chess-results-Sweep haben moeglicherweise schon etwas eingetragen,
    /// und „FIDE weiss es nicht" ist kein Grund, das zu loeschen.
    /// </summary>
    private static bool Apply(TournamentDirectoryEntry entry, CrawlerFideDetail d)
    {
        var any = false;

        if (!string.IsNullOrWhiteSpace(d.TimeControlText))
        {
            entry.TimeControlText = Truncate(d.TimeControlText, 300);
            entry.Speed = TournamentSpeedClassifier.Classify(d.TimeControlText);
            any = true;
        }
        // Ohne ausgeschriebene Bedenkzeit traegt wenigstens FIDEs eigene Klasse. Sie wird NICHT
        // durch den Klassifizierer geschickt: der rechnet Minuten aus einem Rohtext („90 min +
        // 30 sec"), und das Wort „Standard" enthaelt keine Zahl — er antwortete Unknown.
        if (entry.Speed == TournamentSpeed.Unknown)
        {
            var named = MapSpeed(d.TimeControl);
            if (named != TournamentSpeed.Unknown) { entry.Speed = named; any = true; }
        }

        var system = MapSystem(d.System);
        if (system != TournamentSystem.Unknown)
        {
            entry.System = system;
            any = true;
        }

        // `is null or <= 0`, nicht `<= 0`: die Spalten sind nullbar, und ein Vergleich gegen null
        // ist FALSCH — mit `<= 0` allein blieben genau die leeren Felder ungefuellt, also die,
        // um die es hier geht.
        if (d.Rounds is > 0 && entry.Rounds is null or <= 0) { entry.Rounds = d.Rounds.Value; any = true; }
        if (d.Players is > 0 && entry.PlayerCount is null or <= 0) { entry.PlayerCount = d.Players.Value; any = true; }

        return any;
    }

    /// <summary>
    /// „Round-Robin" / „Swiss-System" / „Other" auf die eigene Aufzaehlung.
    ///
    /// <para>Ein unbekannter Text bleibt <see cref="TournamentSystem.Unknown"/> und nicht
    /// <see cref="TournamentSystem.Other"/>: „Other" heisst „FIDE sagt ausdruecklich etwas
    /// anderes", nicht „wir haben den Text nicht verstanden".</para>
    /// </summary>
    internal static TournamentSystem MapSystem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TournamentSystem.Unknown;
        var t = text.Trim();

        if (t.Contains("swiss", StringComparison.OrdinalIgnoreCase)) return TournamentSystem.Swiss;
        if (t.Contains("robin", StringComparison.OrdinalIgnoreCase)) return TournamentSystem.RoundRobin;
        if (t.Equals("other", StringComparison.OrdinalIgnoreCase)) return TournamentSystem.Other;
        return TournamentSystem.Unknown;
    }

    /// <summary>
    /// FIDEs Bedenkzeit-KLASSE auf die eigene Aufzaehlung. Die Quelle nennt sie beim Namen
    /// („Standard", „Rapid", „Blitz") — das ist eine Angabe, keine Rechnung, und wird deshalb
    /// abgebildet statt aus einem Text erschlossen.
    /// </summary>
    internal static TournamentSpeed MapSpeed(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "standard" or "classical" => TournamentSpeed.Standard,
        "rapid" => TournamentSpeed.Rapid,
        "blitz" => TournamentSpeed.Blitz,
        _ => TournamentSpeed.Unknown,
    };

    /// <summary>
    /// Verortet den Eintrag ueber die ANSCHRIFT neu — der Grund, warum dieser Nachlauf sich lohnt.
    ///
    /// <para>Nur, wenn es etwas zu gewinnen gibt: eine von Hand gesetzte Koordinate bleibt
    /// unangetastet, und eine schon ueber die Postleitzahl gefundene wird nicht durch eine zweite
    /// PLZ-Suche ersetzt. Alles andere (kein Pin, Regionsmitte, Ortsname, mehrdeutig) darf eine
    /// Anschrift schlagen.</para>
    /// </summary>
    private async Task<bool> TryGeocodeAsync(
        TournamentDirectoryEntry entry, CrawlerFideDetail d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.VenueAddress)) return false;
        if (entry.GeoSource is GeoSource.Manual or GeoSource.PostalCode) return false;

        var hit = await _geocoding.ResolveAsync(d.VenueAddress, entry.State, entry.Federation, ct);
        if (hit is null || hit.Source == GeoSource.Ambiguous) return false;

        entry.Lat = hit.Lat;
        entry.Lon = hit.Lon;
        entry.GeoSource = hit.Source;
        entry.GeoPlaceName = Truncate(hit.PlaceName, 200);
        return true;
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<CrawlerFideDetail?> FetchAsync(string fideId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/fide-calendar/event?id={Uri.EscapeDataString(fideId)}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        return JsonSerializer.Deserialize<CrawlerFideDetail>(body, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record CrawlerFideDetail(
        string? EventId, string? EventType, string? TimeControl, string? TimeControlText,
        string? System, int? Rounds, int? Players, string? Country, string? City,
        string? VenueAddress, string? Website);

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
