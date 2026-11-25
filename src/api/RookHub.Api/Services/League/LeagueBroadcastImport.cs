using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Lichess-Übertragungen (Broadcasts) von Turnieren am Brett in die Spielerkarten (0.608.0, Wunsch 2026-09-30). Viele
/// österreichische Turniere werden dort live übertragen (Tirol Open, Kufsteiner Open, Bundesliga, Meisterschaften), und jede
/// Partie trägt die FIDE-ID beider Spieler — zugeordnet wird über sie wie bei der Megabase
/// (<see cref="LeagueProfileStore.ImportGamesAsync"/>), Quelle <see cref="Source"/>. Gemessen am 2026-09-30: von 406 Partien
/// mit Ligaspielern in neun Übertragungen fehlten 43 im Bestand (vor allem Bundesliga); der Rest steht schon über
/// chess-results da — der Gewinn ist vor allem, dass die Partien schon WÄHREND des Turniers da sind.
/// <list type="bullet">
/// <item>Lesen der Lichess-Antworten: gemeinsamer Baustein <see cref="LichessBroadcasts"/> (auch für die Live-Bretter der
///   Turnierseite).</item>
/// <item>Finden: Lichess-Suche nach <c>LeagueBroadcasts:Queries</c> (Vorgabe <see cref="DefaultQueries"/>), höchstens
///   <see cref="MaxYears"/> Jahre zurück, einmal je <see cref="DiscoverEvery"/>. Weitere per Link (<see cref="AddAsync"/>).</item>
/// <item>Einspielen: nur fertige Partien (kein „*"), Standardschach ab der Grundstellung, Datum mit Punkten; Kommentare,
///   Bewertungen und Uhrzeiten der Übertragung fallen weg (<see cref="CleanPgn"/>). Dieselbe Partie aus einer anderen Quelle
///   wird auch bei abweichendem Datum erkannt (<c>skipSameMoves</c>).</item>
/// <item>Laufende Turniere alle <see cref="RefreshOngoing"/> neu, fertige (alle Runden vorbei) nie wieder. Seit 1.017.0 prüft
///   ein eigener Takt (<see cref="LeagueBroadcastLiveScheduler"/>, <see cref="RunLiveAsync"/>) Übertragungen mit einer LAUFENDEN
///   Runde (<see cref="LiveOf"/>) alle <see cref="LiveEvery"/> — jede Partie ist so wenige Minuten nach ihrem Ergebnis da.</item>
/// <item>Seit 1.017.0 zusätzlich: fertige Partien der EIGENEN Mannschaft eines Vereins (<c>WhiteTeam</c>/<c>BlackTeam</c>) gehen
///   mit echten Namen in dessen Vereins-Datenbank (<see cref="ClubBroadcastGames"/>) — samt Analyse, Lc0 und Zusammenfassung.</item>
/// </list>
/// </summary>
public sealed partial class LeagueBroadcastImport
{
    public const string Source = "Lichess-Übertragung";
    /// <summary>Tirol/Österreich, seit 0.712.0 auch Bayern (Bayerische Einzelmeisterschaften, Bavarian Open, Tegernsee Masters,
    /// Munich Chess Festival — Recherche 07.10.2026; „Bayern", „Oberbayern", „Landesliga Süd" bringen nichts).</summary>
    public static readonly string[] DefaultQueries =
        { "Austria", "Österreich", "Tirol", "Tyrol", "Südtirol", "Innsbruck", "Bavarian", "Bayerische", "Tegernsee", "Munich", "München" };
    public const int MaxPages = 10;
    public static readonly TimeSpan DiscoverEvery = TimeSpan.FromHours(20);
    public static readonly TimeSpan RefreshOngoing = TimeSpan.FromHours(6);
    /// <summary>So lange nach dem letzten Tag gilt ein Turnier als fertig, wenn die Runden-Angaben fehlen.</summary>
    public static readonly TimeSpan FinishedAfter = TimeSpan.FromDays(3);
    /// <summary>Takt für Übertragungen mit laufender Runde (1.017.0, Wunsch „alle ~10 min statt alle 6 h“).</summary>
    public static readonly TimeSpan LiveEvery = TimeSpan.FromMinutes(10);
    /// <summary>So lange nach ihrem Beginn gilt eine begonnene, noch nicht fertige Runde höchstens als laufend (eine Ligarunde dauert
    /// 5–7 h; danach fragt wieder nur der 6-Stunden-Takt — eine Runde, die Lichess nie abschließt, kostet nicht ewig Anfragen).</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromHours(12);
    /// <summary>Meldet Lichess eine Runde als laufend: so lange ab dem Einlesen gilt sie als laufend (jedes Einlesen verlängert).</summary>
    public static readonly TimeSpan OngoingGrace = TimeSpan.FromHours(1);

