using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record DirectorySweepResult(
    string Federation, int Rows, int Added, int Updated, int Changed, int Removed, string? Error = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Fuellt und pflegt das Turnierverzeichnis aus der chess-results-Turniersuche.
///
/// Eine Abfrage je Foederation liefert alle Turniere eines Zeitfensters samt Ort, Termin und
/// Teilnehmerzahl - die Einzelturnierseiten werden dafuer NICHT geholt. Was sich geaendert hat,
/// entsteht hier zentral am Verzeichniseintrag und wird erst danach auf die Abonnenten aufgefaechert;
/// ein Schnappschuss je Nutzer waere redundant und wuerde bei mehreren Abonnenten auseinanderlaufen.
/// </summary>
public class TournamentDirectoryService
{
    /// <summary>
    /// Der Datumsfilter der Suche greift auf das ENDdatum. Rueckwaerts genug, dass ein gerade
    /// beendetes Turnier nicht sofort als "verschwunden" gilt.
    /// </summary>
    internal int LookBackDays { get; set; } = 30;
    internal int LookAheadMonths { get; set; } = 18;

    /// <summary>
    /// Erst nach so vielen Sweeps ohne Treffer gilt ein Turnier als abgesagt. Ein einzelner
    /// gescheiterter oder abgeschnittener Sweep wuerde sonst reihenweise Absagen melden.
    /// </summary>
    internal int MissedSweepsUntilRemoved { get; set; } = 2;

    /// <summary>
    /// Pause zwischen zwei Foederationen. Gemessen (Dev, 2026-09-06): einzeln beantwortet
    /// chess-results eine Suche in 2 s — neun Suchen als Salve liessen vier davon in einen
    /// 180-s-Timeout laufen. Die Gegenseite drosselt eine Burst-Folge also spuerbar, und der
    /// Crawler wartet die Drosselung mit seinem eigenen Backoff aus. Fuer einen naechtlichen Lauf
    /// sind ein paar Sekunden je Foederation nichts; ein halb fehlgeschlagener Lauf schon.
    /// </summary>
    internal TimeSpan DelayBetweenFederations { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Zeilenlimit einer Trefferliste. Einstellbar wie die uebrigen Sweep-Groessen, damit die
    /// Tests die ABGESCHNITTENE Liste nachstellen koennen, ohne 2000 Zeilen zu erfinden — genau
    /// der Fall, in dem sowohl die Verschwunden-Erkennung als auch die Turnierart aussetzen muss.
    /// </summary>
    internal int MaxRows { get; set; } = 2000;

    /// <summary>
    /// Die Turnierarten der chess-results-Suche, die MANNSCHAFTSturniere liefern: 2 Rundenturnier
    /// fuer Mannschaften, 3 Schweizer System fuer Mannschaften.
    /// </summary>
    /// <summary>
    /// Die vier chess-results-Turnierarten und was sie bedeuten. Sie sind das KREUZPRODUKT aus
    /// „Mannschaft?" und „welches System?" — wer die Durchgaenge ohnehin macht, bekommt beide
    /// Angaben aus derselben Abfrage.
    /// </summary>
    private static readonly (string Art, TournamentKind Kind, TournamentSystem System)[] ArtPasses =
    [
        ("0", TournamentKind.Individual, TournamentSystem.Swiss),
        ("1", TournamentKind.Individual, TournamentSystem.RoundRobin),
        ("2", TournamentKind.Team,       TournamentSystem.RoundRobin),
        ("3", TournamentKind.Team,       TournamentSystem.Swiss),
    ];

    /// <summary>Blockgroesse beim Nachladen der neuen Eintraege fuer die Umkreis-Meldung.</summary>
    private const int NotifyLookupBatch = 500;

    /// <summary>
    /// Name des eigenen Crawler-Clients (laengeres Zeitlimit als der Live-Pfad, siehe Program.cs).
    /// </summary>
    public const string CrawlerClientName = "CrawlerDirectory";

    /// <summary>
    /// Vorgabe fuer das Zeitlimit dieses Clients (<c>TournamentDirectory:CrawlerTimeoutSeconds</c>).
    ///
    /// <para><b>Der Wert haengt an einer MESSUNG, nicht an einem Gefuehl.</b> Am 2026-09-09 gegen
    /// die echten Quellen gemessen, jeweils EIN Aufruf des Crawler-Endpunkts:</para>
    ///
    /// <list type="table">
    ///   <item><term>England (ECF)</term><description><b>196 s</b>, 278 Turniere, 124 kB</description></item>
    ///   <item><term>Irland (ICU)</term><description>21 s, 81 Turniere</description></item>
    ///   <item><term>Niederlande (KNSB)</term><description>20 s, 174 Turniere</description></item>
    ///   <item><term>Frankreich (FFE)</term><description>21 s, 168 Turniere</description></item>
    ///   <item><term>Norwegen, Schottland, Rumaenien, Wales</term><description>unter 3 s</description></item>
    /// </list>
    ///
    /// <para>England sprengt die frueheren 180 s, und zwar nicht aus Versehen: seine robots.txt
    /// nennt „Crawl delay: 10", und bei 278 Turnieren sind das sechs Seiten Termine plus sechs
    /// Seiten Spielstaetten — die Wartezeit IST die Laufzeit. Mit der alten Vorgabe waere die
    /// englische Quelle in JEDER Nacht in den Timeout gelaufen, ohne je ein Turnier zu liefern,
    /// und der Fehler haette wie ein Netzproblem ausgesehen.</para>
    ///
    /// <para>Die Vorgabe traegt deshalb Luft nach oben (Faktor drei auf den gemessenen
    /// Hoechstwert): eine Quelle wird langsamer, wenn sie waechst, und ein Zeitlimit, das genau
    /// auf den Messwert von heute passt, faellt beim naechsten Dutzend Turniere um. Teuer ist ein
    /// zu GROSSES Limit hier nicht — der Sweep laeuft nachts und nacheinander.</para>
    /// </summary>
    public const int DefaultCrawlerTimeoutSeconds = 600;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly NotificationService _notifications;
    private readonly ILogger<TournamentDirectoryService> _log;

    public TournamentDirectoryService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        GeocodingService geocoding,
        NotificationService notifications,
        ILogger<TournamentDirectoryService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _notifications = notifications;
        _log = log;
    }

    /// <summary>
    /// Sweept die uebergebenen Foederationen nacheinander und meldet am Ende EINMAL die neuen
    /// Turniere je Suchprofil. Nacheinander, weil der Crawler ohnehin einen prozessweiten
    /// Rate-Limiter hat - parallel wuerde nur die Warteschlange dort waschen.
    /// </summary>
    public async Task<List<DirectorySweepResult>> RunSweepAsync(
        IReadOnlyList<string> federations, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<DirectorySweepResult>();
        var newEntryIds = new List<int>();

        foreach (var federation in federations)
        {
            ct.ThrowIfCancellationRequested();
            if (results.Count > 0 && DelayBetweenFederations > TimeSpan.Zero)
                await Task.Delay(DelayBetweenFederations, ct);

            var (result, added) = await SweepFederationAsync(federation, today, ct);
            results.Add(result);
            newEntryIds.AddRange(added);
        }

        if (newEntryIds.Count > 0)
            await NotifyNearbyAsync(newEntryIds, today, ct);

        return results;
    }

    /// <summary>
    /// Ein Durchgang fuer eine Foederation. Gibt zusaetzlich die IDs der neu angelegten Eintraege
    /// zurueck, damit die Umkreis-Meldung am Ende ueber alle Foederationen aggregieren kann.
    /// </summary>
    public async Task<(DirectorySweepResult Result, List<int> NewEntryIds)> SweepFederationAsync(
        string federation, DateOnly today, CancellationToken ct = default)
    {
        federation = federation.Trim().ToUpperInvariant();
        var from = today.AddDays(-LookBackDays);
        var to = today.AddMonths(LookAheadMonths);

        var sweep = await _db.TournamentDirectorySweeps.FirstOrDefaultAsync(s => s.Federation == federation, ct);
        if (sweep is null)
        {
            sweep = new TournamentDirectorySweep { Federation = federation };
            _db.TournamentDirectorySweeps.Add(sweep);
        }
        sweep.LastAttemptedAt = DateTime.UtcNow;

        List<CrawlerDirectoryRow> rows;
        try
        {
            var path = $"/api/tournament-search?fed={Uri.EscapeDataString(federation)}" +
                       $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&maxRows={MaxRows}";
            rows = ParseRows(await FetchAsync(path, ct));
        }
        // Ein HttpClient-TIMEOUT kommt als TaskCanceledException — also als
        // OperationCanceledException, obwohl der Aufrufer gar nichts abgebrochen hat. Ein Filter
        // auf den Typ allein liess ihn durch und riss den ganzen Sweep mit: in Dev beendete die
        // eine Foederation, die hinter einer VPN-Rotation wartete, den Lauf der acht anderen mit
        // einem 500er. Durchgereicht wird deshalb nur, was der AUFRUFER abgebrochen hat.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fehlgeschlagener Sweep: LastSweptAt bleibt ALT. Damit nimmt die Rotation die
            // Foederation gleich wieder vor - und die Verschwunden-Erkennung setzt nicht auf
            // einer halben Trefferliste auf, die sonst reihenweise Absagen melden wuerde.
            sweep.LastError = Truncate(ex.Message, 500);
            sweep.ConsecutiveFailures++;
            await _db.SaveChangesAsync(ct);
            _log.LogWarning(ex, "Verzeichnis-Sweep {Federation} fehlgeschlagen", federation);
            return (new DirectorySweepResult(federation, 0, 0, 0, 0, 0, ex.Message), []);
        }

        var artMap = await FetchArtMapAsync(federation, from, to, ct);

        // Die Spielorte MIT laden: ohne sie steht `entry.Venues` leer da, ReplaceVenues loescht
        // nichts, und die alten Zeilen sammeln sich mit jedem naechtlichen Lauf an.
        var existing = await _db.TournamentDirectoryEntries
            .Include(e => e.Venues)
            .Include(e => e.Sources)
            .Where(e => e.Federation == federation && (e.EndDate == null || e.EndDate >= from))
            .ToListAsync(ct);
        var byId = existing.ToDictionary(e => e.ChessResultsId, StringComparer.Ordinal);

        // Und die Zeilen NACHLADEN, die zwar geliefert wurden, aber nicht ins Fenster passen.
        // Ohne das gilt ein VERSCHOBENES Turnier als neu: gespeichert mit Ende im Maerz, vom
        // Veranstalter auf November verlegt, faellt es beim Mai-Lauf aus `existing` heraus, die
        // Suche liefert es aber weiterhin — der Einfuegeversuch laeuft in den Unique-Index auf
        // ChessResultsId und reisst den GANZEN Lauf mit (SaveChanges liegt ausserhalb des
        // try/catch weiter oben). Ausgerechnet die Terminaenderung also, fuer die das
        // Aenderungs-Feature gebaut wurde. Fremde Foederation absichtlich NICHT eingeschraenkt:
        // ein Turnier kann auch die Foederation wechseln, und der Unique-Index gilt global.
        var strays = rows.Select(r => r.ChessResultsId).Where(id => !byId.ContainsKey(id)).Distinct().ToList();
        if (strays.Count > 0)
        {
            foreach (var stray in await _db.TournamentDirectoryEntries
                         .Include(e => e.Venues)
                         .Include(e => e.Sources)
                         .Where(e => strays.Contains(e.ChessResultsId)).ToListAsync(ct))
            {
                byId[stray.ChessResultsId] = stray;
            }
        }

        var now = DateTime.UtcNow;
        var added = new List<TournamentDirectoryEntry>();
        var changed = new List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)>();
        var updated = 0;

        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.ChessResultsId, out var entry))
            {
                var oldHash = entry.ChangeHash;
                var oldDate = FormatRange(entry.StartDate, entry.EndDate);
                var oldLocation = entry.LocationText;
                var oldLocationText = entry.LocationText;

                Apply(row, entry, now, artMap);
                entry.MissedSweeps = 0;
                entry.RemovedAt = null;
                updated++;

                if (oldHash is not null && oldHash != entry.ChangeHash)
                {
                    changed.Add((entry, oldDate, oldLocation));
                    // Der Termin hat sich geaendert — die gespeicherten Spieltermine sind damit
                    // Makulatur. Der Vermerk faellt weg, der naechste Rundenplan-Durchgang holt
                    // sie neu; bis dahin gilt wieder der ganze Zeitraum, was zwar ungenau, aber
                    // nicht falsch ist.
                    if (!string.Equals(oldDate, FormatRange(entry.StartDate, entry.EndDate),
                            StringComparison.Ordinal))
                    {
                        entry.RoundPlanCheckedAt = null;
                        // Die FASSUNG faellt mit weg: sie beantwortet „mit welchem Parser
                        // geholt" und haenge sonst an einem Eintrag ohne Vermerk — eine Aussage
                        // ueber einen Abruf, den es nicht mehr gibt.
                        entry.RoundPlanVersion = 0;
                    }
                }

                // Nur neu verorten, wenn sich der Ortstext wirklich geaendert hat - sonst wuerde
                // jede Nacht der gesamte Bestand durch den Gazetteer laufen.
                if (!string.Equals(oldLocationText, entry.LocationText, StringComparison.Ordinal))
                    await GeocodeAsync(entry, ct);
            }
            else
            {
                entry = new TournamentDirectoryEntry
                {
                    // Fuer einen chess-results-Eintrag ist die Identitaet die Nummer dort — die
                    // Adressen, Abos und Teilen-Links, die es schon gibt, bleiben damit gueltig.
                    PublicId = row.ChessResultsId,
                    ChessResultsId = row.ChessResultsId,
                    FirstSeenAt = now,
                    CreatedAt = now,
                };
                Apply(row, entry, now, artMap);
                await GeocodeAsync(entry, ct);
                _db.TournamentDirectoryEntries.Add(entry);
                added.Add(entry);
                // Sonst legt dieselbe Nummer, zweimal in EINER Trefferliste, zwei Zeilen an —
                // und laeuft in denselben Unique-Index.
                byId[entry.ChessResultsId] = entry;
            }
        }

        // Nicht mehr geliefert: erst zaehlen, dann (ab MissedSweepsUntilRemoved) als abgesagt melden.
        //
        // ABER NUR bei einer VOLLSTAENDIGEN Trefferliste. chess-results kappt bei MaxRows Zeilen,
        // und ueber 18 Monate liegen grosse Foederationen darueber. Der Schwanz der Liste fehlt
        // dann JEDE Nacht an derselben Stelle — die Karenz von zwei Laeufen faengt einen
        // einzelnen Ausfall ab, nicht eine systematische Luecke. Ohne diese Bremse meldet der
        // zweite Lauf reihenweise Absagen fuer Turniere, die stattfinden.
        var truncated = rows.Count >= MaxRows;
        if (truncated)
        {
            _log.LogWarning(
                "Verzeichnis-Sweep {Federation}: Trefferliste bei {Rows} Zeilen abgeschnitten — " +
                "Verschwunden-Erkennung uebersprungen", federation, rows.Count);
        }

        var seen = rows.Select(r => r.ChessResultsId).ToHashSet(StringComparer.Ordinal);
        var removed = new List<TournamentDirectoryEntry>();
        foreach (var entry in truncated
                     ? []
                     : existing.Where(e => e.RemovedAt == null && !seen.Contains(e.ChessResultsId)))
        {
            entry.MissedSweeps++;
            entry.UpdatedAt = now;
            if (entry.MissedSweeps < MissedSweepsUntilRemoved) continue;
            entry.RemovedAt = now;
            removed.Add(entry);
        }

        sweep.LastSweptAt = now;
        sweep.LastRowCount = rows.Count;
        sweep.LastError = null;
        sweep.ConsecutiveFailures = 0;

        await _db.SaveChangesAsync(ct);

        await NotifyChangedAsync(changed, ct);
        await NotifyCancelledAsync(removed, ct);

        _log.LogInformation(
            "Verzeichnis-Sweep {Federation}: {Rows} Zeilen, {Added} neu, {Changed} geaendert, {Removed} abgesagt",
            federation, rows.Count, added.Count, changed.Count, removed.Count);

        return (new DirectorySweepResult(federation, rows.Count, added.Count, updated, changed.Count, removed.Count),
                added.Select(e => e.Id).ToList());
    }

    /// <summary>
    /// Welche Turnierart hat jedes Turnier dieser Foederation — Einzel oder Mannschaft, Schweizer
    /// System oder Rundenturnier?
    ///
    /// <para>Die chess-results-Turniersuche kennt die Turnierart als Suchfeld, und ihre vier
    /// Werte sind genau das Kreuzprodukt beider Fragen (siehe <see cref="ArtPasses"/>). Vier
    /// zusaetzliche Abfragen je Foederation beantworten damit aus der QUELLE, was sonst am
    /// Turniernamen geraten werden muesste: „Liga" im Namen ist ein Indiz, „SK Aachen 2 - SF
    /// Katernberg" keines.</para>
    ///
    /// <para><b>Alles oder nichts.</b> Zwei Faelle geben <c>null</c> zurueck, also „unbekannt",
    /// und lassen Art UND System unangetastet: ein FEHLER (sonst wuerde ein Netzausfall den
    /// halben Bestand umschreiben) und eine ABGESCHNITTENE Liste (chess-results kappt bei
    /// <see cref="MaxRows"/> Zeilen — der fehlende Schwanz waere sonst lauter falsch eingeordnete
    /// Turniere). Das gilt fuer JEDEN der vier Durchgaenge: faellt einer aus, ist die Zuordnung
    /// unvollstaendig, und eine halbe Wahrheit ist hier schlechter als keine.</para>
    /// </summary>
    private async Task<Dictionary<string, (TournamentKind Kind, TournamentSystem System)>?>
        FetchArtMapAsync(string federation, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var map = new Dictionary<string, (TournamentKind, TournamentSystem)>(StringComparer.Ordinal);
        foreach (var (art, kind, system) in ArtPasses)
        {
            List<CrawlerDirectoryRow> rows;
            try
            {
                var path = $"/api/tournament-search?fed={Uri.EscapeDataString(federation)}" +
                           $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&maxRows={MaxRows}&art={art}";
                rows = ParseRows(await FetchAsync(path, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _log.LogWarning(ex,
                    "Turnierart-Durchgang {Federation} (Art {Art}) fehlgeschlagen — Art und System bleiben unveraendert",
                    federation, art);
                return null;
            }

            if (rows.Count >= MaxRows)
            {
                _log.LogWarning(
                    "Turnierart-Durchgang {Federation} (Art {Art}) bei {Rows} Zeilen abgeschnitten — " +
                    "Art und System bleiben unveraendert", federation, art, rows.Count);
                return null;
            }

            // Ein Turnier steht in genau EINER Art; taucht es doch zweimal auf, gilt der erste
            // Treffer, statt die Zuordnung stillschweigend zu ueberschreiben.
            foreach (var row in rows) map.TryAdd(row.ChessResultsId, (kind, system));
        }
        return map;
    }

    /// <summary>
    /// Holt eine Trefferliste vom Crawler. Eigener Client (<see cref="CrawlerClientName"/>) statt
    /// des geteilten Live-Proxys, weil eine Suche hinter dem Rate-Limiter des Crawlers legitim
    /// laenger braucht als die 30 s, die fuer eine Live-Abfrage richtig sind.
    /// </summary>
    private async Task<JsonElement> FetchAsync(string path, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(CrawlerClientName);
        using var response = await client.GetAsync(path, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        return string.IsNullOrWhiteSpace(body)
            ? JsonSerializer.Deserialize<JsonElement>("[]")
            : JsonSerializer.Deserialize<JsonElement>(body);
    }

    // ----- Benachrichtigungen ----------------------------------------------

    private async Task NotifyChangedAsync(
        List<(TournamentDirectoryEntry Entry, string? OldDate, string? OldLocation)> changed, CancellationToken ct)
    {
        if (changed.Count == 0) return;

        // Ein Abo traegt die chess-results-Nummer; ein Eintrag ohne sie kann keine Abonnenten
        // haben und faellt hier heraus.
        var ids = changed.Select(c => c.Entry.ChessResultsId).Where(id => id is not null)
            .Select(id => id!).ToList();
        var subscribers = await SubscribersByTournamentAsync(ids, ct);

        foreach (var (entry, oldDate, oldLocation) in changed)
        {
            if (entry.ChessResultsId is null) continue;
            if (!subscribers.TryGetValue(entry.ChessResultsId, out var userIds)) continue;

            await _notifications.CreateManyAsync(userIds, NotificationType.TournamentChanged,
                new Dictionary<string, string>
                {
                    ["tournamentName"] = entry.Name,
                    ["oldDate"] = oldDate ?? "",
                    ["newDate"] = FormatRange(entry.StartDate, entry.EndDate) ?? "",
                    ["oldLocation"] = oldLocation ?? "",
                    ["newLocation"] = entry.LocationText ?? "",
                },
                DetailLink(entry.PublicId));
        }
    }

    private async Task NotifyCancelledAsync(List<TournamentDirectoryEntry> removed, CancellationToken ct)
    {
        if (removed.Count == 0) return;

        var subscribers = await SubscribersByTournamentAsync(
            removed.Select(e => e.ChessResultsId).Where(id => id is not null)
                .Select(id => id!).ToList(), ct);

        foreach (var entry in removed)
        {
            if (entry.ChessResultsId is null) continue;
            if (!subscribers.TryGetValue(entry.ChessResultsId, out var userIds)) continue;

            await _notifications.CreateManyAsync(userIds, NotificationType.TournamentCancelled,
                new Dictionary<string, string>
                {
                    ["tournamentName"] = entry.Name,
                    ["date"] = FormatRange(entry.StartDate, entry.EndDate) ?? "",
                },
                DetailLink(entry.PublicId));
        }
    }

    /// <summary>
    /// Eine Meldung je Suchprofil und Lauf, nicht eine je Turnier: ein naechtlicher Sweep legt
    /// hunderte Eintraege an, und ein Umkreis von 100 km faengt davon leicht zwanzig.
    /// </summary>
    public async Task<int> NotifyNearbyAsync(
        IReadOnlyList<int> newEntryIds, DateOnly today, CancellationToken ct = default)
    {
        var profiles = await _db.TournamentSearchProfiles.AsNoTracking()
            .Where(p => p.NotifyNew)
            .ToListAsync(ct);
        if (profiles.Count == 0) return 0;

        // Die Kandidaten werden in Bloecken geholt. Eine `Contains`-Liste uebersetzt der Provider
        // in inline-Konstanten; beim ERSTEN Lauf nach einem Deploy sind das ueber alle
        // Foederationen zusammen bis zu sechsstellig viele Nummern — ein Statement von mehreren
        // MB, das an `max_allowed_packet` scheitert. Der Wurf kaeme dann NACH allen erfolgreichen
        // Sweeps: die Daten stehen, aber keine einzige Umkreis-Meldung geht raus.
        var candidates = new List<TournamentDirectoryEntry>();
        foreach (var block in newEntryIds.Distinct().Chunk(NotifyLookupBatch))
        {
            candidates.AddRange(await _db.TournamentDirectoryEntries.AsNoTracking()
                .Where(e => block.Contains(e.Id)
                            && e.Lat != null && e.Lon != null
                            && e.RemovedAt == null
                            && e.StartDate != null && e.StartDate >= today)
                .ToListAsync(ct));
        }
        if (candidates.Count == 0) return 0;

        // Was ein Nutzer AUSGEBLENDET hat, wird ihm auch nicht gemeldet. Eine Benachrichtigung
        // ueber ein Turnier, das man weggeklickt hat, ist genau die Art Meldung, die einen dazu
        // bringt, alle abzuschalten. Nur die Nutzer mit meldenden Profilen und nur die
        // Kandidaten-Nummern werden geladen — das sind wenige Zeilen.
        var candidateIds = candidates.Select(e => e.PublicId).ToList();
        var userIds = profiles.Select(p => p.UserId).Distinct().ToList();
        var ignored = (await _db.TournamentDirectoryIgnores.AsNoTracking()
                .Where(i => userIds.Contains(i.UserId) && candidateIds.Contains(i.PublicId))
                .Select(i => new { i.UserId, i.PublicId })
                .ToListAsync(ct))
            .GroupBy(i => i.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PublicId).ToHashSet(StringComparer.Ordinal));

        var notified = 0;
        foreach (var profile in profiles)
        {
            var hidden = ignored.GetValueOrDefault(profile.UserId);
            var matches = candidates
                .Where(e => MatchesProfile(e, profile))
                .Where(e => hidden is null || !hidden.Contains(e.PublicId))
                .OrderBy(e => e.StartDate)
                .ToList();
            if (matches.Count == 0) continue;

            await _notifications.CreateAsync(profile.UserId, NotificationType.TournamentNearbyNew,
                new Dictionary<string, string>
                {
                    ["profileName"] = profile.Name,
                    ["count"] = matches.Count.ToString(CultureInfo.InvariantCulture),
                    ["firstName"] = matches[0].Name,
                    ["radiusKm"] = profile.RadiusKm.ToString(CultureInfo.InvariantCulture),
                },
                $"/tournaments/calendar?profile={profile.Id}");
            notified++;
        }
        return notified;
    }

    /// <summary>
    /// Passt ein Eintrag in den Umkreis und die Filter eines Profils? Erst die billige
    /// Bounding-Box, dann die teure Distanz - bei hunderten Kandidaten x Profilen zaehlt das.
    /// </summary>
    internal static bool MatchesProfile(TournamentDirectoryEntry entry, TournamentSearchProfile profile)
    {
        if (entry.Lat is not { } lat || entry.Lon is not { } lon) return false;

        var box = GeoDistance.BoundingBox(profile.Lat, profile.Lon, profile.RadiusKm);
        if (lat < box.MinLat || lat > box.MaxLat || lon < box.MinLon || lon > box.MaxLon) return false;
        if (GeoDistance.Haversine(profile.Lat, profile.Lon, lat, lon) > profile.RadiusKm) return false;

        if (SplitCsv(profile.Federations) is { Count: > 0 } feds
            && (entry.Federation is null || !feds.Contains(entry.Federation, StringComparer.OrdinalIgnoreCase)))
            return false;

        if (SplitCsv(profile.Speeds) is { Count: > 0 } speeds
            && !speeds.Contains(entry.Speed.ToString(), StringComparer.OrdinalIgnoreCase))
            return false;

        if (profile.WeekendOnly && entry.StartDate is { } start
            && start.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            return false;

        if (profile.MinPlayers is { } min && (entry.PlayerCount ?? 0) < min)
            return false;

        return true;
    }

    internal static List<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task<Dictionary<string, List<int>>> SubscribersByTournamentAsync(
        List<string> chessResultsIds, CancellationToken ct)
    {
        var rows = await _db.TournamentSubscriptions.AsNoTracking()
            .Where(s => chessResultsIds.Contains(s.CrawlerTournamentId))
            .Select(s => new { s.CrawlerTournamentId, s.UserId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CrawlerTournamentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(r => r.UserId).Distinct().ToList(), StringComparer.Ordinal);
    }

    // ----- Abbildung + Hilfsfunktionen -------------------------------------

    /// <summary>
    /// Uebertraegt eine Trefferzeile auf den Eintrag. <paramref name="artMap"/> ist das Ergebnis
    /// der vier Turnierart-Durchgaenge; <c>null</c> heisst „konnte nicht geklaert werden" und
    /// laesst eine bereits bekannte Art UND ein bekanntes System ausdruecklich in Ruhe.
    /// </summary>
    private void Apply(CrawlerDirectoryRow row, TournamentDirectoryEntry entry, DateTime now,
        Dictionary<string, (TournamentKind Kind, TournamentSystem System)>? artMap)
    {
        entry.Name = Truncate(row.Name, 500);
        entry.Federation = Truncate(row.Federation, 3);
        entry.State = Truncate(row.State, 100);
        entry.StartDate = row.StartDate;
        entry.EndDate = row.EndDate;
        entry.StartsOnWeekend = row.StartDate is { } d
            && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        entry.LocationText = Truncate(row.Location, 500);
        entry.TimeControlText = Truncate(row.TimeControl, 300);
        entry.Speed = TournamentSpeedClassifier.Classify(row.TimeControl);
        entry.Organizer = Truncate(row.Organizer, 300);
        entry.Director = Truncate(row.Director, 300);
        entry.ChiefArbiter = Truncate(row.ChiefArbiter, 300);
        entry.Rounds = row.Rounds;
        entry.PlayerCount = row.PlayerCount;
        entry.UpstreamUpdatedAt = row.LastUpdatedApproxUtc;
        entry.ChangeHash = ComputeChangeHash(row.StartDate, row.EndDate, row.Location);

        // Nur wenn ALLE vier Durchgaenge standen. Ein Turnier, das in keinem davon auftauchte,
        // bleibt bewusst unangetastet: die Arten decken zwar alles ab, was die Suche anbietet,
        // aber „in keiner Liste" ist eine Aussage ueber die Quelle, nicht ueber das Turnier.
        if (artMap is not null && artMap.TryGetValue(row.ChessResultsId, out var art))
        {
            entry.Kind = art.Kind;
            entry.System = art.System;
        }
        // Publikum und Format haengen am Namen (Alter/Geschlecht) bzw. an Art und Dauer (Liga) —
        // beides also NACH den Feldern oben und nach der Turnierart auswerten.
        entry.AgeGroups = TournamentClassifier.AgeGroupsOf(entry.Name);
        entry.Gender = TournamentClassifier.GenderOf(entry.Name);
        entry.IsLeague = TournamentClassifier.LooksLikeLeague(
            entry.Name, entry.Kind, entry.StartDate, entry.EndDate);

        // Nach Name, Termin und Ort - der Gruppenschluessel liest genau diese Felder.
        ApplyGrouping(entry);
        entry.LastSeenAt = now;
        entry.UpdatedAt = now;
        NoteSource(entry, DirectorySourceKind.ChessResults, row.ChessResultsId, now);
    }

    /// <summary>
    /// Vermerkt, dass dieses Turnier auf DIESER Seite gefunden wurde. Beim ersten Mal angelegt,
    /// danach nur der Zeitstempel — der sagt, ob die Quelle das Turnier noch fuehrt.
    ///
    /// <para>Dasselbe Turnier steht auf mehreren Seiten, und es werden mehr; ohne
    /// Herkunftsvermerk ist spaeter nicht zu sagen, woher eine Angabe kommt (siehe
    /// <see cref="TournamentDirectorySource"/>).</para>
    /// </summary>
    internal static void NoteSource(
        TournamentDirectoryEntry entry, DirectorySourceKind kind, string externalId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return;

        var source = entry.Sources.FirstOrDefault(
            s => s.Kind == kind && string.Equals(s.ExternalId, externalId, StringComparison.Ordinal));
        if (source is null)
        {
            entry.Sources.Add(new TournamentDirectorySource
            {
                Kind = kind,
                ExternalId = externalId,
                Url = UrlFor(kind, externalId),
                FirstSeenAt = now,
                LastSeenAt = now,
            });
            return;
        }
        source.LastSeenAt = now;
    }

    /// <summary>Die Seite, auf der das Turnier bei dieser Quelle steht.</summary>
    internal static string? UrlFor(DirectorySourceKind kind, string externalId) => kind switch
    {
        DirectorySourceKind.ChessResults => $"https://chess-results.com/tnr{externalId}.aspx?lan=1",
        DirectorySourceKind.Fide => $"https://calendar.fide.com/calendar.php?id={externalId}",
        _ => null,
    };

    /// <summary>
    /// Verortet einen Eintrag — mit ALLEN Spielorten, die im Ortstext stehen.
    ///
    /// <para>Die Koordinaten am Eintrag bleiben der HAUPT-Spielort (der erste): Kalender, Detail
    /// und der Bounding-Box-Index haengen daran. Die vollstaendige Liste steht in
    /// <see cref="TournamentDirectoryEntry.Venues"/>, und die Umkreissuche fragt sie — sonst faende
    /// sie ein Liga-Turnier nicht, das zur Haelfte vor der Haustuer stattfindet.</para>
    /// </summary>
    private async Task GeocodeAsync(TournamentDirectoryEntry entry, CancellationToken ct)
    {
        // Eine von Hand gesetzte Koordinate nie ueberschreiben - sie ist die Korrektur eines
        // Fehlgriffs und wuerde sonst jede Nacht zurueckfallen.
        if (entry.GeoSource == GeoSource.Manual) return;

        var results = await _geocoding.ResolveManyAsync(
            entry.LocationText, entry.State, entry.Federation, ct);

        // Mehrdeutig heisst: KEINE Koordinaten, aber ein Vermerk fuer die Arbeitsliste.
        var located = results.Where(r => r.Source != GeoSource.Ambiguous).ToList();
        var primary = located.FirstOrDefault();

        entry.Lat = primary?.Lat;
        entry.Lon = primary?.Lon;
        entry.GeoPlaceName = primary is null ? null : Truncate(primary.PlaceName, 200);
        entry.GeoSource = primary?.Source
            ?? (results.Any(r => r.Source == GeoSource.Ambiguous) ? GeoSource.Ambiguous : GeoSource.None);

        ReplaceVenues(entry, located);
    }

    /// <summary>
    /// Setzt die Spielorte neu. Vorhandene Zeilen werden geloescht und neu geschrieben statt
    /// abgeglichen: es sind hoechstens eine Handvoll je Turnier, und ein Abgleich ueber Namen
    /// waere aufwendiger als der Neuaufbau.
    /// </summary>
    private void ReplaceVenues(TournamentDirectoryEntry entry, List<GeocodeResult> located)
    {
        if (entry.Venues.Count > 0) _db.TournamentDirectoryVenues.RemoveRange(entry.Venues);
        entry.Venues = [];

        // Ein einzelner Spielort braucht keine Zeile — er steht schon am Eintrag. Die Tabelle
        // traegt nur, was dort NICHT abbildbar ist.
        if (located.Count < 2) return;

        for (var i = 0; i < located.Count; i++)
        {
            entry.Venues.Add(new TournamentDirectoryVenue
            {
                Ordinal = i,
                Name = Truncate(located[i].PlaceName, 200),
                SourceText = located[i].SourceText is { } t ? Truncate(t, 300) : null,
                Lat = located[i].Lat,
                Lon = located[i].Lon,
                GeoSource = located[i].Source,
            });
        }
    }

    /// <summary>
    /// Hash ueber genau die Felder, deren Aenderung eine Meldung wert ist: Termin und Spielort.
    /// Teilnehmerzahl, Schiedsrichter oder ein neuer "Last update"-Zeitstempel bleiben bewusst
    /// draussen - sonst meldet jede wachsende Meldeliste eine "Aenderung".
    /// </summary>
    /// <summary>
    /// Schluessel, der die Gruppen EINES Turniers zusammenfasst: Basisname, Foederation, Termin und
    /// Ort muessen uebereinstimmen. Alle vier zusammen, weil der Name allein nicht reicht — ein
    /// Vereinsabend, der jede Woche „KK Bardejov" heisst, waere sonst ein einziger Eintrag.
    /// </summary>
    /// <summary>
    /// Setzt Basisname und Gruppenschluessel aus den bereits gefuellten Feldern des Eintrags.
    /// Eine Stelle fuer beides, weil der Schluessel den Basisnamen braucht — und weil das
    /// Nachtragen im Altbestand (<see cref="TournamentGroupingBackfillService"/>) genau dieselbe
    /// Rechnung machen MUSS, sonst gruppiert der Bestand anders als der naechste Sweep.
    /// </summary>
    internal static void ApplyGrouping(TournamentDirectoryEntry entry)
    {
        entry.BaseName = Truncate(TournamentNameGrouping.BaseName(entry.Name), 500);
        entry.GroupKey = ComputeGroupKey(entry);
    }

    internal static string ComputeGroupKey(TournamentDirectoryEntry entry)
    {
        var payload = string.Join('|',
            GroupText(entry.BaseName ?? entry.Name),
            entry.Federation ?? "",
            entry.StartDate?.ToString("yyyy-MM-dd") ?? "",
            entry.EndDate?.ToString("yyyy-MM-dd") ?? "",
            GroupText(entry.LocationText));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()[..32];
    }

    /// <summary>
    /// Der Textanteil des Gruppenschluessels.
    ///
    /// <para><c>GeoTextNormalizer.Normalize</c> wirft alles weg, was nach der Diakritika-Zerlegung
    /// nicht <c>[a-z0-9]</c> ist — bei kyrillischer, griechischer oder ostasiatischer Schrift ist
    /// das der GANZE Text. Zwei verschiedene bulgarische Turniere am selben Ort und Termin haetten
    /// damit denselben Schluessel bekommen und waeren in Liste und Kalender zu EINEM verschmolzen;
    /// eines davon waere unsichtbar geworden. Genau der Fehler, den der Klassenkommentar von
    /// <see cref="TournamentNameGrouping"/> als den teureren bezeichnet — die Rotation faehrt alle
    /// 261 Foederationen ab, darunter BUL, GRE, RUS, UKR, SRB, GEO, ARM, CHN, JPN, KOR.</para>
    ///
    /// <para>Bleibt nach dem Normalisieren nichts uebrig, entscheidet deshalb der Rohtext
    /// (getrimmt, kleingeschrieben). Der ist weniger robust gegen Schreibvarianten — aber
    /// „zusammengefasst, was nicht zusammengehoert" ist der schlimmere Ausgang als „getrennt
    /// gelassen, was zusammengehoert".</para>
    /// </summary>
    private static string GroupText(string? value)
    {
        var normalized = GeoTextNormalizer.Normalize(value);
        return normalized.Length > 0 ? normalized : (value ?? "").Trim().ToLowerInvariant();
    }

    internal static string ComputeChangeHash(DateOnly? start, DateOnly? end, string? location)
    {
        var payload = string.Join('|',
            start?.ToString("yyyy-MM-dd") ?? "",
            end?.ToString("yyyy-MM-dd") ?? "",
            (location ?? "").Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()[..32];
    }

    internal static string? FormatRange(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null) return null;
        if (start is null) return end!.Value.ToString("yyyy-MM-dd");
        if (end is null || end == start) return start.Value.ToString("yyyy-MM-dd");
        return $"{start.Value:yyyy-MM-dd} - {end.Value:yyyy-MM-dd}";
    }

    private static string DetailLink(string publicId) => $"/tournaments/calendar?t={publicId}";

    private static string Truncate(string? value, int max) =>
        value is null ? "" : value.Length <= max ? value : value[..max];

    // ----- Antwort des Crawlers --------------------------------------------

    internal sealed record CrawlerDirectoryRow(
        string ChessResultsId, string Name, string? Federation, string? State,
        DateOnly? StartDate, DateOnly? EndDate, string? Location, string? TimeControl,
        string? Director, string? Organizer, string? ChiefArbiter,
        int? Rounds, int? PlayerCount, DateTime? LastUpdatedApproxUtc);

    internal static List<CrawlerDirectoryRow> ParseRows(JsonElement json)
    {
        var rows = new List<CrawlerDirectoryRow>();
        if (json.ValueKind != JsonValueKind.Array) return rows;

        foreach (var element in json.EnumerateArray())
        {
            var id = Str(element, "chessResultsId");
            var name = Str(element, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

            rows.Add(new CrawlerDirectoryRow(
                id, name,
                Str(element, "federation"), Str(element, "state"),
                Date(element, "startDate"), Date(element, "endDate"),
                Str(element, "location"), Str(element, "timeControl"),
                Str(element, "director"), Str(element, "organizer"), Str(element, "chiefArbiter"),
                Int(element, "rounds"), Int(element, "playerCount"),
                Timestamp(element, "lastUpdatedApproxUtc")));
        }
        return rows;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static DateOnly? Date(JsonElement e, string prop) =>
        DateOnly.TryParseExact(Str(e, prop), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;

    private static DateTime? Timestamp(JsonElement e, string prop) =>
        DateTime.TryParse(Str(e, prop), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
}
