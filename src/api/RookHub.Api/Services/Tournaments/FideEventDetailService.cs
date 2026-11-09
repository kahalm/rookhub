using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using RookHub.Api.Services.TournamentDirectory;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.Tournaments;

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
/// <para><b>Einzel gegen Mannschaft kommt hier nur im ausdruecklichen Fall.</b> Das Feld
/// „Tournament system" trennt Rundenturnier von Schweizer System, nicht Einzel von Mannschaft —
/// die 46. Schacholympiade steht dort auf „Other", die Team-Blitz-WM auf „Round-Robin". Nur ein
/// System, das „Team" ausdruecklich nennt („Team Swiss", „Team Round-Robin"), setzt
/// <see cref="TournamentDirectoryEntry.Kind"/> auf <c>Team</c>; alles andere laesst es stehen.
/// „Sonst Einzel" waere geraten und fuer die Olympiade nachweislich falsch.</para>
///
/// <para><b>Widerspruchsregel fuer Turniere auf BEIDEN Quellen</b> (Entscheidung 2026-11-09):
/// <b>chess-results gilt fuer Termin und Ort</b> — Termin und Ortstext schreibt dieser Nachlauf ohnehin
/// nie, und einen schon verorteten Eintrag mit chess-results-Nummer verortet er nicht neu;
/// Runden, Teilnehmer, Veranstalter und Hauptschiedsrichter fuellt er nur, wo nichts steht.
/// <b>FIDE gilt fuer die Bedenkzeit-Beschreibung</b> — dort ist sie ausgeschrieben („90 minutes
/// with 30 second increment from move 1"), bei chess-results oft nur „90+30" oder leer; sie
/// ersetzt den vorhandenen Text, und die Speed-Klasse folgt ihr (nur wenn sie daraus eindeutig
/// hervorgeht).</para>
/// </summary>
public class FideEventDetailService
{
    /// <summary>
    /// Fassung dieses Abrufs. **Erhoehen, sobald sich `ParseEventAsync` im Crawler oder der Weg
    /// zum Detail-Fragment aendert** — der naechtliche Durchgang holt dann jedes aeltere Ereignis
    /// genau EINMAL nach, ohne dass jemand `retryEmpty` von Hand ausloest.
    ///
    /// <para>1 = Stand 2026-09-07 (erste Fassung des Detail-Nachlaufs).
    /// 2 = 2026-11-09: Veranstalter und Hauptschiedsrichter, Mannschaft aus „Team …", Ortstext.
    /// <b>Setzt einen Crawler voraus, der <c>organizers</c>/<c>chiefArbiter</c> liefert</b> — ein
    /// aelterer Crawler laesst die Felder weg, und der Eintrag gaelte trotzdem als Fassung 2.</para>
    /// </summary>
    public const int CurrentVersion = 2;

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

        // Widerspruchsregel: FIDE gilt fuer die Bedenkzeit-BESCHREIBUNG, auch gegen chess-results.
        // Die Klasse folgt dem Text nur, wenn er sie hergibt — ein Text, aus dem der Klassifizierer
        // nichts liest, loescht keine vorhandene Klasse.
        if (!string.IsNullOrWhiteSpace(d.TimeControlText))
        {
            entry.TimeControlText = Truncate(d.TimeControlText, 300);
            var classified = TournamentSpeedClassifier.Classify(d.TimeControlText);
            if (classified != TournamentSpeed.Unknown) entry.Speed = classified;
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

        if (entry.Kind == TournamentKind.Unknown && MapKind(d.System) is { } kind)
        {
            entry.Kind = kind;
            any = true;
        }

        // `is null or <= 0`, nicht `<= 0`: die Spalten sind nullbar, und ein Vergleich gegen null
        // ist FALSCH — mit `<= 0` allein blieben genau die leeren Felder ungefuellt, also die,
        // um die es hier geht.
        if (d.Rounds is > 0 && entry.Rounds is null or <= 0) { entry.Rounds = d.Rounds.Value; any = true; }
        if (d.Players is > 0 && entry.PlayerCount is null or <= 0) { entry.PlayerCount = d.Players.Value; any = true; }

        // Personen nur in Luecken — chess-results fuehrt sie oft genauer (mit Titel, ohne Kuerzel).
        if (!string.IsNullOrWhiteSpace(d.Organizers) && string.IsNullOrWhiteSpace(entry.Organizer))
        {
            entry.Organizer = Truncate(d.Organizers, 300);
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(d.ChiefArbiter) && string.IsNullOrWhiteSpace(entry.ChiefArbiter))
        {
            entry.ChiefArbiter = Truncate(d.ChiefArbiter, 300);
            any = true;
        }

        // Den Ortstext besitzt der Sweep (Jahresansicht bzw. chess-results, und der ChangeHash
        // haengt daran) — hier nur, wenn noch gar keiner steht.
        if (string.IsNullOrWhiteSpace(entry.LocationText) && !string.IsNullOrWhiteSpace(d.City))
        {
            entry.LocationText = Truncate(
                string.IsNullOrWhiteSpace(d.Country) ? d.City : $"{d.City}, {d.Country}", 500);
            any = true;
        }

        return any;
    }

    /// <summary>
    /// Mannschaft nur, wenn das Turniersystem es AUSDRUECKLICH sagt („Team Swiss", „Team
    /// Round-Robin"). Sonst <c>null</c> — auch nicht „Einzel", denn FIDE fuehrt die Olympiade auf
    /// „Other" und die Team-Blitz-WM auf „Round-Robin".
    /// </summary>
    internal static TournamentKind? MapKind(string? system) =>
        system is not null
        && System.Text.RegularExpressions.Regex.IsMatch(system, @"\bteams?\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            ? TournamentKind.Team
            : null;

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
        if (entry.GeoSource is GeoSource.Manual or GeoSource.PostalCode or GeoSource.SourceProvided) return false;
        // Widerspruchsregel: bei Turnieren auf beiden Quellen gilt chess-results fuer den Ort. Ein
        // schon verorteter Eintrag mit chess-results-Nummer bleibt, wo er ist; nur ein Eintrag OHNE
        // brauchbaren Pin (keiner, oder mehrdeutig) darf die FIDE-Anschrift nehmen.
        if (entry.ChessResultsId is not null && entry.GeoSource is not (GeoSource.None or GeoSource.Ambiguous))
            return false;

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
        // 404 heisst beim Crawler „keine gueltige Ereignis-Nummer" — das aendert sich nie, also
        // „nachgesehen, nichts da" (null) statt eines Fehlers, der den Eintrag jede Nacht wieder
        // vorn in die Warteschlange stellt.
        return await _httpClientFactory.GetCrawlerJsonAsync<CrawlerFideDetail>(
            $"/api/fide-calendar/event?id={Uri.EscapeDataString(fideId)}", ct,
            absentOn: System.Net.HttpStatusCode.NotFound);
    }

    /// <summary>
    /// SPIEGEL von <c>FideEventDetailResponse</c> im Crawler (<c>DTOs/FideCalendarDtos.cs</c>);
    /// goldene Fassung in <c>tests/…/Fixtures/CrawlerContract/fide-event.json</c>.
    /// </summary>
    internal sealed record CrawlerFideDetail(
        string? EventId, string? EventType, string? TimeControl, string? TimeControlText,
        string? System, int? Rounds, int? Players, string? Country, string? City,
        string? VenueAddress, string? Website, string? Organizers = null, string? ChiefArbiter = null);
}
