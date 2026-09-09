using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Der Aktivitaeten-Feed des norwegischen Schachverbands (sjakk.no) als Quelle.
///
/// <para><b>Kein Zugewinn ist so eindeutig wie dieser.</b> chess-results fuehrt fuer NOR
/// <b>null</b> kuenftige Turniere — jeder Eintrag hier ist einer, den das Verzeichnis sonst
/// ueberhaupt nicht kennt. Der Feed bringt in EINEM Abruf 1000 Eintraege, davon 81 kuenftige
/// (gemessen 2026-09-09).</para>
///
/// <para><b>Und keine Quelle sagt so wenig ueber den ORT.</b> Der Feed hat kein Ortsfeld; die
/// Detailseite (ein Abruf je Turnier) nennt bei <b>17 von 80</b> ein „Spillsted" und bei
/// <b>52 von 80</b> den ausrichtenden VEREIN. Daraus die zwei Wege in
/// <see cref="LocationOf"/>, und die Zahl, die man dazu kennen muss:</para>
///
/// <list type="bullet">
/// <item><b>34 %</b> der 81 kuenftigen Eintraege bekommen mit dem heutigen Lexikon einen Pin
/// (42 % der 64 Eintraege, die ueberhaupt ein einzelnes norwegisches Turnier sind — der Rest sind
/// Ligawochenenden mit „Diverse" als Ort, Auslandsereignisse und Mitgliederversammlungen).</item>
/// <item><b>Der Engpass ist das LEXIKON, nicht die Quelle:</b> fuer NO stehen ganze <b>42</b>
/// Orte darin (nur <c>cities15000</c>, keine Postleitzahlen). Fagernes, Kongsvinger, Hammerfest,
/// Vestby, Geilo, Rakkestad — alles echte Spielorte, alle nicht im Lexikon. Ein
/// <c>POST /api/admin/tournament-directory/gazetteer/postal/NO</c> bringt <b>1831</b> weitere
/// Ortsnamen mit Koordinaten (GeoNames <c>NO.zip</c>, gegengeprueft) und hebt die Quote auf
/// <b>46 %</b> aller bzw. <b>58 %</b> der echten Turniere. Diese eine Einmal-Aktion ist mehr wert
/// als jede Verbesserung am Parser hier.</item>
/// </list>
///
/// <para><b>Der Vereinsname ist ein HINWEIS, kein Spielort</b> — deshalb bekommt ein so
/// entstandener Pin <see cref="GeoSource.TeamHint"/>. Ein norwegischer Verein traegt fast immer
/// seinen Ort im Namen („Kongsvinger Sjakklubb", „Bergens Schakklub"), und er spielt in aller
/// Regel dort; aber es ist eine Ableitung, keine Angabe, und die Admin-Arbeitsliste soll das
/// unterscheiden koennen.</para>
///
/// <para><b>Der TURNIERNAME wird bewusst NICHT verortet</b>, obwohl er weitere elf Pins braechte.
/// Ein Ortsfeld und ein Vereinsname sind Aussagen ueber einen Ort, ein Turniername ist keine:
/// dort ueber Wortfolgen zu suchen macht jedes zufaellig passende Wort zu einem Pin („Time" ist
/// eine norwegische Gemeinde mit 19 781 Einwohnern), und <c>LocationText</c> ist die Spalte
/// „Ort" in der Anzeige — „Bronstein Cup NGP 2026" gehoert dort nicht hinein.</para>
///
/// <para><b>„Diverse" ist kein Ort.</b> So bezeichnen die acht Ligawochenenden
/// („Seriesjakkens/Eliteseriens N. helg") ihre ueber das Land verteilten Spielstaetten. Ein Pin
/// darauf waere frei erfunden.</para>
/// </summary>
public class SjakkDirectorySweepService
{
    private const int SaveEvery = 25;

    /// <summary>
    /// Pause zwischen zwei Detailabrufen. Die robots.txt der Quelle nennt keine — der Crawler
    /// wartet aus eigener Zurueckhaltung, hier kommt nichts dazu.
    /// </summary>
    private static readonly TimeSpan DetailDelay = TimeSpan.FromMilliseconds(200);

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<SjakkDirectorySweepService> _log;
    private readonly int _detailBatchSize;

