using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Kalender des franzoesischen Verbands (FFE) als Quelle.
///
/// <para><b>Der groesste Einzel-Zugewinn der Quellenrunde.</b> Von 40 gegengeprueften
/// FFE-Turnieren stehen <b>zwei</b> auf chess-results — 5 %. Die FFE fuehrt die
/// nicht-FIDE-gewerteten Vereins- und Ligue-Turniere, und die kennt chess-results praktisch gar
/// nicht; ihre Wertung laeuft ueber die eigene Datenbank und braucht chess-results nicht.</para>
///
/// <para><b>Zweistufig, wie chessarbiter.</b> Die Monatsliste kostet EINEN Abruf je Monat und
/// bringt Nummer, Name, Starttag, Ort und Departement. Enddatum, Anschrift, Bedenkzeit,
/// Rundenzahl und Paarungsverfahren stehen auf der Turnierseite — ein Abruf je Turnier, deshalb
/// <b>nur fuer die, die wir noch nicht kennen</b>, gedeckelt und mit Pause.</para>
///
/// <para><b>Warum die Turnierseite ueberhaupt geholt wird, obwohl sie teuer ist.</b> Ohne sie
/// fehlen drei Dinge, die der Bestand nirgendwo anders herbekommt:</para>
/// <list type="number">
/// <item><b>Das ENDDATUM.</b> Die Liste nennt nur den Starttag. Ohne Ende stuende jedes
/// mehrtaegige Open im Kalender nur an seinem ersten Tag — und die Mehrzahl der franzoesischen
/// Turniere laeuft ueber ein Wochenende oder laenger.</item>
/// <item><b>Die BEDENKZEIT.</b> Ohne sie bliebe <see cref="TournamentSpeed.Unknown"/> fuer
/// jedes franzoesische Turnier stehen, und der Speed-Filter des Verzeichnisses waere fuer
/// Frankreich wirkungslos. Die Liste sagt dazu kein Wort.</item>
/// <item><b>Die POSTLEITZAHL in der Anschrift.</b> Sie ist der einzige Weg des
/// <see cref="GeocodingService"/>, der auf wenige Kilometer genau ist. Die Liste hat nur den
/// Ortsnamen — und Frankreich hat reichlich gleichnamige Orte, bei denen der Namensweg dann
/// <see cref="GeoSource.Ambiguous"/> liefert und gar keinen Pin setzt.</item>
/// </list>
///
/// <para>Gemessen: von neun Turnierseiten trugen 9 das Enddatum, 9 die Bedenkzeit, 9 die
/// Rundenzahl und 8 eine Anschrift, davon 7 mit Postleitzahl. Der Abruf lohnt sich also fast
/// immer — und er faellt genau einmal je Turnier an.</para>
///
/// <para><b>Wie weit der Kalender reicht (gemessen 09.09.2026).</b> September 105, Oktober 43,
/// November 12, Dezember 10 — danach Januar 0, Februar 8, Maerz 1, April 3, Mai 0, Juni 2, ab
/// Juli 0. Zwoelf Monatslisten kosten 12 GET + 3 Postbacks; der Ertrag steckt in den ersten vier
/// Monaten, weiter voraus stehen nur Fruehstarter. Ein Durchgang muss deshalb ROLLIEREND
/// nachfassen — ein einmaliger Blick weit voraus bringt nichts.</para>
///
/// <para><b>Das DEPARTEMENT ist der Unterscheider, den diese Quelle mitbringt.</b> Es steht in
/// der Liste (zweistellig, „58") und ist zugleich der Anfang jeder Postleitzahl des Departements.
/// Es wandert deshalb in den Ortstext und, ausgeschrieben als „Dept. 58", in die Regionsspalte —
/// die Ligue-Spalte der Quelle taugt dafuer NICHT: sie sagt, wer die Wertung fuehrt, nicht wo
/// gespielt wird, und in vier von fuenf Faellen steht dort ohnehin „FFE".</para>
///
/// <para><b>Rechtslage.</b> <c>robots.txt</c> auf beiden Hostnamen 404 (2026-09-09 selbst
/// geprueft) — nichts gesperrt. ABER die „Mentions legales" der FFE berufen sich in Punkt 7
/// ausdruecklich auf das sui-generis-DATENBANKRECHT (Gesetz vom 1. Juli 1998, Umsetzung der
/// EU-Richtlinie 96/9/EG). Der Text liest sich wie generisches franzoesisches
/// Vereins-Boilerplate und nicht wie eine gegen diesen Kalender gerichtete Klausel — ein Verband
/// veroeffentlicht seine Ankuendigungen ja aktiv, ohne Anmeldung und ohne technische Huerde.
/// Er ist trotzdem echt, und er ist der Grund fuer die Sparsamkeit hier: die Liste rollierend,
/// die Turnierseite genau einmal je Turnier, gedeckelt. Eine kurze Anfrage beim oeffentlich
/// genannten Webmaster bleibt vor dem Dauerbetrieb das saubere Vorgehen.</para>
/// </summary>
public class FfeDirectorySweepService
{
    private const int SaveEvery = 25;