    /// <summary>Die Kopfzeilen, die aus der Übertragung übernommen werden.</summary>
    private static readonly string[] KeepHeaders =
    {
        "Event", "Site", "Date", "Round", "White", "Black", "Result", "WhiteElo", "BlackElo", "WhiteTitle", "BlackTitle",
        "WhiteFideId", "BlackFideId", "TimeControl", "ECO", "Opening", "GameURL",
    };


    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<LeagueBroadcastImport> _logger;
    private readonly string _lichess;
    private readonly string[] _queries;
    private readonly LeagueClubService _clubs;
    private readonly LeagueBroadcastGate _gate;

    /// <param name="clubs">Die Vereins-Datenbank (1.017.0); ohne (Tests) eine eigene ohne Analysen.</param>
    /// <param name="gate">Nur ein Einspielen zugleich — in der App ein Singleton; ohne ein eigenes.</param>
    public LeagueBroadcastImport(AppDbContext db, HttpClient http, ILogger<LeagueBroadcastImport> logger, IConfiguration? config = null,
        LeagueClubService? clubs = null, LeagueBroadcastGate? gate = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _clubs = clubs ?? new LeagueClubService(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<LeagueClubService>.Instance, config: config);
        _gate = gate ?? new LeagueBroadcastGate();
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
        var q = config?["LeagueBroadcasts:Queries"];
        _queries = string.IsNullOrWhiteSpace(q) ? DefaultQueries
            : q.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        MaxYears = Math.Clamp(config?.GetValue<int?>("LeagueBroadcasts:MaxYears") ?? 5, 1, 30);
    }

    public int MaxYears { get; }
    /// <summary>Pause zwischen zwei Abrufen bei Lichess.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Übertragung als sauberes PGN: nur fertige Partien (1-0, 0-1, ½) im Standardschach ab der Grundstellung mit
    /// mindestens einer FIDE-ID; Datum mit Punkten (ältere Übertragungen schreiben „2024-08-24" — sonst erkennte der Abgleich
    /// mit chess-results die Partie nicht), fehlt es, gilt <c>UTCDate</c>; nur die Hauptvariante, ohne Bewertungen und Uhren.
    /// </summary>
    public static (string Pgn, int Games) CleanPgn(string pgn)
    {
        var (clean, games) = CleanGames(pgn);
        return (clean, games.Count);
    }

