using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Der Kalender von Chess Scotland (chessscotland.com) als Quelle.
///
/// <para><b>Warum diese Quelle.</b> Am 2026-09-09 gemessen: 43-44 kuenftige Turniere in EINER
/// Seite bis 2028, gegen 8 auf chess-results fuer SCO im selben Zeitraum — rund 36 zusaetzliche,
/// ueberwiegend kleinere Vereins- und Jugendturniere (Allegro-/Blitz-Abende, Schulligen).</para>
///
/// <para><b>Die Bedenkzeit-KLASSE steht strukturiert dabei — der Sonderwert dieser Quelle.</b> Bei
/// jeder anderen Quelle des Projekts muss sie aus Freitext erschlossen werden
/// (<see cref="TournamentSpeedClassifier"/>); hier liefert die Zeile sie als Schlagwort:
/// „Standard", „Allegro" (FIDE-Sprache fuer Schnellschach/Rapid), „Blitz", dazu „Fide" als reiner
/// Wertungs-Vermerk ohne eigene Klasse (siehe <see cref="SpeedOf"/>). Ein Kongress kann mehrere
/// gleichzeitig anbieten — die ERNSTHAFTESTE Klasse gilt als die des Turniers.</para>
///
/// <para><b>„Junior" ist nur OHNE „Adult" eine Jugend-Angabe.</b> 25 von 43 gemessenen Terminen
/// tragen BEIDE Schlagworte zugleich (ein Kongress mit Erwachsenen- UND Jugendabteilung), nur 16
/// tragen ausschliesslich „Junior". Ein Pin auf „Nachwuchs" bei einem gemischten Kongress waere
/// falsch — siehe <see cref="ApplyYouthMark"/>.</para>
///
/// <para><b>Kein Spielort in der Liste — und nur mit Vorbehalt auf der Detailseite.</b> Die
/// Ausschreibung liegt dort als freier Rich-Text-Block ohne strukturierte Adressfelder. Gemessen
/// an allen 43 damals kuenftigen Detailseiten: 8 (19 %) enthalten eine erkennbare britische
/// Postleitzahl irgendwo im Text (der Crawler liest NUR diese eine Zeile, siehe
/// <c>ChessScotlandCalendarService.VenueOf</c>), 35 nicht. Der Detailabruf lohnt trotzdem: ohne
/// Postleitzahl-Bestand fuer Grossbritannien im Ortslexikon (<c>FideCountryCodes.ToIso2("SCO")</c>
/// bildet auf <c>GB</c> ab, siehe dort) traegt die gefundene Zeile ueberwiegend einen ECHTEN
/// Ortsnamen VOR der Postleitzahl („Dalblair Road, Ayr. KA7 1UG") — die normale
/// <see cref="GeocodingService"/>-Aufloesung ueber den Staedtenamen greift also unveraendert.</para>
///
/// <para><b>Der Detailabruf ist EINMALIG je Turnier</b> — wie bei sjakk.no wird eine gelesene Seite
/// nicht erneut geholt, AUCH WENN sie keinen Spielort ergab (<see cref="HasDetail"/> haengt nur am
/// Herkunftsvermerk-Link, nicht am Ergebnis). Ein bekannter, hingenommener Nachteil: schreibt ein
/// Veranstalter die Anschrift erst SPAETER in eine heute noch leere Ausschreibung („More details
/// to follow" — der haeufigste Fall bei weit vorausliegenden Terminen bis 2028), wird das nicht
/// nachgeholt. Dasselbe Verhalten hat die bestehende Norwegen-Quelle bereits; ein Aendern dieser
/// Regel ist bewusst NICHT Teil dieser Anbindung.</para>
///
/// <para><b>Rechtslage (2026-09-09 geprueft).</b> <c>/robots.txt</c> antwortet 404 (RFC 9309: keine
/// Einschraenkung), keine Nutzungsbedingungen gefunden.</para>
/// </summary>
public class ChessScotlandDirectorySweepService : IDirectorySourceAdapter<ChessScotlandDirectorySweepService.CrawlerChessScotlandEvent>
{

