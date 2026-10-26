using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments;
using RookHub.Api.Services.Social;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

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
    private readonly DirectorySweepMerge _merge;
    private readonly DirectoryNotifier _notifier;
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
        _merge = new DirectorySweepMerge(db, new DirectoryGeocoder(db, geocoding));
        _notifier = new DirectoryNotifier(db, notifications);
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

            // Je Foederation gefangen: SweepFederationAsync faengt nur den ABRUF, sein SaveChanges
            // steht ausserhalb. Ein Speicherfehler (Unique-Index, „Data too long") in einer
            // Foederation riss sonst die uebrigen, die Umkreis-Meldung und im Nachtlauf alle
            // folgenden Schritte mit.
            try
            {
                var (result, added) = await SweepFederationAsync(federation, today, ct);
                results.Add(result);
                newEntryIds.AddRange(added);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                results.Add(await RecordFailedSweepAsync(federation, ex, ct));
            }
        }

        if (newEntryIds.Count > 0)
            await _notifier.NotifyNearbyAsync(newEntryIds, today, ct);

        return results;
    }

    /// <summary>
    /// Eine Foederation ist NACH dem Abruf gescheitert, meist beim Speichern.
    ///
    /// <para>Zuerst wird der Change-Tracker geleert: die gescheiterten Added/Modified-Eintraege
    /// blieben sonst darin, und jedes folgende SaveChanges — die naechste Foederation, die
    /// Umkreis-Meldung — schickte sie erneut ab und scheiterte an derselben Ausnahme. Danach
    /// landet der Fehler wie beim Abruffehler an der Sweep-Zeile; LastSweptAt bleibt alt, die
    /// Rotation nimmt die Foederation also wieder vor.</para>
    /// </summary>
    private async Task<DirectorySweepResult> RecordFailedSweepAsync(
        string federation, Exception error, CancellationToken ct)
    {
        federation = federation.Trim().ToUpperInvariant();
        // Die innerste Meldung: bei einer DbUpdateException steht der eigentliche Grund
        // („Duplicate entry … for key …") erst in der InnerException.
        var message = error.GetBaseException().Message;
        _log.LogWarning(error, "Verzeichnis-Sweep {Federation} fehlgeschlagen", federation);
        _db.ChangeTracker.Clear();

        try
        {
            var sweep = await _db.TournamentDirectorySweeps.FirstOrDefaultAsync(s => s.Federation == federation, ct);
            if (sweep is null)
            {
                sweep = new TournamentDirectorySweep { Federation = federation };
                _db.TournamentDirectorySweeps.Add(sweep);
            }
            sweep.LastAttemptedAt = DateTime.UtcNow;
            sweep.LastError = Truncate(message, 500);
            sweep.ConsecutiveFailures++;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Nicht einmal der Vermerk ging durch (Datenbank weg) — dann wenigstens nichts
            // Halbes im Tracker fuer die naechste Foederation zuruecklassen.
            _db.ChangeTracker.Clear();
            _log.LogWarning(ex, "Verzeichnis-Sweep {Federation}: Fehlschlag nicht vermerkt", federation);
        }

        return new DirectorySweepResult(federation, 0, 0, 0, 0, 0, message);
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

        List<DirectorySearchRows.CrawlerDirectoryRow> rows;
        try
        {
            var path = $"/api/tournament-search?fed={Uri.EscapeDataString(federation)}" +
                       $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&maxRows={MaxRows}";
            rows = DirectorySearchRows.ParseRows(await FetchAsync(path, ct));
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

        var artMap = await DirectoryArtPasses.FetchMapAsync(FetchAsync, _log, federation, from, to, MaxRows, ct);

        var (existing, byId) = await _merge.LoadKnownAsync(federation, from, rows, ct);

        var now = DateTime.UtcNow;
        var (added, changed, updated) = await _merge.ApplyRowsAsync(rows, byId, now, artMap, ct);
        var removed = MarkVanished(federation, rows, existing, now);

        sweep.LastSweptAt = now;
        sweep.LastRowCount = rows.Count;
        sweep.LastError = null;
        sweep.ConsecutiveFailures = 0;

        await _db.SaveChangesAsync(ct);

        await _notifier.NotifyChangedAsync(changed, ct);
        await _notifier.NotifyCancelledAsync(removed, today, ct);

        _log.LogInformation(
            "Verzeichnis-Sweep {Federation}: {Rows} Zeilen, {Added} neu, {Changed} geaendert, {Removed} abgesagt",
            federation, rows.Count, added.Count, changed.Count, removed.Count);

        return (new DirectorySweepResult(federation, rows.Count, added.Count, updated, changed.Count, removed.Count),
                added.Select(e => e.Id).ToList());
    }

    /// <summary>
    /// Was die Liste nicht mehr liefert: Fehlschlag zaehlen, ab <see cref="MissedSweepsUntilRemoved"/>
    /// als abgesagt markieren. Gibt die abgesagten Eintraege zurueck.
    /// </summary>
    private List<TournamentDirectoryEntry> MarkVanished(string federation,
        List<DirectorySearchRows.CrawlerDirectoryRow> rows, List<TournamentDirectoryEntry> existing, DateTime now)
    {
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

        return DirectorySweepMerge.MarkVanished(rows, existing, now, truncated, MissedSweepsUntilRemoved);
    }

    /// <summary>
    /// Holt eine Trefferliste vom Crawler. Eigener Client (<see cref="CrawlerClientName"/>) statt
    /// des geteilten Live-Proxys, weil eine Suche hinter dem Rate-Limiter des Crawlers legitim
    /// laenger braucht als die 30 s, die fuer eine Live-Abfrage richtig sind.
    ///
    /// <para>Ein leerer Rumpf ist eine leere Liste (<see cref="ParseRows"/> liest aus einem
    /// Nicht-Array keine Zeile).</para>
    /// </summary>
    private Task<JsonElement> FetchAsync(string path, CancellationToken ct) =>
        _httpClientFactory.GetCrawlerJsonAsync<JsonElement>(path, ct, emptyAsAbsent: true);

    /// <summary>
    /// Vermerkt, dass dieses Turnier auf DIESER Seite gefunden wurde. Beim ersten Mal angelegt,
    /// danach nur der Zeitstempel — der sagt, ob die Quelle das Turnier noch fuehrt.
    ///
    /// <para>Dasselbe Turnier steht auf mehreren Seiten, und es werden mehr; ohne
    /// Herkunftsvermerk ist spaeter nicht zu sagen, woher eine Angabe kommt (siehe
    /// <see cref="TournamentDirectorySource"/>).</para>
    /// </summary>
    /// <para>Reicht an <see cref="ExternalDirectorySource.NoteSourceAsync"/> durch und ergaenzt nur
    /// die Adresse, die sich hier aus der Quellenart ableiten laesst. Vorher stand hier eine EIGENE
    /// Fassung der Einfuege-Logik — die dritte im Haus, und wie die beiden anderen sah sie nur die
    /// Vermerke des uebergebenen Eintrags. Verschiebt sich die Zuordnung einer Kennung auf einen
    /// anderen Eintrag, starb der Lauf am eindeutigen Index (Kind, ExternalId); genau das ist am
    /// 2026-09-09 drei Quellen passiert. Der FIDE-Kalender verschmilzt Ereignisse regelmaessig in
    /// bestehende Eintraege (heute 7 Stueck) und lief damit in dieselbe Falle.</para>
    internal static Task NoteSourceAsync(AppDbContext db,
        TournamentDirectoryEntry entry, DirectorySourceKind kind, string externalId, DateTime now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return Task.CompletedTask;

        return ExternalDirectorySource.NoteSourceAsync(
            db, entry, kind, externalId, UrlFor(kind, externalId), now, ct);
    }

    /// <summary>Die Seite, auf der das Turnier bei dieser Quelle steht.</summary>
    internal static string? UrlFor(DirectorySourceKind kind, string externalId) => kind switch
    {
        DirectorySourceKind.ChessResults => $"https://chess-results.com/tnr{externalId}.aspx?lan=1",
        DirectorySourceKind.Fide => $"https://calendar.fide.com/calendar.php?id={externalId}",
        _ => null,
    };

    // ----- Fassade: Weiterleitungen fuer bestehende Aufrufer -----------------
    // Die Rechnungen stehen seit der Zerlegung (Codereview 2026-09-29, Z8) in DirectoryKeys,
    // DirectoryNotifier und DirectorySearchRows; die Namen hier bleiben, damit Aufrufer ausserhalb
    // des Bereichs (TournamentGroupingBackfillService) und die Tests unveraendert weiterlaufen.

    /// <inheritdoc cref="DirectoryNotifier.NotifyNearbyAsync"/>
    public Task<int> NotifyNearbyAsync(IReadOnlyList<int> newEntryIds, DateOnly today, CancellationToken ct = default) =>
        _notifier.NotifyNearbyAsync(newEntryIds, today, ct);

    /// <inheritdoc cref="DirectoryNotifier.MatchesProfile"/>
    internal static bool MatchesProfile(TournamentDirectoryEntry entry, TournamentSearchProfile profile) =>
        DirectoryNotifier.MatchesProfile(entry, profile);

    /// <inheritdoc cref="DirectoryKeys.ApplyGrouping"/>
    internal static void ApplyGrouping(TournamentDirectoryEntry entry) => DirectoryKeys.ApplyGrouping(entry);

    /// <inheritdoc cref="DirectoryKeys.ComputeGroupKey"/>
    internal static string ComputeGroupKey(TournamentDirectoryEntry entry) => DirectoryKeys.ComputeGroupKey(entry);

    /// <inheritdoc cref="DirectoryKeys.GroupPlace"/>
    internal static string GroupPlace(TournamentDirectoryEntry entry) => DirectoryKeys.GroupPlace(entry);

    /// <inheritdoc cref="DirectoryKeys.ComputeChangeHash"/>
    internal static string ComputeChangeHash(DateOnly? start, DateOnly? end, string? location) =>
        DirectoryKeys.ComputeChangeHash(start, end, location);

    /// <inheritdoc cref="DirectoryKeys.FormatRange"/>
    internal static string? FormatRange(DateOnly? start, DateOnly? end) => DirectoryKeys.FormatRange(start, end);

    /// <inheritdoc cref="DirectorySearchRows.ParseRows"/>
    internal static List<DirectorySearchRows.CrawlerDirectoryRow> ParseRows(JsonElement json) =>
        DirectorySearchRows.ParseRows(json);
}
