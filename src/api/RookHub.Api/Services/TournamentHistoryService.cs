using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Der Turnierverlauf eines Spielers: welche Turniere er gespielt hat, welche noch kommen, und in
/// den gespielten Punkte, Platz und Performance-Rating.
///
/// <para><b>Zwei Quellen, zwei Kostenklassen.</b> Die chess-results-Spielersuche liefert in EINEM
/// Abruf ALLE Teilnahmen eines Spielers — vergangene und kuenftige, je Zeile mit Datum, Platz,
/// Rundenzahl, Teilnehmerzahl und (nur im Link versteckt) der Startnummer. Punkte,
/// Performance-Rating und Elo-Aenderung stehen dagegen nur auf der SPIELERKARTE, und die kostet
/// einen Abruf je Turnier. An einem echten Konto gemessen: 23 Turniere, 12 davon gespielt — also
/// ein Abruf fuer die Liste und zwoelf fuer die Ergebnisse, rund zwanzig Sekunden hinter dem
/// Rate-Limiter des Crawlers.</para>
///
/// <para><b>Daraus folgt die Aufteilung.</b> Die LISTE wird beim Aufruf geholt, wenn sie zu alt
/// ist (ein Abruf, gut eine Sekunde), und die Ansicht steht damit sofort — mit Platz und Termin,
/// also dem, was man beim Ueberfliegen sucht. Die KARTEN laufen im Hintergrund nach; ein
/// abgeschlossenes Turnier aendert sich nie wieder, der Zwischenspeicher gilt also fuer immer.
/// Die Antwort sagt, wie viele noch fehlen, damit die Seite sich selbst nachladen kann statt eine
/// halbe Tabelle als endgueltig auszugeben.</para>
///
/// <para><b>Ein kuenftiges Turnier bekommt keinen Kartenabruf.</b> Es hat noch kein Ergebnis (der
/// Player-info-Block ist leer), und der Platz in der Trefferliste steht auf „-". Die
/// Unterscheidung spart bei dem gemessenen Konto elf von 23 Abrufen.</para>
/// </summary>
public class TournamentHistoryService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<TournamentHistoryService> _log;

    public TournamentHistoryService(
        AppDbContext db, IHttpClientFactory httpClientFactory, IBackgroundTaskQueue queue,
        ILogger<TournamentHistoryService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _queue = queue;
        _log = log;
    }

    /// <summary>
    /// Wie lange die Trefferliste gilt. Ein Turnier kommt Wochen vor dem Termin in die Liste und
    /// verschwindet nie — stuendlich nachzufragen brachte nichts als Last bei chess-results.
    /// </summary>
    internal TimeSpan ListTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Wie viele Spielerkarten je Aufruf in den Hintergrund gehen. Deckel, weil jede einen
    /// Seitenabruf kostet: bei einem frischen Konto sind zwoelf offen, bei einem Vielspieler
    /// hundert, und die sollen nicht in einem Rutsch gegen chess-results laufen.
    /// </summary>
    internal int MaxCardsPerRequest { get; set; } = 25;

    /// <summary>Ein Spieler, wie ihn diese Ansicht braucht.</summary>
    public sealed record PlayerIdentity(string Key, string LastName, string? FirstName, string? FideId, string? IdentNumber);

    /// <summary>
    /// Warum ein Konto keinen Verlauf hat. Ein Grund ist besser als eine leere Tabelle: „trage
    /// deinen Namen ins Profil ein" ist eine Handlungsanweisung, „keine Turniere" waere eine
    /// Falschaussage.
    /// </summary>
    public enum HistoryStatus
    {
        Ok = 0,
        /// <summary>Kein Nachname im Profil — die Spielersuche laesst sich nicht stellen.</summary>
        NoName = 1,
        /// <summary>chess-results war nicht erreichbar; der Zwischenspeicher (falls vorhanden) gilt weiter.</summary>
        SourceUnavailable = 2,
    }

    public sealed record PlayerHistory(
        int UserId,
        string DisplayName,
        HistoryStatus Status,
        List<PlayerTournamentResult> Results,
        int PendingResults);

    /// <summary>
    /// Der Verlauf mehrerer Konten. Mehrere, weil die Ansicht auf Freunde umschaltbar ist und
    /// „alle Freunde" sonst N Anfragen waere.
    /// </summary>
    public async Task<List<PlayerHistory>> GetAsync(
        IReadOnlyList<int> userIds, CancellationToken ct = default)
    {
        var profiles = await _db.UserProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new
            {
                p.UserId, p.FirstName, p.LastName, p.DisplayName, p.FideId, p.ChessResultsId,
                Username = p.User.Username,
            })
            .ToListAsync(ct);

        var histories = new List<PlayerHistory>();
        foreach (var profile in profiles)
        {
            var name = profile.DisplayName ?? profile.Username;
            var identity = IdentityOf(profile.LastName, profile.FirstName, profile.FideId, profile.ChessResultsId);
            if (identity is null)
            {
                histories.Add(new PlayerHistory(profile.UserId, name, HistoryStatus.NoName, [], 0));
                continue;
            }

            var status = await RefreshListIfStaleAsync(identity, ct);
            var results = await _db.PlayerTournamentResults.AsNoTracking()
                .Where(r => r.PlayerKey == identity.Key)
                .OrderByDescending(r => r.EndDate)
                .ToListAsync(ct);

            var pending = QueueMissingCards(identity, results);
            histories.Add(new PlayerHistory(profile.UserId, name, status, results, pending));
        }
        return histories;
    }

    /// <summary>
    /// Wer der Spieler ist. Der Nachname ist Pflicht — die chess-results-Spielersuche kennt keine
    /// Suche ueber die Ident-Nummer, sie sucht ueber den NAMEN. Die Kennung entscheidet danach,
    /// welche der Namensgleichen gemeint ist.
    ///
    /// <para>Die FIDE-Kennung hat Vorrang: bei einem AUSLANDS-Turnier steht in der
    /// chess-results-Ident-Spalte „0", und dann traegt allein die FIDE-ID die Identitaet. Ohne
    /// beides bleibt der Name als Schluessel — dann sieht man bei Namensgleichheit fremde
    /// Turniere, und genau deshalb sagt die Ansicht, dass eine Kennung im Profil das behebt.</para>
    /// </summary>
    internal static PlayerIdentity? IdentityOf(
        string? lastName, string? firstName, string? fideId, string? chessResultsId)
    {
        var last = (lastName ?? "").Trim();
        if (last.Length < 2) return null;

        var fide = Clean(fideId);
        var ident = Clean(chessResultsId);
        var key = fide is not null ? $"fide:{fide}"
            : ident is not null ? $"cr:{ident}"
            : "name:" + GeoTextNormalizer.Normalize($"{last} {firstName}").Replace(' ', '-');

        return new PlayerIdentity(key, last, string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim(),
            fide, ident);
    }

    /// <summary>„0" ist bei chess-results „keine Nummer" und darf nicht als Kennung gelten.</summary>
    private static string? Clean(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 0 || text == "0" ? null : text;
    }

    private async Task<HistoryStatus> RefreshListIfStaleAsync(PlayerIdentity identity, CancellationToken ct)
    {
        var sync = await _db.PlayerHistorySyncs.FirstOrDefaultAsync(s => s.PlayerKey == identity.Key, ct);
        if (sync is not null && DateTime.UtcNow - sync.LastFetchedAt < ListTtl) return HistoryStatus.Ok;

        List<CrawlerPlayerTournament> rows;
        try
        {
            rows = await FetchListAsync(identity, ct);
        }
        // Ein HttpClient-TIMEOUT kommt als TaskCanceledException, also als
        // OperationCanceledException, obwohl der Aufrufer nichts abgebrochen hat.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Turnierverlauf {Key}: Trefferliste nicht erreichbar", identity.Key);
            if (sync is not null) sync.LastError = Truncate(ex.Message);
            else _db.PlayerHistorySyncs.Add(new PlayerHistorySync
            {
                PlayerKey = identity.Key,
                // NICHT jetzt: der Zeitstempel steht fuer einen erfolgreichen Abruf. Sonst
                // bliebe ein Konto nach einem Netzfehler zwoelf Stunden ohne Verlauf.
                LastFetchedAt = DateTime.MinValue,
                LastError = Truncate(ex.Message),
            });
            await _db.SaveChangesAsync(ct);
            // Der Zwischenspeicher gilt weiter — eine alte Liste ist besser als keine.
            return HistoryStatus.SourceUnavailable;
        }

        await MergeAsync(identity, rows, ct);

        if (sync is null)
        {
            _db.PlayerHistorySyncs.Add(new PlayerHistorySync
            {
                PlayerKey = identity.Key, LastFetchedAt = DateTime.UtcNow,
            });
        }
        else
        {
            sync.LastFetchedAt = DateTime.UtcNow;
            sync.LastError = null;
        }
        await _db.SaveChangesAsync(ct);
        return HistoryStatus.Ok;
    }

    /// <summary>
    /// Die Zeilen der Trefferliste in den Zwischenspeicher uebernehmen. Die Ergebnisfelder der
    /// Spielerkarte bleiben dabei UNANGETASTET: die Liste kennt sie nicht, und sie zu leeren
    /// hiesse, die zwoelf Abrufe von vorhin wegzuwerfen.
    /// </summary>
    private async Task MergeAsync(
        PlayerIdentity identity, List<CrawlerPlayerTournament> rows, CancellationToken ct)
    {
        var mine = rows.Where(r => BelongsTo(identity, r)).ToList();
        if (mine.Count == 0) return;

        var ids = mine.Select(r => r.TournamentId).ToList();
        var existing = await _db.PlayerTournamentResults
            .Where(r => r.PlayerKey == identity.Key && ids.Contains(r.ChessResultsId))
            .ToDictionaryAsync(r => r.ChessResultsId, StringComparer.Ordinal, ct);

        var now = DateTime.UtcNow;
        foreach (var row in mine)
        {
            if (!existing.TryGetValue(row.TournamentId, out var result))
            {
                result = new PlayerTournamentResult
                {
                    PlayerKey = identity.Key,
                    ChessResultsId = row.TournamentId,
                };
                _db.PlayerTournamentResults.Add(result);
                existing[row.TournamentId] = result;
            }

            result.Snr = row.Snr ?? result.Snr;
            result.TournamentName = Truncate(row.TournamentName, 500);
            result.EndDate = ParseDate(row.EndDate) ?? result.EndDate;
            result.Rank = row.Rank;
            result.Rounds = row.Rounds;
            result.PlayerCount = row.PlayerCount;
            result.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Gehoert diese Zeile zu diesem Spieler? Die Suche geht ueber den NAMEN und liefert damit
    /// auch die Namensgleichen. Die FIDE-ID entscheidet, wenn beide sie tragen; sonst die
    /// Ident-Nummer. Bei einem Auslandsturnier steht dort „0" — dann faellt die Zeile auf die
    /// FIDE-ID zurueck, und ohne jede Kennung gilt der Name (und damit womoeglich zu viel; die
    /// Ansicht sagt das).
    /// </summary>
    internal static bool BelongsTo(PlayerIdentity identity, CrawlerPlayerTournament row)
    {
        var rowFide = Clean(row.FideId);
        var rowIdent = Clean(row.IdentNumber);

        if (identity.FideId is not null && rowFide is not null)
            return string.Equals(identity.FideId, rowFide, StringComparison.Ordinal);
        if (identity.IdentNumber is not null && rowIdent is not null)
            return string.Equals(identity.IdentNumber, rowIdent, StringComparison.Ordinal);

        return identity.FideId is null && identity.IdentNumber is null;
    }

    /// <summary>
    /// Reiht die fehlenden Spielerkarten in den Hintergrund und sagt, wie viele es sind.
    ///
    /// <para>Nur GESPIELTE Turniere: ein kuenftiges hat kein Ergebnis, und der Platz in der
    /// Trefferliste steht dort auf „-". Neueste zuerst — das letzte Turnier ist das, dessen
    /// Ergebnis man sucht.</para>
    /// </summary>
    private int QueueMissingCards(PlayerIdentity identity, List<PlayerTournamentResult> results)
    {
        var missing = results
            .Where(r => r.CardFetchedAt is null && r.Rank is not null && r.Snr > 0)
            .OrderByDescending(r => r.EndDate)
            .ToList();
        if (missing.Count == 0) return 0;

        foreach (var result in missing.Take(MaxCardsPerRequest))
        {
            var key = identity.Key;
            var chessResultsId = result.ChessResultsId;
            var snr = result.Snr;

            // Eigener Scope: der Auftrag laeuft, nachdem die Anfrage samt ihrem DbContext weg ist.
            _queue.EnqueueAsync(async (provider, token) =>
            {
                var scoped = provider.GetRequiredService<TournamentHistoryService>();
                await scoped.FetchCardAsync(key, chessResultsId, snr, token);
            });
        }
        return missing.Count;
    }

    /// <summary>
    /// Holt EINE Spielerkarte und schreibt ihre Werte in den Zwischenspeicher. Oeffentlich, weil
    /// der Hintergrund-Auftrag sie in einem eigenen Scope aufruft.
    /// </summary>
    public async Task FetchCardAsync(
        string playerKey, string chessResultsId, int snr, CancellationToken ct = default)
    {
        var result = await _db.PlayerTournamentResults
            .FirstOrDefaultAsync(r => r.PlayerKey == playerKey && r.ChessResultsId == chessResultsId, ct);
        if (result is null || result.CardFetchedAt is not null) return;

        CrawlerPlayerCard? card;
        try
        {
            card = await FetchCardFromCrawlerAsync(chessResultsId, snr, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Der Vermerk bleibt LEER: ein Netzfehler ist keine Auskunft ueber das Turnier, und
            // beim naechsten Seitenaufruf soll es wieder eingereiht werden.
            _log.LogWarning(ex, "Spielerkarte {Id}/{Snr} nicht erreichbar", chessResultsId, snr);
            return;
        }

        // Auch ein LEERES Ergebnis wird vermerkt — sonst wird dieselbe Seite bei jedem Aufruf
        // erneut geholt. „Keine Werte" heisst hier: das Turnier wurde noch nicht gespielt.
        result.CardFetchedAt = DateTime.UtcNow;
        result.UpdatedAt = DateTime.UtcNow;

        if (card is { HasResult: true })
        {
            result.Points = card.Points;
            result.PerformanceRating = card.PerformanceRating;
            result.RatingChange = card.RatingChange;
            result.RatingInternational = card.RatingInternational;
            // Der Platz der KARTE ist der genauere: die Trefferliste rundet bei Gleichstand.
            result.Rank = card.Rank ?? result.Rank;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ----- Crawler ----------------------------------------------------------

    private async Task<List<CrawlerPlayerTournament>> FetchListAsync(
        PlayerIdentity identity, CancellationToken ct)
    {
        var path = $"/api/tournament-search/player-history?lastName={Uri.EscapeDataString(identity.LastName)}";
        if (identity.FirstName is not null) path += $"&firstName={Uri.EscapeDataString(identity.FirstName)}";

        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<CrawlerPlayerTournament>>(json, JsonOptions) ?? [];
    }

    private async Task<CrawlerPlayerCard?> FetchCardFromCrawlerAsync(
        string chessResultsId, int snr, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(
            $"/api/tournament-search/player-card?id={Uri.EscapeDataString(chessResultsId)}&snr={snr}", ct);

        // 204: die Seite hat keinen Player-info-Block (falsche Startnummer). Kein Fehler, aber
        // auch nichts zu speichern.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<CrawlerPlayerCard>(json, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record CrawlerPlayerTournament(
        string TournamentId, string TournamentName, string? EndDate, int? Snr,
        string? PlayerName, string? IdentNumber, string? FideId, string? Club, string? Federation,
        int? Rank, int? Rounds, int? PlayerCount);

    internal sealed record CrawlerPlayerCard(
        decimal? Points, int? Rank, int? PerformanceRating, decimal? RatingChange,
        int? RatingInternational, bool HasResult);

    private static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] formats = ["yyyy/MM/dd", "yyyy-MM-dd", "dd.MM.yyyy"];
        return DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
    }

    private static string Truncate(string? value, int max = 500) =>
        (value ?? "").Length <= max ? value ?? "" : value![..max];
}