    /// <summary>Wartezeit vor einem Detailabruf. Die Quelle nennt keine — Selbstbeschraenkung.</summary>
    private static readonly TimeSpan DetailDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Die Foederation ist fest: die Quelle ist der Kalender EINES Verbands.</summary>
    internal const string Federation = "SCO";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingService _geocoding;
    private readonly ILogger<ChessScotlandDirectorySweepService> _log;
    private readonly int _detailBatchSize;

    /// <summary>Wie viele Detailseiten der LAUFENDE Durchgang noch holen darf (je <see cref="RunAsync"/> neu gesetzt).</summary>
    private int _detailBudget;

    public ChessScotlandDirectorySweepService(AppDbContext db, IHttpClientFactory httpClientFactory,
        GeocodingService geocoding, IConfiguration configuration,
        ILogger<ChessScotlandDirectorySweepService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _geocoding = geocoding;
        _log = log;
        // 50 deckt den ganzen heutigen Bestand (43-44) in einem Durchgang ab; wie bei sjakk.no
        // ist der Deckel trotzdem konfigurierbar, falls die Quelle waechst.
        _detailBatchSize = configuration.GetValue("TournamentDirectory:ChessScotlandDetailBatchSize", 50);
    }

    public async Task<ExternalSweepResult> RunAsync(int? detailLimit = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await FetchAsync(today, ct);
        _detailBudget = Math.Max(0, detailLimit ?? _detailBatchSize);
        return await ExternalSweepRunner.RunAsync(_db, _log, this, events, ct);
    }

    // ----- Abbildung fuer den gemeinsamen Runner ------------------------------

    DirectorySourceKind IDirectorySourceAdapter<CrawlerChessScotlandEvent>.Kind => DirectorySourceKind.ScottishChessFederation;
    string IDirectorySourceAdapter<CrawlerChessScotlandEvent>.LogName => "Chess Scotland";
    string? IDirectorySourceAdapter<CrawlerChessScotlandEvent>.SourceKey(CrawlerChessScotlandEvent row) => row.Slug.Length == 0 ? null : PublicIdOf(row.Slug);
    DateOnly? IDirectorySourceAdapter<CrawlerChessScotlandEvent>.Start(CrawlerChessScotlandEvent row) => row.Start;
    string IDirectorySourceAdapter<CrawlerChessScotlandEvent>.Name(CrawlerChessScotlandEvent row) => row.Name;
    string IDirectorySourceAdapter<CrawlerChessScotlandEvent>.PublicId(CrawlerChessScotlandEvent row) => PublicIdOf(row.Slug);
    string? IDirectorySourceAdapter<CrawlerChessScotlandEvent>.Federation(CrawlerChessScotlandEvent row) => Federation;
    // Ohne Ort: diese Liste nennt den Spielort nicht, er steht erst auf der Detailseite. Dann
    // entscheidet allein der Namensvergleich.
    string? IDirectorySourceAdapter<CrawlerChessScotlandEvent>.MatchPlace(CrawlerChessScotlandEvent row) => null;
    string? IDirectorySourceAdapter<CrawlerChessScotlandEvent>.Url(CrawlerChessScotlandEvent row) => row.Url;

    // Altbestand: bis 2026-09-15 trug jeder Vermerk dieser Quelle den ROHEN Slug. Umstellen, BEVOR
    // ein Vermerk gesucht wird — sonst legte der Lauf je Turnier einen zweiten an, und
    // HasOtherNoteOfSameKind blockierte danach jede Zuordnung zu chess-results.
    Task IDirectorySourceAdapter<CrawlerChessScotlandEvent>.BeforeLoopAsync(CancellationToken ct) => MigrateLegacyNotesAsync(ct);