    /// <summary>Wie <see cref="CleanPgn"/>, dazu jede übernommene Partie einzeln (Kopfzeilen der Übertragung samt
    /// <c>WhiteTeam</c>/<c>BlackTeam</c>, Datum schon mit Punkten; Hauptvariante als SAN) — für die Vereins-Datenbank.</summary>
    public static (string Pgn, List<LeagueBroadcastGame> Games) CleanGames(string pgn)
    {
        var sb = new StringBuilder();
        var list = new List<LeagueBroadcastGame>();
        foreach (var b in PgnParser.SplitGameBlocks(pgn))
        {
            var h = new Dictionary<string, string>(b.Headers, StringComparer.OrdinalIgnoreCase);
            var result = h.GetValueOrDefault("Result") ?? "";
            if (result is not ("1-0" or "0-1" or "1/2-1/2")) continue;
            if (h.TryGetValue("Variant", out var v) && !v.Equals("Standard", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.ContainsKey("FEN") || h.GetValueOrDefault("SetUp") == "1") continue;
            if (!h.ContainsKey("WhiteFideId") && !h.ContainsKey("BlackFideId")) continue;
            var moveText = PgnParser.SplitGames(b.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            var sans = PgnParser.ExtractMainlineSans(moveText);
            if (sans.Count == 0) continue;
            h["Date"] = DateOf(h.GetValueOrDefault("Date")) ?? DateOf(h.GetValueOrDefault("UTCDate")) ?? "????.??.??";
            foreach (var k in KeepHeaders)
                if (h.TryGetValue(k, out var val) && !string.IsNullOrWhiteSpace(val)) sb.Append(PgnWriter.Tag(k, val.Trim()));
            sb.Append('\n').Append(PgnWriter.MoveText(sans, null, null, result)).Append("\n\n");
            list.Add(new LeagueBroadcastGame(h, sans));
        }
        return (sb.ToString(), list);
    }

    /// <summary>
    /// Wann die Übertragung wieder schnell geprüft werden soll (1.017.0), aus den Runden laut Lichess: <c>LiveUntil</c> = eine Runde
    /// läuft (Lichess <c>ongoing</c>: <paramref name="now"/> + <see cref="OngoingGrace"/>) oder hat vor weniger als
    /// <see cref="LiveWindow"/> begonnen und ist nicht fertig (ihr Beginn + <see cref="LiveWindow"/>); <c>NextRoundAt</c> = Beginn
    /// der nächsten künftigen, nicht fertigen Runde.
    /// </summary>
    public static (DateTime? NextRoundAt, DateTime? LiveUntil) LiveOf(IReadOnlyList<LichessBroadcasts.Round> rounds, DateTime now)
    {
        DateTime? next = null, live = null;
        foreach (var r in rounds.Where(r => !r.Finished))
        {
            DateTime? until = null;
            if (r.Ongoing) until = now + OngoingGrace;
            if (r.StartsAt is { } s)
            {
                if (s > now) next = next is { } n && n <= s ? n : s;
                else if (s + LiveWindow > now && (until is null || s + LiveWindow > until)) until = s + LiveWindow;
            }
            if (until is { } u && (live is null || u > live)) live = until;
        }
        return (next, live);
    }

    /// <summary>„2024-08-24" / „2024.08.24" → „2024.08.24"; Unvollständiges (mit „?") → <c>null</c>.</summary>
    public static string? DateOf(string? s)
    {
        var d = (s ?? "").Trim().Replace('-', '.').Replace('/', '.');
        return DateRe().IsMatch(d) ? d : null;
    }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}$")]
    private static partial Regex DateRe();

    // ── Abrufen ─────────────────────────────────────────────────────────────────────────────────

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var r = await _http.GetAsync(url, ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(ct);
    }

    private Task Wait(CancellationToken ct) => Pause > TimeSpan.Zero ? Task.Delay(Pause, ct) : Task.CompletedTask;

    /// <summary>Neue Übertragungen über die Suche finden und vormerken. → wie viele neu.</summary>
    public async Task<int> DiscoverAsync(CancellationToken ct)
    {
        var horizon = DateTime.UtcNow.AddYears(-MaxYears);
        var known = (await _db.LeagueBroadcasts.Select(b => b.TourId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var q in _queries)
        {
            int? page = 1;
            for (var i = 0; page is { } p && i < MaxPages; i++)
            {
                var json = await GetAsync($"{_lichess}/api/broadcast/search?q={Uri.EscapeDataString(q)}&page={p}", ct);
                await Wait(ct);
                if (json is null) break;
                var (tours, next) = LichessBroadcasts.ParseSearch(json);
                foreach (var t in tours)
                {
                    if ((t.End ?? t.Start) is { } last && last < horizon || !known.Add(t.Id)) continue;
                    _db.LeagueBroadcasts.Add(Row(t, manual: false));
                    added++;
                }
                page = next;
            }
        }
        await _db.SaveChangesAsync(ct);
        return added;
    }

    private static LeagueBroadcast Row(LichessBroadcasts.Tour t, bool manual) => new()
    {
        TourId = t.Id, Name = Cut(t.Name, 200)!, Location = Cut(t.Location, 200), StartsAt = t.Start, EndsAt = t.End, Manual = manual,
        FoundAt = DateTime.UtcNow,
    };

    /// <summary>Eine Übertragung (neu) einspielen: Stand, PGN, Partien mit Ligaspielern in die Karten.</summary>
    public async Task ImportAsync(LeagueBroadcast b, CancellationToken ct)
    {
        using var _ = await _gate.EnterAsync(ct);
        await ImportCoreAsync(b, ct);
    }

    private async Task ImportCoreAsync(LeagueBroadcast b, CancellationToken ct)
    {
        var detail = await GetAsync($"{_lichess}/api/broadcast/{Uri.EscapeDataString(b.TourId)}", ct);
        await Wait(ct);
        if (detail is null)
        {
            b.Error = "Übertragung nicht gefunden";
            b.Finished = true;                                                // gelöscht — nicht jeden Tag wieder fragen
            b.ImportedAt = DateTime.UtcNow;
            await SaveAsync(b, ct);
            return;
        }
        var parsed = LichessBroadcasts.ParseTourDetail(detail);
        var tour = parsed?.Tour;
        var allFinished = parsed?.AllFinished ?? false;
        if (tour is not null)
        {
            b.Name = Cut(tour.Name, 200)!;
            b.Location = Cut(tour.Location, 200) ?? b.Location;
            b.StartsAt = tour.Start ?? b.StartsAt;
            b.EndsAt = tour.End ?? b.EndsAt;
        }
        var pgn = await GetAsync($"{_lichess}/api/broadcast/{Uri.EscapeDataString(b.TourId)}.pgn", ct) ?? "";
        await Wait(ct);
        var (clean, cleanGames) = CleanGames(pgn);
        var (games, _) = clean.Length == 0 ? (0, 0)
            : await new LeagueProfileStore(_db).ImportGamesAsync(clean, Source, ct, skipSameMoves: true);
        await ClubGamesAsync(b, cleanGames, ct);
        b.Games = games;
        b.Error = null;
        b.ImportedAt = DateTime.UtcNow;
        (b.NextRoundAt, b.LiveUntil) = parsed is null ? (null, null) : LiveOf(parsed.Rounds, b.ImportedAt.Value);
        b.Finished = allFinished || b.EndsAt is { } end && end < DateTime.UtcNow - FinishedAfter;
        await SaveAsync(b, ct);
    }

    /// <summary>Die Partien mit der eigenen Mannschaft in die Vereins-Datenbank jedes Vereins (<see cref="ClubBroadcastGames"/>). Beiwerk:
    /// ein Fehler hier lässt das Einspielen in die Karten nicht scheitern (beim nächsten Takt wieder — schon Übernommenes zählt nicht doppelt).</summary>
    private async Task ClubGamesAsync(LeagueBroadcast b, List<LeagueBroadcastGame> games, CancellationToken ct)
    {
        if (games.Count == 0 || !games.Any(g => g.H("WhiteTeam") != null || g.H("BlackTeam") != null)) return;
        foreach (var club in await _db.LeagueClubs.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct))
        {
            try
            {
                await _clubs.ImportBroadcastGamesAsync(club, games, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "LeagueHub: übertragene Partien von {Tour} nicht in die Vereins-Datenbank {Club} übernommen", b.TourId, club.Id);
                _db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Den Stand der Übertragung schreiben — ausdrücklich: das Einspielen in die Karten leert den ChangeTracker
    /// (<see cref="LeagueProfileStore.ImportGamesAsync"/>), danach hängt die Zeile nicht mehr daran.</summary>
    private async Task SaveAsync(LeagueBroadcast b, CancellationToken ct)
    {
        _db.LeagueBroadcasts.Update(b);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Eine Übertragung per Link (Turnier oder Runde) hinzufügen und gleich einspielen — für Turniere, die die Suche nicht
    /// findet (Opens im Ausland mit Tiroler Spielern). Reason: <c>invalidUrl</c>, <c>notFound</c>.
    /// </summary>
    public async Task<(LeagueBroadcast? Broadcast, string? Reason)> AddAsync(string? input, CancellationToken ct)
    {
        if (LichessBroadcasts.IdsOf(input) is not { } ids) return (null, "invalidUrl");
        var detail = await GetAsync(ids.TourId is { } t ? $"{_lichess}/api/broadcast/{t}" : $"{_lichess}/api/broadcast/-/-/{ids.RoundId}", ct);
        if (detail is null || LichessBroadcasts.ParseTourDetail(detail)?.Tour is not { } tour) return (null, "notFound");
        var b = await _db.LeagueBroadcasts.FirstOrDefaultAsync(x => x.TourId == tour.Id, ct);
        if (b is null) _db.LeagueBroadcasts.Add(b = Row(tour, manual: true));
        b.Finished = false;                                                   // ausdrücklich gewünscht: neu holen
        await _db.SaveChangesAsync(ct);
        await ImportAsync(b, ct);
        return (b, null);
    }

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    /// <summary>
    /// Ein Durchgang: mit <paramref name="discover"/> erst suchen (der Takt ruft das höchstens alle <see cref="DiscoverEvery"/>),
    /// dann fällige Übertragungen einspielen — nie eingespielte, und laufende alle <see cref="RefreshOngoing"/>; noch nicht
    /// begonnene warten. → noch etwas offen?
    /// </summary>
    public async Task<bool> RunOnceAsync(TimeSpan budget, bool discover, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        try
        {
            if (discover) await DiscoverAsync(ct);
            var now = DateTime.UtcNow;
            var refresh = now - RefreshOngoing;
            var due = await _db.LeagueBroadcasts
                .Where(b => !b.Finished && (b.StartsAt == null || b.StartsAt <= now) && (b.ImportedAt == null || b.ImportedAt < refresh))
                .OrderBy(b => b.ImportedAt.HasValue).ThenByDescending(b => b.StartsAt).ToListAsync(ct);
            var done = 0;
            foreach (var b in due)
            {
                if (DateTime.UtcNow - started >= budget) break;
                try
                {
                    await ImportAsync(b, ct);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(e, "LeagueHub: Übertragung {Tour} nicht eingespielt", b.TourId);
                    b.Error = Cut(e.Message, 300);
                    b.ImportedAt = DateTime.UtcNow;                           // nicht gleich wieder, beim nächsten Takt
                    await SaveAsync(b, ct);
                }
                done++;
            }
            return done < due.Count;
        }
        catch (LeagueOnlineSync.RateLimitedException e)
        {
            _logger.LogDebug("LeagueHub: {Message} — Übertragungen pausiert", e.Message);   // Warnung schreibt LeagueRateGate
            return true;
        }
    }

    /// <summary>
    /// Der schnelle Takt (1.017.0, <see cref="LeagueBroadcastLiveScheduler"/>): jede nicht fertige Übertragung mit laufender Runde
    /// (<see cref="LeagueBroadcast.LiveUntil"/> noch nicht vorbei) oder einer Runde, die inzwischen begonnen haben sollte
    /// (<see cref="LeagueBroadcast.NextRoundAt"/> erreicht), die vor mindestens <see cref="LiveEvery"/> zuletzt eingespielt wurde.
    /// Alle anderen bleiben beim 6-Stunden-Takt. Ein 429 bricht ab (das Tor pausiert dann alle Lichess-Aufrufe). → wie viele eingespielt.
    /// </summary>
    public async Task<int> RunLiveAsync(CancellationToken ct, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var since = now - LiveEvery + TimeSpan.FromSeconds(30);   // Takt und Laufzeit verschieben sich — nicht erst beim übernächsten
        var due = await _db.LeagueBroadcasts
            .Where(b => !b.Finished && (b.ImportedAt == null || b.ImportedAt < since)
                && (b.LiveUntil != null && b.LiveUntil > now || b.NextRoundAt != null && b.NextRoundAt <= now))
            .OrderBy(b => b.ImportedAt).ToListAsync(ct);
        var done = 0;
        foreach (var b in due)
        {
            try
            {
                await ImportAsync(b, ct);
                done++;
            }
            catch (LeagueOnlineSync.RateLimitedException e)
            {
                _logger.LogDebug("LeagueHub: {Message} — schneller Takt der Übertragungen pausiert", e.Message);
                break;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(e, "LeagueHub: laufende Übertragung {Tour} nicht eingespielt", b.TourId);
                b.Error = Cut(e.Message, 300);
                b.ImportedAt = DateTime.UtcNow;
                await SaveAsync(b, ct);
            }
        }
        return done;
    }

    /// <summary>
    /// Nachtrag nach dem Deploy (1.017.2): nicht fertige Übertragungen, deren <see cref="LeagueBroadcast.LiveUntil"/> UND
    /// <see cref="LeagueBroadcast.NextRoundAt"/> leer sind (Zeilen von vorher), holen einmal NUR den Stand mit den Runden
    /// (<c>/api/broadcast/{id}</c>, kein PGN) und setzen daraus beide Felder (<see cref="LiveOf"/>) — danach greift der schnelle
    /// Takt, ohne dass jemand die Übertragung neu hinzufügen muss. <see cref="LeagueBroadcast.ImportedAt"/> bleibt, wie es ist.
    /// Ein 429 wirft (<see cref="LeagueOnlineSync.RateLimitedException"/>) — der Aufrufer versucht es beim nächsten Takt wieder.
    /// → wie viele aufgefrischt.
    /// </summary>
    public async Task<int> RefreshUnknownRoundsAsync(CancellationToken ct, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var rows = await _db.LeagueBroadcasts.Where(b => !b.Finished && b.LiveUntil == null && b.NextRoundAt == null)
            .OrderBy(b => b.TourId).ToListAsync(ct);
        var done = 0;
        foreach (var b in rows)
        {
            using var _ = await _gate.EnterAsync(ct);
            try
            {
                var detail = await GetAsync($"{_lichess}/api/broadcast/{Uri.EscapeDataString(b.TourId)}", ct);
                await Wait(ct);
                if (detail is null || LichessBroadcasts.ParseTourDetail(detail) is not { } parsed) continue;
                (b.NextRoundAt, b.LiveUntil) = LiveOf(parsed.Rounds, now);
                await SaveAsync(b, ct);
                done++;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(e, "LeagueHub: Runden der Übertragung {Tour} nicht nachgetragen", b.TourId);
            }
        }
        return done;
    }

    /// <summary>Alle vorgemerkten Übertragungen (jüngste zuerst) als JSON für die Verwaltung.</summary>
    public async Task<List<object>> ListAsync(CancellationToken ct) =>
        (await _db.LeagueBroadcasts.AsNoTracking().OrderByDescending(b => b.StartsAt).ToListAsync(ct))
        .Select(b => (object)new
        {
            tourId = b.TourId, name = b.Name, location = b.Location, url = $"https://lichess.org/broadcast/-/{b.TourId}",
            startsAt = b.StartsAt?.ToString("yyyy-MM-dd"), endsAt = b.EndsAt?.ToString("yyyy-MM-dd"), manual = b.Manual,
            importedAt = b.ImportedAt is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("O") : null,
            finished = b.Finished, games = b.Games, error = b.Error,
            nextRoundAt = b.NextRoundAt is { } n ? DateTime.SpecifyKind(n, DateTimeKind.Utc).ToString("O") : null,
            liveUntil = b.LiveUntil is { } l ? DateTime.SpecifyKind(l, DateTimeKind.Utc).ToString("O") : null,
        }).ToList();
}