    /// <summary>
    /// Wie viele Monatslisten ein Durchgang liest. Zwoelf, obwohl der Ertrag in den ersten vier
    /// steckt: die acht weiteren kosten je einen Abruf von 18 kB und fangen die Fruehstarter
    /// (Februar 8, April 3, Juni 2) — das ist billiger als eine zweite Rotationsregel.
    /// </summary>
    public const int DefaultMonths = 12;

    /// <summary>
    /// Pause zwischen zwei Turnierseiten. Der Abruf laeuft ueber den Crawler, der seinerseits
    /// wartet — diese Pause ist die zweite Bremse und der sichtbare Ausdruck davon, dass wir hier
    /// wegen der Datenbankschutz-Klausel hoeflich holen.
    /// </summary>
    private static readonly TimeSpan DetailDelay = TimeSpan.FromMilliseconds(600);

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly int _detailBatchSize;
    private readonly ILogger<FfeDirectorySweepService> _log;

    public FfeDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, IConfiguration configuration,
        ILogger<FfeDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _detailBatchSize = configuration.GetValue("TournamentDirectory:FfeDetailBatchSize", 150);
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(
        int months = DefaultMonths, int? detailLimit = null, CancellationToken ct = default)
    {
        var events = await FetchListAsync(months, ct);
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var budget = Math.Max(0, detailLimit ?? _detailBatchSize);
        var now = DateTime.UtcNow;
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;
        // Was DIESER Lauf geliefert hat — Grundlage der Verschwunden-Erkennung unten.
        var delivered = new List<string>();

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = $"fr{row.EventId}";
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(_db, "FRA", start, row.Name, ct);
                await EnsureSourcesLoadedAsync(own, ct);
                await EnsureSourcesLoadedAsync(match, ct);

                if (match is not null)
                {
                    // chess-results kennt das Turnier — bei Frankreich der Ausnahmefall (2 von 40).
                    // Dann bleibt es bei einem Herkunftsvermerk: die Turnierseite zu holen, nur um
                    // Luecken zu fuellen, waere ein Abruf fuer einen Eintrag, der schon steht.
                    delivered.Add(row.EventId);
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.FrenchChessFederation, row.EventId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("FFE: {PublicId} geht in {Target} auf ({Name})",
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
                        // Die FFE kennt keine chess-results-Nummer. Alles, was dort gebraucht wird
                        // (Rundenplan, Vereins-Aufloesung, Abo), bleibt deshalb aus.
                        ChessResultsId = null,
                        Federation = "FRA",
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                var location = LocationOf(row);
                var locationChanged = own.LocationText != location;

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.StartDate = start;
                // Die Liste kennt nur den Starttag. Ein schon geholtes Ende bleibt stehen — es
                // kam von der Turnierseite und ist die genauere Angabe.
                own.EndDate = own.EndDate is { } end && end > start ? end : start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.State = ExternalDirectorySource.Truncate(RegionOf(row.Department), 100);
                own.LocationText = ExternalDirectorySource.Truncate(location, 300);
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);

                // Der Detailabruf lohnt nur einmal je Turnier. „Schon geholt" steht in der Adresse
                // des Herkunftsvermerks — sie wird erst dabei gesetzt (dasselbe Verfahren wie bei
                // chessarbiter; es braucht keine eigene Spalte, weil der Vermerk genau das sagt).
                var hasDetail = HasDetail(own, row.EventId);
                if (!hasDetail && budget > 0)
                {
                    budget--;
                    if (await LoadDetailAsync(own, row, ct))
                    {
                        updated++;
                        hasDetail = true;
                        locationChanged = true;   // die Anschrift traegt die Postleitzahl
                    }
                    await Task.Delay(DetailDelay, ct);
                }

                delivered.Add(row.EventId);
                await ExternalDirectorySource.NoteSourceAsync(_db, own, DirectorySourceKind.FrenchChessFederation,
                    row.EventId, hasDetail ? row.Url : null, now, ct);

                if (own.LocationText is { Length: > 0 } && (locationChanged || own.Lat is null))
                {
                    var hit = await _geocoding.ResolveAsync(own.LocationText, own.State, "FRA", ct);
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
            catch (Exception ex) { _log.LogWarning(ex, "FFE: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "FFE-Kalender: {Read} gelesen, {Added} neu, {Updated} mit Turnierseite, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        // Was die Quelle nicht mehr liefert, wird zurueckgezogen (zwei Laeufe Karenz,
        // Bremse gegen halbe Laeufe — siehe RetireVanishedAsync).
        retired += await ExternalDirectorySource.RetireVanishedAsync(
            _db, DirectorySourceKind.FrenchChessFederation, delivered, now, ct);

        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

    /// <summary>
    /// Die Herkunftsvermerke eines aus der Datenbank geholten Eintrags nachladen.
    ///
    /// <para><b>Warum das hier steht und nicht in <see cref="ExternalDirectorySource"/>.</b>
    /// <c>FindOwnAsync</c> und <c>FindMatchAsync</c> holen den Eintrag OHNE
    /// <c>Include(e =&gt; e.Sources)</c>, und Lazy Loading ist nicht eingeschaltet. In einem
    /// frischen Scope — also in jeder Nacht ausser der ersten — ist die Sammlung damit leer, und
    /// daran haengen hier zwei Dinge: <see cref="HasDetail"/> saehe die schon gelesene
    /// Turnierseite nicht und holte sie jede Nacht erneut (bei Frankreich rund 180 Abrufe), und
    /// <c>NoteSource</c> legte eine ZWEITE Zeile mit derselben Kennung an — gegen den eindeutigen
    /// Index (Kind, ExternalId). Der gemeinsame Helfer wird dafuer bewusst nicht angefasst; das
    /// betrifft alle Zusatzquellen und gehoert an einer Stelle entschieden.</para>
    /// </summary>
    private async Task EnsureSourcesLoadedAsync(TournamentDirectoryEntry? entry, CancellationToken ct)
    {
        if (entry is null) return;

        var collection = _db.Entry(entry).Collection(e => e.Sources);
        if (!collection.IsLoaded) await collection.LoadAsync(ct);
    }

    /// <summary>
    /// Ob die Turnierseite dieses Turniers schon gelesen wurde — erkennbar an der Adresse im
    /// Herkunftsvermerk.
    /// </summary>
    internal static bool HasDetail(TournamentDirectoryEntry entry, string externalId) =>
        entry.Sources.Any(s => s.Kind == DirectorySourceKind.FrenchChessFederation
                               && s.ExternalId == externalId
                               && s.Url is { Length: > 0 });

    private async Task<bool> LoadDetailAsync(
        TournamentDirectoryEntry entry, CrawlerFfeEvent row, CancellationToken ct)
    {
        var detail = await FetchDetailAsync(row.EventId, ct);
        if (detail is null) return false;

        // Der Termin der Turnierseite ist der genauere: die Liste nennt nur Tag und Monatskuerzel,
        // das Jahr stammt aus der Zwischenueberschrift.
        if (detail.Start is { } start) entry.StartDate = start;
        if (detail.End is { } end && entry.StartDate is { } from && end >= from) entry.EndDate = end;
        if (entry.StartDate is { } begin)
            entry.StartsOnWeekend = begin.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        // Die Anschrift ERSETZT den Listenort — nur sie traegt die Postleitzahl, und genau dafuer
        // wurde die Seite geholt. Der Ortsname der Liste haengt hinten dran, falls die Anschrift
        // ihn nicht selbst nennt (gemessen: „Espace Kerourgue 53 rue de Kerourgue" ohne Ort).
        var address = AddressOf(detail.Address, row.City);
        if (address is { Length: > 0 })
            entry.LocationText = ExternalDirectorySource.Truncate(address, 300);

        if (detail.TimeControl is { Length: > 0 })
        {
            // GESPEICHERT wird der Wortlaut der Quelle; EINGEORDNET wird eine uebersetzte Fassung
            // (siehe NormalizeCadence) — die franzoesische Schreibweise versteht der gemeinsame
            // Klassifizierer nicht.
            entry.TimeControlText = ExternalDirectorySource.Truncate(detail.TimeControl, 300);
            entry.Speed = TournamentSpeedClassifier.Classify(NormalizeCadence(detail.TimeControl));
        }
        if (detail.Rounds is > 0) entry.Rounds = detail.Rounds;
        entry.System = SystemOf(detail.PairingSystem, entry.System);

        ExternalDirectorySource.ApplyClassification(entry);
        return true;
    }

    /// <summary>
    /// Der Ortstext aus der LISTE: „SAINT BRISSON (58)". Das Departement ist der einzige
    /// Unterscheider, den die Liste mitbringt — Frankreich hat reichlich gleichnamige Orte, und
    /// ohne ihn liefert der Namensweg des Geocoders dort <see cref="GeoSource.Ambiguous"/> und
    /// damit gar keinen Pin.
    /// </summary>
    internal static string? LocationOf(CrawlerFfeEvent row)
    {
        var city = row.City?.Trim();
        var department = row.Department?.Trim();

        if (city is not { Length: > 0 }) return null;
        return department is { Length: > 0 } ? $"{city} ({department})" : city;
    }

    /// <summary>
    /// Der Ortstext von der TURNIERSEITE: die Anschrift, und dahinter der Ortsname, falls sie ihn
    /// nicht selbst nennt.
    ///
    /// <para>Das Departement kommt hier bewusst NICHT dazu: der Postleitzahl-Weg des Geocoders
    /// laeuft vor der Zerlegung, und eine zweistellige Zahl in Klammern waere dort eine weitere
    /// Ziffernfolge, die wie eine Hausnummer aussieht. Die Postleitzahl in der Anschrift traegt
    /// das Departement ohnehin in ihren ersten beiden Stellen.</para>
    /// </summary>
    internal static string? AddressOf(string? address, string? city)
    {
        var text = Collapse(address);
        if (text is not { Length: > 0 }) return null;

        var place = city?.Trim();
        if (place is not { Length: > 0 }) return text;

        return text.Contains(place, StringComparison.OrdinalIgnoreCase) ? text : $"{text}, {place}";
    }

    /// <summary>
    /// Das Departement als Regionsangabe. Eine nackte „58" saehe in der Oberflaeche neben lauter
    /// Bundeslaendern und Woiwodschaften wie ein Fehler aus; „Dept. 58" sagt, was es ist.
    ///
    /// <para>Die Ligue-Spalte der Quelle taugt dafuer NICHT: sie nennt, wer die Wertung fuehrt
    /// („FFE", „EST", „NAQ"), nicht wo gespielt wird — und in vier von fuenf Faellen steht dort
    /// ohnehin „FFE".</para>
    /// </summary>
    internal static string? RegionOf(string? department)
    {
        var text = department?.Trim();
        return text is { Length: > 0 } ? $"Dept. {text}" : null;
    }

    /// <summary>
    /// Die franzoesische Bedenkzeit-Schreibweise in die Form bringen, die
    /// <see cref="TournamentSpeedClassifier"/> versteht — „60' + [30'']" wird zu
    /// „60 min + 30 sec".
    ///
    /// <para><b>Warum das noetig ist, und warum es hier steht.</b> Die FFE schreibt das Inkrement
    /// mit ZWEI APOSTROPHEN, nicht mit einem Anfuehrungszeichen; der Klassifizierer kennt
    /// <c>sec</c>, <c>sek</c>, <c>"</c> und <c>″</c>, aber nicht <c>''</c>. Er las deshalb
    /// „60' + [30'']" als 60 Minuten ohne Inkrement und ordnete ein FIDE-Standardturnier
    /// (60 + 30 = 90 Minuten) als Schnellschach ein. Bei „1h30 + [30'']" war es schlimmer: die
    /// Stundenregel greift dort nicht (<c>h</c> steht direkt vor einer Ziffer), und das
    /// Minutenmuster fand die 30 aus der Klammer — aus 120 Minuten wurden 30. Beides ist eine
    /// Eigenheit DIESER Quelle, deshalb wird sie hier uebersetzt und nicht der gemeinsame
    /// Klassifizierer umgebaut.</para>
    ///
    /// <para>Die Regel der Quelle, an neun Turnierseiten abgelesen: vor dem Plus die Grundzeit
    /// („60'", „1h30", „15'"), in der Klammer das Inkrement in SEKUNDEN — unabhaengig davon,
    /// welches Zeichen der Veranstalter dahinter setzt („[30'']", „[30\"]", und einmal „[30']").
    /// Steht keine Grundzeit da, kommt <c>null</c> zurueck und die Klasse bleibt
    /// <see cref="TournamentSpeed.Unknown"/> — raten waere schlechter als schweigen.</para>
    /// </summary>
    internal static string? NormalizeCadence(string? cadence)
    {
        var text = Collapse(cadence);
        if (text is not { Length: > 0 }) return null;

        int? baseMinutes = null;

        // „1h30", „1h" — die Stundenform steht bei den laengeren Bedenkzeiten.
        var hours = HoursPattern.Match(text);
        if (hours.Success)
        {
            var h = int.Parse(hours.Groups[1].Value);
            var m = hours.Groups[2].Success && hours.Groups[2].Value.Length > 0
                ? int.Parse(hours.Groups[2].Value)
                : 0;
            baseMinutes = h * 60 + m;
        }
        else
        {
            // „60'", „15 min" — alles VOR der Klammer; die Klammer ist das Inkrement.
            var head = text.Split('[')[0];
            var minutes = MinutesPattern.Match(head);
            if (minutes.Success) baseMinutes = int.Parse(minutes.Groups[1].Value);
        }

        if (baseMinutes is not > 0) return null;

        var increment = IncrementPattern.Match(text);
        return increment.Success
            ? $"{baseMinutes} min + {increment.Groups[1].Value} sec"
            : $"{baseMinutes} min";
    }

    private static readonly System.Text.RegularExpressions.Regex HoursPattern =
        new(@"(\d{1,2})\s*h\s*(\d{0,2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex MinutesPattern =
        new(@"(\d{1,3})\s*(?:'|′|min)");

    /// <summary>
    /// Das Inkrement steht IMMER hinter dem Plus, meist in einer Klammer. Gesucht wird deshalb
    /// nach dem Plus und nicht nach der Klammer: „60' + 30''" ohne Klammer kommt genauso vor, und
    /// welches Zeichen der Veranstalter hinter die Zahl setzt, ist gleichgueltig — die Klammer ist
    /// bei dieser Quelle immer in SEKUNDEN.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex IncrementPattern =
        new(@"\+\s*\[?\s*(\d{1,3})");

    /// <summary>
    /// Das Paarungsverfahren im Wortlaut der Quelle. „Suisse", „S.A.D." (Systeme Accelere
    /// Degressif) und „Haley" sind alle drei SCHWEIZER System — die beiden letzten sind
    /// Beschleunigungsverfahren innerhalb davon und keine eigene Turnierform. Was nicht erkannt
    /// wird, laesst den bisherigen Wert stehen: raten waere schlechter als schweigen.
    /// </summary>
    internal static TournamentSystem SystemOf(string? text, TournamentSystem current)
    {
        var value = text?.Trim().ToLowerInvariant();
        if (value is not { Length: > 0 }) return current;

        if (value.Contains("toutes rondes", StringComparison.Ordinal)
            || value.Contains("berger", StringComparison.Ordinal))
            return TournamentSystem.RoundRobin;

        if (value.Contains("suisse", StringComparison.Ordinal)
            || value.Contains("haley", StringComparison.Ordinal)
            || value.Contains("s.a.d", StringComparison.Ordinal)
            || value.Contains("accelere", StringComparison.Ordinal)
            || value.Contains("accéléré", StringComparison.Ordinal))
            return TournamentSystem.Swiss;

        return current;
    }

    private static string? Collapse(string? text) =>
        text is null ? null : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerFfeEvent>> FetchListAsync(int months, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/ffe-calendar?from={today:yyyy-MM-dd}&months={months}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<FfeRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerFfeEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), r.City, r.Department,
                r.HomologatedBy, r.Url))
            .Where(e => e.Start is not null)
            .ToList();
    }

    /// <summary>
    /// Die Turnierseite EINES Turniers. Ein Fehlschlag ist kein Grund, den Durchgang abzubrechen —
    /// der Eintrag steht dann eben nur mit dem, was die Liste hergibt, und wird beim naechsten
    /// Durchgang erneut versucht (der Herkunftsvermerk bekommt seine Adresse ja nicht).
    /// </summary>
    private async Task<CrawlerFfeDetail?> FetchDetailAsync(string eventId, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
            using var response = await client.GetAsync(
                $"/api/ffe-calendar/detail?id={Uri.EscapeDataString(eventId)}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            var row = JsonSerializer.Deserialize<FfeDetailRow>(body, JsonOptions);
            return row is null
                ? null
                : new CrawlerFfeDetail(ParseDate(row.StartDate), ParseDate(row.EndDate),
                    row.City, row.Department, row.Address, row.TimeControl, row.Rounds,
                    row.PairingSystem);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FFE: Turnierseite {EventId} nicht lesbar", eventId);
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record FfeRow(
        string? EventId, string? Name, string? StartDate, string? City, string? Department,
        string? HomologatedBy, string? Url);

    private sealed record FfeDetailRow(
        string? StartDate, string? EndDate, string? City, string? Department, string? Address,
        string? TimeControl, int? Rounds, string? PairingSystem);

    public sealed record CrawlerFfeEvent(
        string EventId, string Name, DateOnly? Start, string? City, string? Department,
        string? HomologatedBy, string? Url);

    public sealed record CrawlerFfeDetail(
        DateOnly? Start, DateOnly? End, string? City, string? Department, string? Address,
        string? TimeControl, int? Rounds, string? PairingSystem);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