    public SjakkDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, IConfiguration configuration,
        ILogger<SjakkDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
        _detailBatchSize = configuration.GetValue("TournamentDirectory:SjakkDetailBatchSize", 120);
    }

    /// <summary>Die Foederation ist fest: der Feed ist der Kalender EINES Verbands.</summary>
    internal const string Federation = "NOR";

    public async Task<ExternalSweepResult> RunAsync(
        int? detailLimit = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, ct);
        if (events.Count == 0) return new ExternalSweepResult(0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        var budget = Math.Max(0, detailLimit ?? _detailBatchSize);
        int added = 0, updated = 0, matched = 0, retired = 0, processed = 0;

        try
        {
            foreach (var row in events)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Start is not { } start || row.Name.Length == 0 || row.EventId.Length == 0)
                    continue;

                processed++;
                var publicId = PublicIdOf(row.EventId);
                var own = await ExternalDirectorySource.FindOwnAsync(_db, publicId, ct);
                var match = await ExternalDirectorySource.FindMatchAsync(
                    _db, Federation, start, row.Name, ct);

                // Hat die Turniersuche dieselbe Veranstaltung inzwischen? Dann gehoert ihr der
                // Eintrag — hier wird nur der Herkunftsvermerk gesetzt. Heute der seltene Fall
                // (chess-results fuehrt fuer NOR nichts), aber genau dafuer ist er da.
                if (match is not null)
                {
                    await ExternalDirectorySource.NoteSourceAsync(_db, match,
                        DirectorySourceKind.NorwegianChessFederation, row.EventId, row.Url, now, ct);
                    matched++;

                    if (ExternalDirectorySource.RetireIfSuperseded(own, match, now))
                    {
                        retired++;
                        _log.LogInformation("sjakk.no: {PublicId} geht in {Target} auf ({Name})",
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
                        ChessResultsId = null,
                        FirstSeenAt = now,
                    };
                    _db.TournamentDirectoryEntries.Add(own);
                    added++;
                }

                own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
                own.Federation = Federation;
                own.StartDate = start;
                own.EndDate = row.End ?? start;
                own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                own.LastSeenAt = now;
                own.MissedSweeps = 0;
                own.RemovedAt = null;
                ExternalDirectorySource.ApplyClassification(own);

                // Der Detailabruf lohnt nur einmal je Turnier. „Schon geholt" steht in der
                // Adresse des Herkunftsvermerks — vermerkt wird sie erst, wenn die Seite da war.
                var hasDetail = HasDetail(own, row.EventId);
                if (!hasDetail && budget > 0)
                {
                    budget--;
                    if (await LoadDetailAsync(own, row.EventId, ct))
                    {
                        updated++;
                        hasDetail = true;
                    }
                    await Task.Delay(DetailDelay, ct);
                }

                await ExternalDirectorySource.NoteSourceAsync(_db, own,
                    DirectorySourceKind.NorwegianChessFederation, row.EventId,
                    hasDetail ? row.Url : null, now, ct);

                if (processed % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "sjakk.no: Zwischenstand konnte nicht gespeichert werden"); }
        }

        _log.LogInformation(
            "sjakk.no-Aktivitaeten: {Read} gelesen, {Added} neu, {Updated} mit Detailseite, {Matched} zugeordnet, {Retired} zurueckgezogen",
            events.Count, added, updated, matched, retired);
        return new ExternalSweepResult(events.Count, added, updated, matched, retired);
    }

    /// <summary>
    /// Ob die Detailseite dieses Turniers schon gelesen wurde — erkennbar an der Adresse im
    /// Herkunftsvermerk. Ein Vermerk OHNE Adresse heisst „aus dem Feed, Detailseite fehlt noch".
    /// </summary>
    internal static bool HasDetail(TournamentDirectoryEntry entry, string externalId) =>
        entry.Sources.Any(s => s.Kind == DirectorySourceKind.NorwegianChessFederation
                               && s.ExternalId == externalId && s.Url is { Length: > 0 });

    /// <summary>
    /// Die Detailseite holen und ihre Angaben eintragen. Gibt zurueck, ob sie lesbar war — ein
    /// Ausfall kostet Ort, Bedenkzeit und Rundenzahl, aber nicht den Termin (den hat der Feed).
    /// </summary>
    private async Task<bool> LoadDetailAsync(
        TournamentDirectoryEntry entry, string slug, CancellationToken ct)
    {
        SjakkDetail? detail;
        try { detail = await FetchDetailAsync(slug, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "sjakk.no: Detailseite {Slug} nicht lesbar", slug);
            return false;
        }
        if (detail is null) return false;

        var text = entry.TimeControlText;
        var rounds = entry.Rounds;
        ExternalDirectorySource.FillIfEmpty(detail.TimeControl, ref text, 300);
        ExternalDirectorySource.FillIfEmpty(detail.Rounds, ref rounds);
        entry.TimeControlText = text;
        entry.Rounds = rounds;

        if (entry.Speed == TournamentSpeed.Unknown)
            entry.Speed = TournamentSpeedClassifier.Classify(entry.TimeControlText);

        if (entry.System == TournamentSystem.Unknown)
            entry.System = detail.System switch
            {
                "swiss" => TournamentSystem.Swiss,
                "roundRobin" => TournamentSystem.RoundRobin,
                _ => TournamentSystem.Unknown,
            };

        var (location, fromClub) = LocationOf(detail.Venue, detail.Organizer);
        if (location is null) return true;

        var locationChanged = entry.LocationText != location;
        entry.LocationText = ExternalDirectorySource.Truncate(location, 300);

        if (locationChanged || entry.Lat is null)
        {
            var hit = await _geocoding.ResolveAsync(location, null, Federation, ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                entry.Lat = hit.Lat;
                entry.Lon = hit.Lon;
                // Aus einem VEREINSnamen abgeleitet ist kein Spielort-Treffer, auch wenn der
                // Name exakt passt. TeamHint sagt genau das — und die Arbeitsliste sieht es.
                entry.GeoSource = fromClub ? GeoSource.TeamHint : hit.Source;
                entry.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
        return true;
    }

    /// <summary>
    /// Woher der Ortstext kommt: aus „Spillsted", sonst aus „Arrangør". Der zweite Wert sagt, ob
    /// es der VEREIN war — dann ist der Pin eine Ableitung (<see cref="GeoSource.TeamHint"/>).
    ///
    /// <para>An 80 kuenftigen Terminen gemessen: 17 nennen ein Spillsted, davon acht „Diverse"
    /// (die Ligawochenenden) und drei ein Auslandsereignis; 52 nennen einen Verein. Ohne den
    /// zweiten Weg haetten <b>vier</b> Eintraege einen Pin.</para>
    /// </summary>
    internal static (string? Location, bool FromClub) LocationOf(string? venue, string? organizer)
    {
        if (HasVenue(venue)) return (venue!.Trim(), false);
        return organizer is { Length: > 0 } ? (organizer.Trim(), true) : (null, false);
    }

    /// <summary>
    /// Ob der Ortstext ein SPIELORT ist. „Diverse" ist der Sammelbegriff der Ligawochenenden fuer
    /// ihre ueber das Land verteilten Spielstaetten — ein Pin darauf waere frei erfunden. Dazu
    /// dieselbe Regel wie ueberall fuer Onlineschach.
    /// </summary>
    internal static bool HasVenue(string? venue)
    {
        if (venue is not { Length: > 0 }) return false;

        var value = venue.Trim();
        return !value.Equals("diverse", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("ulike steder", StringComparison.OrdinalIgnoreCase)
               && !value.StartsWith("online", StringComparison.OrdinalIgnoreCase)
               && !value.StartsWith("nettbasert", StringComparison.OrdinalIgnoreCase)
               && !value.Contains("server", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Diese Quelle hat keine Nummer — ihre Kennung ist der Adressbestandteil der Detailseite
    /// („horten-bgp-høst-2027"), und der traegt norwegische Buchstaben. Die Spalte fasst 24
    /// Zeichen, also ein Kurzwert; der lesbare Adressbestandteil steht vollstaendig im
    /// Herkunftsvermerk.
    /// </summary>
    internal static string PublicIdOf(string slug)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(slug.Trim().ToLowerInvariant()));
        return "no" + Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerSjakkEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync($"/api/sjakk-calendar?from={from:yyyy-MM-dd}", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        var rows = JsonSerializer.Deserialize<List<SjakkRow>>(body, JsonOptions) ?? [];
        return rows
            .Where(r => r.EventId is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerSjakkEvent(
                r.EventId!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Url))
            .Where(e => e.Start is not null)
            .ToList();
    }

    private async Task<SjakkDetail?> FetchDetailAsync(string slug, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/sjakk-calendar/detail?slug={Uri.EscapeDataString(slug)}", ct);

        // 404 = die Seite gibt es nicht mehr oder sie ist nicht lesbar. Kein Fehler: der Termin
        // steht schon, nur seine Zusatzangaben fehlen.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        return JsonSerializer.Deserialize<SjakkDetail>(body, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SjakkRow(
        string? EventId, string? Name, string? StartDate, string? EndDate, string? Url);

    private sealed record SjakkDetail(
        string? Venue, string? Organizer, string? TimeControl, int? Rounds, string? System,
        string? Website);

    public sealed record CrawlerSjakkEvent(
        string EventId, string Name, DateOnly? Start, DateOnly? End, string? Url);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
