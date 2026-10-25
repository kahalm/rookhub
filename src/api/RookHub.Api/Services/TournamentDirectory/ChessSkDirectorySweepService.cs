using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using RookHub.Api.Services.Tournaments;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender des slowakischen Verbands (chess.sk) als Quelle.
///
/// <para><b>Die reichhaltigste der Zusatzquellen.</b> Sie liefert als einzige alles auf einmal:
/// Anschrift MIT Postleitzahl, Bedenkzeit, Rundenzahl, Turniersystem und die Bedenkzeit-Klasse als
/// ausdrueckliche Angabe („Typ turnaja"). Der Preis: die Liste ist eine JSON-Schnittstelle, die
/// Details stehen aber je Turnier auf einer eigenen HTML-Seite — der Crawler holt sie mit Pause
/// und Deckel nach.</para>
///
/// <para><b>Der entscheidende Unterschied zu FSI und SZS: die Quelle nennt die
/// chess-results-Nummer selbst</b> (Feld „Swiss manager URL", 27 von 79 Eintraegen). Damit ist die
/// Zuordnung zum bestehenden Verzeichnis ein EXAKTER Schluessel. Der Namensvergleich bleibt fuer
/// die uebrigen — er kann zwei Turniere derselben Woche am selben Ort verwechseln, und genau das
/// passiert in der Slowakei leicht: die „ŠACH-MAT NITRA"-Liga steht dort mit dreizehn Runden unter
/// dreizehn fast gleichlautenden Namen.</para>
///
/// <para><b>Schulungen sind keine Turniere.</b> Der Kalender fuehrt Schiedsrichter-Lehrgaenge und
/// Trainingslager in derselben Liste (4 von 79) und hat kein Feld, das sie trennt. Sie werden
/// nicht angelegt und bestehende Eintraege zurueckgezogen — dieselbe Regel wie bei den abgesagten
/// Turnieren der slowenischen Quelle.</para>
/// </summary>
public class ChessSkDirectorySweepService : IDirectorySourceAdapter<ChessSkDirectorySweepService.CrawlerChessSkEvent>
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<ChessSkDirectorySweepService> _log;

    public ChessSkDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, ILogger<ChessSkDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(bool details = true, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, details, ct);
        return await ExternalSweepRunner.RunAsync(_db, _log, this, events, ct);
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<CrawlerChessSkEvent>.Kind => DirectorySourceKind.SlovakChessFederation;
    string IDirectorySourceAdapter<CrawlerChessSkEvent>.LogName => "chess.sk";
    string? IDirectorySourceAdapter<CrawlerChessSkEvent>.SourceKey(CrawlerChessSkEvent row) => row.EventId.Length == 0 ? null : row.EventId;
    DateOnly? IDirectorySourceAdapter<CrawlerChessSkEvent>.Start(CrawlerChessSkEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerChessSkEvent>.Name(CrawlerChessSkEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerChessSkEvent>.PublicId(CrawlerChessSkEvent row) => $"sk{row.EventId}";

    /// <summary>Schulung, Seminar, Trainingslager: nichts anlegen, Bestehendes zurueckziehen.</summary>
    bool IDirectorySourceAdapter<CrawlerChessSkEvent>.Withdrawn(CrawlerChessSkEvent row) => row.NonTournament;

    string? IDirectorySourceAdapter<CrawlerChessSkEvent>.Federation(CrawlerChessSkEvent row) => row.Federation;
    string? IDirectorySourceAdapter<CrawlerChessSkEvent>.MatchPlace(CrawlerChessSkEvent row) => LocationOf(row);
    string? IDirectorySourceAdapter<CrawlerChessSkEvent>.Url(CrawlerChessSkEvent row) => row.Url;

    /// <summary>
    /// Erst die Nummer, dann der Name: die Quelle nennt die chess-results-Nummer bei einem Drittel
    /// der Eintraege selbst, und das ist der einzige Weg, der nicht raten muss.
    /// </summary>
    string? IDirectorySourceAdapter<CrawlerChessSkEvent>.ChessResultsId(CrawlerChessSkEvent row) => NumberOf(row);

    /// <summary>
    /// Die chess-results-Nummer der Zeile, <c>null</c> wenn sie fehlt oder leer ist. Eine LEERE
    /// Nummer am Eintrag (<c>ChessResultsId = ""</c>) zaehlte als „mit Nummer": der Eintrag war
    /// Kandidat im Namensvergleich anderer Quellen, fiel aus der Verschwunden-Erkennung heraus,
    /// und auf der Spalte liegt ein eindeutiger Index — ein zweiter solcher Eintrag haette das
    /// Speichern des ganzen Laufs abgebrochen.
    /// </summary>
    private static string? NumberOf(CrawlerChessSkEvent row) =>
        string.IsNullOrWhiteSpace(row.ChessResultsId) ? null : row.ChessResultsId;

    bool IDirectorySourceAdapter<CrawlerChessSkEvent>.FillMatch(TournamentDirectoryEntry match, CrawlerChessSkEvent row) => FillGaps(match, row);

    /// <summary>
    /// Nennt die Quelle eine chess-results-Nummer, wurde sie schon zur Zuordnung benutzt und hat
    /// KEINEN Eintrag gefunden — dann ist das Turnier dort ausgeschrieben, aber der Sweep hat es
    /// noch nicht gesehen (er liest die Turniersuche, die sich erst mit dem Datei-Upload fuellt).
    /// Die Nummer gehoert an den Eintrag: daran haengen Rundenplan, Abo und der Verweis. Ein neuer
    /// Eintrag uebernimmt sie; eine leere bleibt <c>null</c> (<see cref="NumberOf"/>).
    /// </summary>
    void IDirectorySourceAdapter<CrawlerChessSkEvent>.OnCreated(TournamentDirectoryEntry own, CrawlerChessSkEvent row) => own.ChessResultsId = NumberOf(row);

    async Task<bool> IDirectorySourceAdapter<CrawlerChessSkEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerChessSkEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        // Nachgereicht: der Veranstalter traegt die Swiss-Manager-Adresse oft erst spaeter nach.
        // (Ein neuer Eintrag hat die Nummer der Zeile schon aus OnCreated.)
        if (own.ChessResultsId is null && NumberOf(row) is { } number)
            own.ChessResultsId = number;

        var location = LocationOf(row);
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.Federation = row.Federation;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        own.TimeControlText = ExternalDirectorySource.Truncate(row.TimeControl, 300);
        own.Speed = SpeedOf(row);
        own.System = SystemOf(row.System);
        own.Rounds = row.Rounds;
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.SlovakChessFederation, row.EventId, row.Url, now, ct);

        // Ein ONLINE-Turnier hat keinen Spielort. Ihm einen Pin zu geben waere eine
        // Behauptung ueber die Wirklichkeit — dieselbe Regel wie bei den ONL-Ereignissen
        // des FIDE-Kalenders.
        if (!row.Online && (locationChanged || own.Lat is null))
        {
            var hit = await _geocoding.ResolveAsync(own.LocationText, null, own.Federation, ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                own.Lat = hit.Lat;
                own.Lon = hit.Lon;
                own.GeoSource = hit.Source;
                own.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
        // „Ergaenzt" zaehlt bei dieser Quelle nur die Luecken am chess-results-Eintrag (FillMatch).
        return false;
    }

    void IDirectorySourceAdapter<CrawlerChessSkEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "chess.sk-Kalender: {Read} gelesen, {Added} neu, {Updated} ergaenzt, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);

    /// <summary>
    /// Was die Quelle einem BESTEHENDEN chess-results-Eintrag beitragen darf: nur Luecken. Die
    /// Turniersuche ist der gepflegte Bestand — eine Zusatzquelle, die vorhandene Werte ersetzt,
    /// macht die Reihenfolge der naechtlichen Durchgaenge zum Entscheider.
    /// </summary>
    private static bool FillGaps(TournamentDirectoryEntry entry, CrawlerChessSkEvent row)
    {
        var text = entry.TimeControlText;
        var rounds = entry.Rounds;
        var changed = ExternalDirectorySource.FillIfEmpty(row.TimeControl, ref text, 300);
        changed |= ExternalDirectorySource.FillIfEmpty(row.Rounds, ref rounds);
        entry.TimeControlText = text;
        entry.Rounds = rounds;

        if (entry.Speed == TournamentSpeed.Unknown && SpeedOf(row) is var speed
            && speed != TournamentSpeed.Unknown)
        {
            entry.Speed = speed;
            changed = true;
        }

        if (entry.System == TournamentSystem.Unknown && SystemOf(row.System) is var system
            && system != TournamentSystem.Unknown)
        {
            entry.System = system;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Die Bedenkzeit-KLASSE. Die Quelle nennt sie beim Namen („Typ turnaja": Standard / Rapid /
    /// Blitz / Online) — das ist eine Angabe und wird deshalb abgebildet. Nur wo sie fehlt (16 von
    /// 79 stehen auf „Nie je nastavené"), wird sie aus dem Bedenkzeit-Text erschlossen.
    ///
    /// <para><b>„Online" ist keine Bedenkzeit</b>, sondern eine Aussage ueber den Ort. Es zaehlt
    /// deshalb wie eine fehlende Angabe und die Klasse wird aus dem Text erschlossen — ein
    /// Online-Turnier ueber 2 × 15 Minuten IST ein Schnellschach.</para>
    /// </summary>
    internal static TournamentSpeed SpeedOf(CrawlerChessSkEvent row) =>
        row.Type?.Trim().ToLowerInvariant() switch
        {
            "standard" => TournamentSpeed.Standard,
            "rapid" => TournamentSpeed.Rapid,
            "blitz" => TournamentSpeed.Blitz,
            _ => TournamentSpeedClassifier.Classify(row.TimeControl),
        };

    internal static TournamentSystem SystemOf(string? system) => system switch
    {
        "swiss" => TournamentSystem.Swiss,
        "roundRobin" => TournamentSystem.RoundRobin,
        _ => TournamentSystem.Unknown,
    };

    /// <summary>
    /// Der Ortstext fuer die Verortung: die ANSCHRIFT, wenn es eine gibt (sie traegt bei rund der
    /// Haelfte der Eintraege eine Postleitzahl — den genauesten Weg des
    /// <see cref="GeocodingService"/>), sonst der blosse Ort.
    ///
    /// <para>Fehlt der Ortsname in der Anschrift („Kultúrny dom Drážovce", „Mlynská 27"), wird er
    /// HINTEN angehaengt: bei einem Text MIT Ziffer gilt der LETZTE Ortstreffer als der eine
    /// Spielort, und das ist dann genau er.</para>
    ///
    /// <para><b>Der Stadtteil faellt weg</b> („Bratislava - mestská časť Rača" → „Bratislava",
    /// 30 von 79 Eintraegen). Im Lexikon stehen Gemeinden, keine Stadtteile; stehen bliebe der
    /// Zusatz, suchte die Verortung nach „Rača" und faende entweder nichts oder einen
    /// gleichnamigen Ort anderswo. Wo der Stadtteil wirklich zaehlt, steht die Postleitzahl in der
    /// Anschrift und entscheidet ohnehin genauer.</para>
    /// </summary>
    internal static string? LocationOf(CrawlerChessSkEvent row)
    {
        var city = CityOf(row.City);
        var address = row.Address?.Trim();

        if (address is not { Length: > 0 }) return city;
        if (city is not { Length: > 0 }) return address;

        return address.Contains(city, StringComparison.OrdinalIgnoreCase)
            ? address
            : $"{address}, {city}";
    }

    /// <summary>„Bratislava - mestská časť Rača" → „Bratislava".</summary>
    internal static string? CityOf(string? city)
    {
        if (city is not { Length: > 0 }) return null;

        var trimmed = BoroughPattern.Split(city.Trim())[0].Trim(' ', ',', '-');
        return trimmed.Length > 0 ? trimmed : null;
    }

    private static readonly Regex BoroughPattern =
        new(@"\s*[-–]\s*mestsk[áa]\s+čas[ťt]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessSkEvent>> FetchAsync(
        DateOnly from, bool details, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<ChessSkRow>>(
            $"/api/chess-sk-calendar?from={from:yyyy-MM-dd}&details={(details ? "true" : "false")}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessSkEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate),
                r.City, r.Address, FederationOf(r.Country), r.ChessResultsId, r.Url,
                r.TimeControl, r.System, r.Rounds, r.Type, r.NonTournament))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    /// <summary>
    /// Der Staat der Quelle ist schon ein ISO-3-Kuerzel („SVK", vereinzelt „CZE") und damit
    /// dasselbe, was chess-results als Foederation fuehrt. Fehlt er, gilt die Slowakei — es ist
    /// ihr Kalender.
    /// </summary>
    private static string FederationOf(string? country) =>
        country is { Length: 3 } ? country.ToUpperInvariant() : "SVK";

    private sealed record ChessSkRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? City,
        string? Address, string? Country, string? ChessResultsId, string? Website, string? Url,
        string? SystemText, string? TimeControl, string? System, int? Rounds, string? Type,
        bool NonTournament);

    public sealed record CrawlerChessSkEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? City, string? Address,
        string Federation, string? ChessResultsId, string? Url, string? TimeControl,
        string? System, int? Rounds, string? Type, bool NonTournament)
    {
        /// <summary>Die Quelle fuehrt Online-Turniere als eigene Art — sie haben keinen Spielort.</summary>
        public bool Online => string.Equals(Type, "online", StringComparison.OrdinalIgnoreCase);
    }
}