    async Task<bool> IDirectorySourceAdapter<CrawlerChessScotlandEvent>.ApplyAsync(
        TournamentDirectoryEntry own, CrawlerChessScotlandEvent row, DateOnly start, DateTime now, CancellationToken ct)
    {
        own.Name = ExternalDirectorySource.Truncate(row.Name, 500)!;
        own.Federation = Federation;
        own.StartDate = start;
        own.EndDate = row.End ?? start;
        own.StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        ExternalSweepRunner.MarkSeen(own, now);
        ExternalDirectorySource.ApplyClassification(own);
        ApplyYouthMark(own, row.Categories);

        own.TimeControlText = ExternalDirectorySource.Truncate(TimeControlTextOf(row.TimeControls), 300);
        if (own.Speed == TournamentSpeed.Unknown)
            own.Speed = SpeedOf(row.TimeControls);

        // Ein Online-Turnier hat keinen Spielort — kein Detailabruf, kein Pin.
        var isOnline = Has(row.Categories, "Online");

        // „Aktualisiert" heisst hier: die Detailseite war lesbar (Spielort).
        var loaded = false;
        var hasDetail = HasDetail(own, row.Slug);
        if (!isOnline && !hasDetail && _detailBudget > 0)
        {
            _detailBudget--;
            if (await LoadDetailAsync(own, row.Slug, ct))
            {
                loaded = true;
                hasDetail = true;
            }
            await Task.Delay(DetailDelay, ct);
        }

        await ExternalDirectorySource.NoteSourceAsync(_db, own,
            DirectorySourceKind.ScottishChessFederation, PublicIdOf(row.Slug),
            hasDetail ? row.Url : null, now, ct);
        return loaded;
    }

    void IDirectorySourceAdapter<CrawlerChessScotlandEvent>.LogSummary(
        ILogger log, int read, int added, int updated, int matched, int retired) =>
        log.LogInformation(
            "Chess-Scotland-Kalender: {Read} gelesen, {Added} neu, {Updated} mit Spielort, {Matched} zugeordnet, {Retired} zurueckgezogen",
            read, added, updated, matched, retired);


    /// <summary>
    /// Ob die Detailseite dieses Turniers schon gelesen wurde — erkennbar an der Adresse im
    /// Herkunftsvermerk. Ein Vermerk OHNE Adresse heisst „aus der Liste, Detailseite fehlt noch".
    /// </summary>
    internal static bool HasDetail(TournamentDirectoryEntry entry, string slug)
    {
        // Verglichen wird der KURZSCHLUESSEL — das ist seit 2026-09-15 die Kennung im Vermerk.
        var key = PublicIdOf(slug);
        return entry.Sources.Any(s => s.Kind == DirectorySourceKind.ScottishChessFederation
                                      && string.Equals(s.ExternalId, key, StringComparison.OrdinalIgnoreCase)
                                      && s.Url is { Length: > 0 });
    }

