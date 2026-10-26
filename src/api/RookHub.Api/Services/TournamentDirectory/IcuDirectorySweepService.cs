using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Text.Json;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender der Irish Chess Union (icu.ie) als Quelle.
///
/// <para><b>Warum diese Quelle.</b> Am 2026-09-09 gemessen: <b>81 kuenftige Turniere</b> bis
/// Oktober 2030 gegen <b>19</b> fuer IRL auf chess-results — rund 94 % fehlen dort. Irland faehrt
/// sein Turnierwesen ueber den eigenen Kalender.</para>
///
/// <para><b>Sie ist billig, weil die LISTE fast alles traegt.</b> Fuenf Abrufe bringen Name,
/// Termin, Bedenkzeit-Klasse, die Publikums-Schlagworte und einen Ortstext, der hier oft die
/// volle Anschrift samt Postleitzahl ist (15 von 81 mit Eircode). Die Koordinaten stehen im
/// Kartenblock DERSELBEN Seite — 30 der 81 Turniere sind damit ohne Zusatzabruf verortet und
/// tragen <see cref="GeoSource.SourceProvided"/>.</para>
///
/// <para><b>Die Detailseite ist absichtlich klein gehalten.</b> An 28 echten Seiten gemessen
/// bringt sie: die chess-results-Nummer 2-mal (7 %), die Teilnehmerzahl 6-mal (21 %) und
/// <b>keine einzige</b> Koordinate, die nicht schon in der Liste stand. Sie wird deshalb nur
/// EINMAL je Turnier geholt und gedeckelt — die Nummer ist der einzige exakte
/// Zuordnungsschluessel dieser Quelle und darum den Abruf wert, alles andere waere er nicht.</para>
///
/// <para><b>Was in der Liste steht und kein Turnier ist:</b> Unterricht, Lehrgaenge und die
/// Jahreshauptversammlung — 5 von 81. Zwei der Unterrichtsreihen laufen ueber 78 bzw. 84 Tage;
/// im Kalender haetten sie ein Vierteljahr zugedeckt. Der Crawler markiert sie
/// (<c>NonTournament</c>), hier werden sie uebersprungen und ein frueher angelegter Eintrag
/// zurueckgezogen.</para>
///
/// <para><b>Der Nordirland-Fall — die Falle dieser Quelle.</b> Die ICU ist der GESAMTirische
/// Verband: 11 der 81 Turniere finden in Nordirland statt (Bangor, Coleraine, Kilrea, Coalisland
/// — britische BT-Postleitzahlen). Die Foederation bleibt fuer sie <c>IRL</c>, denn genau dort
/// sucht sie jemand; fuer die VERORTUNG waere das aber falsch, weil <c>IRL</c> im Ortslexikon auf
/// <c>IE</c> abbildet und ein BT-Code dort nicht existiert. Deshalb bekommt der Geocoder fuer
/// diese Zeilen <c>ENG</c> (→ <c>GB</c>) mit. Heute faellt das nicht auf — alle 11 kommen mit
/// Koordinaten der Quelle —, aber es faellt eben auch nicht auf, wenn es kippt.</para>
/// </summary>
public class IcuDirectorySweepService : IDirectorySourceAdapter<IcuDirectorySweepService.IcuSweepRow>
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly int _detailBatchSize;
    private readonly TimeSpan _detailDelay;
    private readonly ILogger<IcuDirectorySweepService> _log;

    /// <summary>Wie viele Detailseiten dieser Lauf noch holen darf.</summary>
    private int _detailBudget;

    public IcuDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, IConfiguration configuration,
        ILogger<IcuDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _detailBatchSize = configuration.GetValue("TournamentDirectory:IcuDetailBatchSize", 40);

        // Die Wartezeit steht HIER und nicht im Crawler: dessen Detail-Route ist zustandslos und
        // holt genau eine Seite — wer sie hintereinander aufruft, muss selbst warten. (Der
        // Crawler wartet nur zwischen den Seiten SEINER eigenen Blaetterei.) In Tests 0.
        _detailDelay = TimeSpan.FromSeconds(
            configuration.GetValue("TournamentDirectory:IcuDetailDelaySeconds", 5));
        _log = log;
    }

    public async Task<ExternalSweepResult> RunAsync(
        int? detailLimit = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchListAsync(today, ct);
        _detailBudget = Math.Max(0, detailLimit ?? _detailBatchSize);
        return await ExternalSweepRunner.RunAsync(_db, _log, this,
            events.Select(e => new IcuSweepRow(e)).ToList(), ct);
    }

    /// <summary>
    /// Eine Zeile im Lauf: die Listenzeile und — falls in DIESEM Lauf geholt — ihre Detailseite.
    /// Die Detailseite haengt an der Zeile, weil sie VOR der Zuordnung geholt wird
    /// (<see cref="IDirectorySourceAdapter{TRow}.BeforeMatchAsync"/>) und danach Zuordnung,
    /// Herkunftsvermerk und Teilnehmerzahl bestimmt.
    /// </summary>
    public sealed class IcuSweepRow(CrawlerIcuEvent row)
    {
        public CrawlerIcuEvent Row { get; } = row;
        public ParsedIcuDetail? Detail { get; set; }

        /// <summary>
        /// Die chess-results-Nummer des Eintrags, an dem der Herkunftsvermerk dieser Zeile schon
        /// haengt — die Erinnerung an einen frueheren exakten Treffer (siehe
        /// <see cref="IDirectorySourceAdapter{TRow}.BeforeMatchAsync"/>).
        /// </summary>
        public string? KnownChessResultsId { get; set; }
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<IcuSweepRow>.Kind => DirectorySourceKind.IrishChessUnion;
    string IDirectorySourceAdapter<IcuSweepRow>.LogName => "ICU";
    string? IDirectorySourceAdapter<IcuSweepRow>.SourceKey(IcuSweepRow row) => row.Row.EventId.Length == 0 ? null : row.Row.EventId;
    DateOnly? IDirectorySourceAdapter<IcuSweepRow>.Start(IcuSweepRow row) => row.Row.Start;
    string IDirectorySourceAdapter<IcuSweepRow>.Name(IcuSweepRow row) => row.Row.Name;
    string IDirectorySourceAdapter<IcuSweepRow>.PublicId(IcuSweepRow row) => $"ie{row.Row.EventId}";

    /// <summary>Unterricht, Lehrgang, Jahreshauptversammlung: kein Turnier.</summary>
    bool IDirectorySourceAdapter<IcuSweepRow>.Withdrawn(IcuSweepRow row) => row.Row.NonTournament;

    string? IDirectorySourceAdapter<IcuSweepRow>.Federation(IcuSweepRow row) => FederationOf(row.Row);
    string? IDirectorySourceAdapter<IcuSweepRow>.MatchPlace(IcuSweepRow row) => row.Row.Place;
    string? IDirectorySourceAdapter<IcuSweepRow>.Url(IcuSweepRow row) => DetailUrl(row.Row, row.Detail);

    /// <summary>
    /// Die Nummer der Detailseite, wenn sie in DIESEM Lauf geholt wurde — sonst die Nummer des
    /// Eintrags, an dem der Vermerk schon haengt. Die Detailseite wird nur EINMAL geholt; ohne die
    /// zweite Haelfte fehlte die Nummer ab der zweiten Nacht, der Namensvergleich schlug fehl, und
    /// das Turnier stand als eigener Eintrag <c>ie…</c> neben seinem chess-results-Eintrag.
    /// </summary>
    string? IDirectorySourceAdapter<IcuSweepRow>.ChessResultsId(IcuSweepRow row) =>
        row.Detail?.ChessResultsId is { Length: > 0 } fromDetail ? fromDetail : row.KnownChessResultsId;

    /// <summary>
    /// Die Detailseite kommt VOR der Zuordnung — anders als bei chessarbiter, wo sie nur den
    /// eigenen Eintrag anreichert. Hier traegt sie den einzigen EXAKTEN Zuordnungsschluessel dieser
    /// Quelle, und ein Schluessel, der erst nach der Entscheidung eintrifft, ist wertlos.
    /// „Aktualisiert" heisst hier: die Detailseite war lesbar.
    ///
    /// <para>Vorher wird der bestehende Herkunftsvermerk gelesen: er sagt, ob die Detailseite schon
    /// geholt wurde, und an welchem Eintrag er haengt. Traegt DER eine chess-results-Nummer, war
    /// das ein frueherer Treffer — die Nummer gilt dann weiter als exakter Schluessel, auch wenn
    /// die Detailseite in dieser Nacht nicht geholt wird. Ein eigener Eintrag <c>ie…</c> hat
    /// keine Nummer und liefert damit nichts.</para>
    /// </summary>
    async Task<bool> IDirectorySourceAdapter<IcuSweepRow>.BeforeMatchAsync(IcuSweepRow row, CancellationToken ct)
    {
        var note = await FindNoteAsync(row.Row.EventId, ct);
        row.KnownChessResultsId = note?.ChessResultsId;
        if (_detailBudget <= 0 || note is { HasDetail: true }) return false;

        _detailBudget--;
        row.Detail = await FetchDetailAsync(row.Row.EventId, ct);
        if (_detailDelay > TimeSpan.Zero) await Task.Delay(_detailDelay, ct);
        return row.Detail is not null;
    }

    async Task<bool> IDirectorySourceAdapter<IcuSweepRow>.ApplyAsync(
        TournamentDirectoryEntry own, IcuSweepRow sweepRow, DateOnly start, DateTime now, CancellationToken ct)
    {
        var (row, detail) = (sweepRow.Row, sweepRow.Detail);
        var location = row.Place?.Trim();
        var locationChanged = own.LocationText != location;

        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.Federation = FederationOf(row);
        own.StartDate = start;
        own.EndDate = row.End is { } end && end >= start ? end : start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        own.LocationText = ExternalDirectorySource.Truncate(location, 300);
        own.Speed = SpeedOf(row.Categories);
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        ApplyAudienceMarks(own, row);

        if (detail?.PlayerCount is > 0) own.PlayerCount = detail.PlayerCount;

        await ExternalDirectorySource.NoteSourceAsync(_db, own, DirectorySourceKind.IrishChessUnion,
            row.EventId, DetailUrl(row, detail), now, ct);

        await ApplyCoordinatesAsync(own, row, locationChanged, ct);
        // „Aktualisiert" zaehlt bei dieser Quelle nur die gelesene Detailseite (BeforeMatchAsync).
        return false;
    }

    void IDirectorySourceAdapter<IcuSweepRow>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "ICU-Kalender: {Read} gelesen, {Added} neu, {Updated} mit Detailseite, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);

    /// <summary>Ein Auslandsturnier bekommt keine Foederation, alles andere ist <c>IRL</c>.</summary>
    private static string? FederationOf(CrawlerIcuEvent row) => row.Foreign ? null : "IRL";

    /// <summary>
    /// Der Herkunftsvermerk dieses Turniers (<c>null</c> = noch keiner): ob die Detailseite schon
    /// gelesen wurde — erkennbar an der Adresse im
    /// Herkunftsvermerk, die erst dabei gesetzt wird.
    ///
    /// <para>Gefragt wird nach dem VERMERK und nicht nach dem eigenen Eintrag: wurde das Turnier
    /// einem chess-results-Eintrag zugeordnet, gibt es gar keinen eigenen mehr, und ueber ihn
    /// gesucht wuerde dieselbe Seite jede Nacht erneut geholt.</para>
    ///
    /// <para>Derselbe Vermerk nennt auch die chess-results-Nummer seines Eintrags (siehe
    /// <see cref="IDirectorySourceAdapter{TRow}.BeforeMatchAsync"/>). Der Vermerk ist je
    /// <c>(Kind, ExternalId)</c> eindeutig.</para>
    /// </summary>
    private Task<IcuNote?> FindNoteAsync(string eventId, CancellationToken ct) =>
        _db.TournamentDirectorySources
            .Where(s => s.Kind == DirectorySourceKind.IrishChessUnion && s.ExternalId == eventId)
            .Select(s => new IcuNote(s.Url != null, s.Entry.ChessResultsId))
            .FirstOrDefaultAsync(ct);

    private sealed record IcuNote(bool HasDetail, string? ChessResultsId);

    /// <summary>
    /// Die Adresse kommt in den Vermerk erst, wenn die Detailseite wirklich gelesen wurde — sie
    /// ist zugleich die Marke „schon geholt". Ein Vermerk mit Adresse ohne gelesene Seite
    /// verhinderte den Abruf fuer immer.
    /// </summary>
    private static string? DetailUrl(CrawlerIcuEvent row, ParsedIcuDetail? detail) =>
        detail is null ? null : row.Url;

    /// <summary>
    /// Die Koordinaten setzen — mit der Angabe der Quelle, wenn es eine gibt, sonst ueber das
    /// Ortslexikon.
    ///
    /// <para>Eine mitgelieferte Koordinate schlaegt jeden Lexikon-Treffer: sie meint die
    /// Spielstaette, ein Lexikon-Treffer bestenfalls die Ortsmitte. Sie wird deshalb auch dann
    /// gesetzt, wenn schon ein Pin steht — es sei denn, jemand hat ihn von Hand gesetzt.</para>
    /// </summary>
    private async Task<bool> ApplyCoordinatesAsync(TournamentDirectoryEntry entry,
        CrawlerIcuEvent row, bool locationChanged, CancellationToken ct)
    {
        if (entry.GeoSource == GeoSource.Manual) return false;

        if (row.Lat is { } lat && row.Lon is { } lon)
        {
            entry.Lat = lat;
            entry.Lon = lon;
            entry.GeoSource = GeoSource.SourceProvided;
            entry.GeoPlaceName = ExternalDirectorySource.Truncate(row.Place, 200);
            return true;
        }

        if (entry.LocationText is not { Length: > 0 }) return false;
        if (!locationChanged && entry.Lat is not null) return false;

        var hit = await _geocoding.ResolveAsync(entry.LocationText, null, GeoFederationOf(row), ct);
        if (hit is null || hit.Source == GeoSource.Ambiguous) return false;

        entry.Lat = hit.Lat;
        entry.Lon = hit.Lon;
        entry.GeoSource = hit.Source;
        entry.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
        return true;
    }

    /// <summary>
    /// Das Land, in dem der Geocoder suchen soll — nicht dasselbe wie die Foederation des
    /// Eintrags.
    ///
    /// <para>Die ICU ist der GESAMTirische Verband, und 11 der 81 Turniere liegen in Nordirland,
    /// also im Vereinigten Koenigreich. Als <c>IRL</c> gesucht landet eine BT-Postleitzahl im
    /// Nichts, weil das Ortslexikon dafuer auf <c>IE</c> eingeschraenkt wird. Erkannt wird der
    /// Fall am Ortstext selbst — die Anschrift nennt entweder „Northern Ireland" oder eine
    /// BT-Postleitzahl.</para>
    ///
    /// <para>Ein Auslandsturnier („Foreign") bekommt gar kein Land: dort ist jede Einschraenkung
    /// eine Behauptung, und ein unbeschraenkter Namenstreffer ist die ehrlichere Auskunft.</para>
    /// </summary>
    internal static string? GeoFederationOf(CrawlerIcuEvent row) =>
        row.Foreign ? null : LooksNorthernIrish(row.Place) ? "ENG" : "IRL";

    internal static bool LooksNorthernIrish(string? place) =>
        place is { Length: > 0 }
        && (place.Contains("Northern Ireland", StringComparison.OrdinalIgnoreCase)
            || NorthernIrishPostcode.IsMatch(place));

    /// <summary>Britische Postleitzahlen des Bezirks Belfast — „BT19 6JR", „BT71 5DX".</summary>
    private static readonly System.Text.RegularExpressions.Regex NorthernIrishPostcode =
        new(@"\bBT\d{1,2}\s?\d[A-Z]{2}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Die Bedenkzeit-Klasse steht als Schlagwort in der Zeile — diese Quelle sagt sie also
    /// selbst, statt sie im Namen zu verstecken.
    ///
    /// <para>MEHRERE Klassen an einem Eintrag heissen „gemischt" (ein Festival mit Turnier-,
    /// Schnell- und Blitzbewerb, 3 von 81) und ergeben KEINE Klasse: eine davon auszuwaehlen
    /// waere geraten, und der Filter „nur Blitz" bekaeme ein Turnierschach-Wochenende.</para>
    /// </summary>
    internal static TournamentSpeed SpeedOf(IReadOnlyCollection<string> categories)
    {
        var speeds = categories
            .Select(c => c.Trim().ToLowerInvariant() switch
            {
                "classical" => TournamentSpeed.Standard,
                "rapid" => TournamentSpeed.Rapid,
                "blitz" => TournamentSpeed.Blitz,
                _ => TournamentSpeed.Unknown,
            })
            .Where(s => s != TournamentSpeed.Unknown)
            .Distinct()
            .ToList();

        return speeds.Count == 1 ? speeds[0] : TournamentSpeed.Unknown;
    }

    /// <summary>
    /// Die Publikums-Schlagworte der Quelle — aber nur, wo der Name nichts Genaueres sagt.
    /// „The Irish Womens Championships" traegt seine Klasse selbst; „14th ChessMates" nicht, und
    /// dort ist „Junior International" die einzige Auskunft, dass es ein Jugendturnier ist.
    /// </summary>
    private static void ApplyAudienceMarks(TournamentDirectoryEntry entry, CrawlerIcuEvent row)
    {
        if (row.JuniorInternational && entry.AgeGroups == TournamentAgeGroups.None)
            entry.AgeGroups = TournamentAgeGroups.YouthUnspecified;

        if (row.WomenOnly && entry.Gender == TournamentGender.Open)
            entry.Gender = TournamentGender.Female;
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerIcuEvent>> FetchListAsync(DateOnly from, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<IcuRow>>(
            $"/api/icu-calendar?from={from:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerIcuEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Place,
                r.Lat, r.Lon, r.Url, r.Categories ?? [], r.NonTournament))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    /// <summary>
    /// Die Detailseite EINES Turniers. Ein Fehlschlag ist kein Grund, den Durchgang abzubrechen —
    /// die Zeile steht dann eben ohne die beiden Zusatzangaben da, und der naechste Durchgang
    /// versucht es wieder (der Vermerk bekommt ohne gelesene Seite keine Adresse).
    /// </summary>
    private async Task<ParsedIcuDetail?> FetchDetailAsync(string eventId, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
            using var response = await client.GetAsync(
                $"/api/icu-calendar/detail?id={Uri.EscapeDataString(eventId)}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            var row = JsonSerializer.Deserialize<IcuDetailRow>(body, DirectoryCrawlerClient.JsonOptions);
            return row is null
                ? null
                : new ParsedIcuDetail(row.ChessResultsId, row.PlayerCount, row.Website);
        }
        catch (Exception ex) when (ExternalDirectorySource.IsSourceFailure(ex, ct))
        {
            _log.LogWarning(ex, "ICU: Detailseite {EventId} nicht lesbar", eventId);
            return null;
        }
    }

    private sealed record IcuRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Url,
        string? Place, double? Lat, double? Lon, List<string>? Categories, bool NonTournament);

    private sealed record IcuDetailRow(string? ChessResultsId, int? PlayerCount, string? Website);

    public sealed record ParsedIcuDetail(string? ChessResultsId, int? PlayerCount, string? Website);

    public sealed record CrawlerIcuEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Place,
        double? Lat, double? Lon, string? Url, List<string> Categories, bool NonTournament)
    {
        /// <summary>
        /// Das Turnier findet im AUSLAND statt — die Quelle sagt das selbst („46th Chess Olympiad
        /// 2026" in Samarkand, „Sel Hotel Myvatn Open" in Island). 2 von 81.
        ///
        /// <para>Bewusst NICHT mitgezaehlt wird „Junior International": das sagt etwas ueber das
        /// FELD, nicht ueber den Spielort — die Glorney-Gilbert-Laenderkaempfe wandern zwischen
        /// den Inselverbaenden und finden mal in Irland statt. Die drei so markierten Turniere
        /// lagen 2026/27 zwar auch im Ausland und bekommen damit <c>IRL</c>; das Ortslexikon
        /// findet fuer sie dann nichts, was besser ist als ein Pin am falschen Ort.</para>
        /// </summary>
        public bool Foreign => Has("Foreign");

        /// <summary>Jugend-Laenderkampf oder internationales Jugendturnier laut Quelle.</summary>
        public bool JuniorInternational => Has("Junior International");

        /// <summary>Reines Frauenturnier laut Quelle.</summary>
        public bool WomenOnly => Has("Women only");

        private bool Has(string category) =>
            Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
    }
}
