using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

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
public class ChessSkDirectorySweepService
{
    private const int SaveEvery = 25;

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
                var publicId = $"sk{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);

                // Schulung, Seminar, Trainingslager: nichts anlegen, Bestehendes zurueckziehen.
                if (row.NonTournament)
                {
                    if (own is not null && own.RemovedAt is null)
                    {
                        own.RemovedAt = now;
                        retired++;
                    }
                    continue;
                }

                // Erst die Nummer, dann der Name: die Quelle nennt die chess-results-Nummer bei
                // einem Drittel der Eintraege selbst, und das ist der einzige Weg, der nicht raten
                // muss.
                var match = await ExternalDirectorySource.FindByChessResultsIdAsync(
                                _db, row.ChessResultsId, ct)
                            ?? await ExternalDirectorySource.FindMatchAsync(
                                _db, row.Federation, start, row.Name,
                    new ExternalDirectorySource.MatchHint(
                        DirectorySourceKind.SlovakChessFederation, row.EventId, LocationOf(row)), ct);

                if (match is not null)
                {
                    if (FillGaps(match, row)) updated++;
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.SlovakChessFederation, row.EventId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("chess.sk: {PublicId} geht in {Target} auf ({Name})",
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
                        // Nennt die Quelle eine chess-results-Nummer, wurde sie oben schon zur
                        // Zuordnung benutzt und hat KEINEN Eintrag gefunden — dann ist das Turnier
                        // dort ausgeschrieben, aber der Sweep hat es noch nicht gesehen (er liest
                        // die Turniersuche, die sich erst mit dem Datei-Upload fuellt). Die Nummer
                        // gehoert an den Eintrag: daran haengen Rundenplan, Abo und der Verweis.
                        ChessResultsId = row.ChessResultsId,
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }
                else if (own.ChessResultsId is null && row.ChessResultsId is { Length: > 0 })
                {
                    // Nachgereicht: der Veranstalter traegt die Swiss-Manager-Adresse oft erst
                    // spaeter nach.
                    own.ChessResultsId = row.ChessResultsId;
                }

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
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
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

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "chess.sk: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "chess.sk-Kalender: {Read} gelesen, {Added} neu, {Updated} ergaenzt, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.SlovakChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

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
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/chess-sk-calendar?from={from:yyyy-MM-dd}&details={(details ? "true" : "false")}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<ChessSkRow>>(body, JsonOptions) ?? [];
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