    /// <summary>
    /// Die Detailseite holen und ihren Spielort eintragen. Gibt zurueck, ob sie lesbar war — ein
    /// Ausfall kostet den Spielort, nicht den Termin (den hat die Liste schon).
    /// </summary>
    private async Task<bool> LoadDetailAsync(
        TournamentDirectoryEntry entry, string slug, CancellationToken ct)
    {
        ChessScotlandDetail? detail;
        try { detail = await FetchDetailAsync(slug, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Chess Scotland: Detailseite {Slug} nicht lesbar", slug);
            return false;
        }
        if (detail is null) return false;

        if (detail.Venue is not { Length: > 0 } venue) return true;

        var locationChanged = entry.LocationText != venue;
        entry.LocationText = ExternalDirectorySource.Truncate(venue, 300);
        // Der Ort gehoert zum Gruppenschluessel — ApplyClassification lief schon vor dem Abruf.
        DirectoryKeys.ApplyGrouping(entry);

        if ((locationChanged || entry.Lat is null) && entry.GeoSource != GeoSource.Manual)
        {
            var hit = await _geocoding.ResolveAsync(venue, null, Federation, ct);
            if (hit is not null && hit.Source != GeoSource.Ambiguous)
            {
                entry.Lat = hit.Lat;
                entry.Lon = hit.Lon;
                entry.GeoSource = hit.Source;
                entry.GeoPlaceName = ExternalDirectorySource.Truncate(hit.PlaceName, 200);
            }
        }
        return true;
    }

    /// <summary>
    /// Die Bedenkzeit-KLASSE aus den Schlagworten. Ein Kongress kann mehrere zugleich anbieten
    /// (Hauptturnier + Blitz-Abend) — die ERNSTHAFTESTE gilt als die des Turniers, dieselbe
    /// Prioritaet wie bei einer Bedenkzeit-Angabe mit mehreren Phasen ("die erste Zeitangabe
    /// zaehlt"). „Fide" ist ein reiner Wertungs-Vermerk, keine eigene Klasse.
    /// </summary>
    internal static TournamentSpeed SpeedOf(IReadOnlyCollection<string> timeControls)
    {
        if (Has(timeControls, "Standard")) return TournamentSpeed.Standard;
        if (Has(timeControls, "Allegro")) return TournamentSpeed.Rapid;
        if (Has(timeControls, "Blitz")) return TournamentSpeed.Blitz;
        return TournamentSpeed.Unknown;
    }

    internal static string? TimeControlTextOf(IReadOnlyCollection<string> timeControls) =>
        timeControls.Count == 0 ? null : string.Join(", ", timeControls);

    private static bool Has(IReadOnlyCollection<string> values, string target) =>
        values.Any(v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// „Junior" ist nur dann eine verlaessliche JUGEND-Angabe, wenn NICHT gleichzeitig „Adult"
    /// dabeisteht — 25 von 43 gemessenen Terminen tragen beide Schlagworte zugleich (ein Kongress
    /// mit Erwachsenen- UND Jugendabteilung), ein Pin auf „Nachwuchs" waere dort falsch. Sagt der
    /// NAME bereits eine Klasse, ist das die bessere Auskunft und bleibt unangetastet.
    /// </summary>
    internal static void ApplyYouthMark(TournamentDirectoryEntry entry, List<string> categories)
    {
        if (entry.AgeGroups != TournamentAgeGroups.None) return;
        if (!Has(categories, "Junior") || Has(categories, "Adult")) return;

        entry.AgeGroups = TournamentAgeGroups.YouthUnspecified;
    }

    /// <summary>
    /// Stellt die Herkunftsvermerke dieser Quelle vom ROHEN Slug auf den Kurzschluessel um
    /// (<see cref="PublicIdOf"/>). Einmalige Altlast, aber bewusst im Lauf selbst und nicht als
    /// Migration: der Kurzschluessel ist ein SHA-256 in C#, und eine SQL-Nachbildung ueber
    /// <c>SHA2()</c> muesste Byte fuer Byte dasselbe ergeben — faende sie auch nur einen Slug
    /// anders, waere dessen Vermerk fuer immer unerreichbar. Hier rechnet dieselbe Funktion wie
    /// ueberall sonst. Wiederholbar: bereits umgestellte Vermerke erkennt
    /// <see cref="IsShortKey"/> und laesst sie stehen.
    ///
    /// <para>Gemessen vor der Umstellung: Dev 43, Prod 44 Vermerke, alle mit rohem Slug.</para>
    /// </summary>
    internal async Task<int> MigrateLegacyNotesAsync(CancellationToken ct)
    {
        var notes = await _db.TournamentDirectorySources
            .Where(s => s.Kind == DirectorySourceKind.ScottishChessFederation)
            .ToListAsync(ct);

        var moved = 0;
        foreach (var note in notes)
        {
            if (IsShortKey(note.ExternalId)) continue;
            note.ExternalId = PublicIdOf(note.ExternalId);
            moved++;
        }

        if (moved > 0)
        {
            await _db.SaveChangesAsync(ct);
            _log.LogInformation(
                "Chess Scotland: {Count} Herkunftsvermerke vom rohen Slug auf den Kurzschluessel umgestellt",
                moved);
        }
        return moved;
    }

    /// <summary>Ob eine Kennung schon der Kurzschluessel ist — ein echter Slug sieht nie so aus.</summary>
    internal static bool IsShortKey(string externalId) => ShortKeyPattern.IsMatch(externalId);

    private static readonly Regex ShortKeyPattern = new("^sc[0-9a-f]{12}$", RegexOptions.Compiled);

    /// <summary>
    /// Diese Quelle hat keine Nummer — der Slug ist eine echte, aber teils lange Kennung. Er ist
    /// der Kurzwert fuer <see cref="TournamentDirectoryEntry.PublicId"/> (24 Zeichen) UND die
    /// Kennung im Herkunftsvermerk (<see cref="TournamentDirectorySource.ExternalId"/>, 60 Zeichen).
    ///
    /// <para><b>Frueher stand im Vermerk der rohe Slug</b>, mit der Begruendung „bis 54 Zeichen
    /// gemessen". Am 2026-09-10 kam der erste mit 71 — und weil
    /// <see cref="ExternalDirectorySource.NoteSourceAsync"/> eine zu lange Kennung bewusst mit einer
    /// Ausnahme ablehnt statt sie abzuschneiden, brach von da an JEDE NACHT die ganze Quelle ab.
    /// Eine gemessene Hoechstlaenge ist keine Zusage der Quelle; der Kurzschluessel ist immer 14
    /// Zeichen lang.</para>
    /// </summary>
    internal static string PublicIdOf(string slug)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(slug.Trim().ToLowerInvariant()));
        return "sc" + Convert.ToHexStringLower(digest)[..12];
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerChessScotlandEvent>> FetchAsync(DateOnly from, CancellationToken ct)
    {
        var rows = await _httpClientFactory.GetCrawlerJsonAsync<List<ChessScotlandRow>>(
            $"/api/chess-scotland-calendar?from={from:yyyy-MM-dd}", ct) ?? [];
        return rows
            .Where(r => r.Slug is { Length: > 0 } && r.Name is { Length: > 0 })
            .Select(r => new CrawlerChessScotlandEvent(
                r.Slug!, r.Name!, ParseDate(r.StartDate), ParseDate(r.EndDate), r.Url,
                r.Categories ?? [], r.TimeControls ?? []))
            // KEIN Filter auf den Termin. Eine Zeile ohne lesbaren Termin bleibt in der Liste,
            // weil die Verschwunden-Erkennung sie sonst nicht als GELIEFERT sieht — die Quelle
            // fuehrt sie ja. Ausgesiebt wird sie erst in der Schleife, dort steht sie dann schon
            // in `delivered`. Ein geaendertes Datumsformat trifft nicht eine Zeile, sondern alle:
            // ohne das waeren es reihenweise falsche Absagen nach zwei Naechten.
            .ToList();
    }

    private async Task<ChessScotlandDetail?> FetchDetailAsync(string slug, CancellationToken ct)
    {
        // 404 = die Seite gibt es nicht mehr oder sie ist nicht lesbar. Kein Fehler: der Termin
        // steht schon, nur sein Spielort fehlt noch.
        return await _httpClientFactory.GetCrawlerJsonAsync<ChessScotlandDetail>(
            $"/api/chess-scotland-calendar/detail?slug={Uri.EscapeDataString(slug)}", ct,
            absentOn: System.Net.HttpStatusCode.NotFound);
    }

    private sealed record ChessScotlandRow(
        string? Slug, string? Name, string? StartDate, string? EndDate, string? Url,
        List<string>? Categories, List<string>? TimeControls);

    private sealed record ChessScotlandDetail(string? Venue);

    public sealed record CrawlerChessScotlandEvent(
        string Slug, string Name, DateOnly? Start, DateOnly? End, string? Url,
        List<string> Categories, List<string> TimeControls);
}
