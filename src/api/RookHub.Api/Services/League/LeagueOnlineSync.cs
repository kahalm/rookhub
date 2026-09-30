using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>Weckruf für den Abruf — ein neues oder geändertes Konto soll nicht bis zum nächsten Takt warten.</summary>
public sealed class LeagueOnlineSyncSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Wake()
    {
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Holt die Partien der Online-Konten der Ligaspieler (0.605.0, Wunsch 2026-09-30: „im Hintergrund holst du die Spiele
/// dieser User und legst sie in der DB ab"). Beide Seiten öffentlich und ohne Anmeldung: Lichess über den Partie-Export
/// (ndjson, älteste zuerst, ab dem Stand des Kontos), chess.com über die Monatsarchive. Nur Standardschach ab der
/// Grundstellung, höchstens <see cref="MaxYears"/> Jahre zurück. Je Aufruf ein Deckel (<see cref="LichessPagesPerCall"/>,
/// <see cref="ChessComArchivesPerCall"/>) — was übrig bleibt, holt der nächste (<c>SyncMore</c>).
/// </summary>
public sealed class LeagueOnlineSync
{
    public const string ClientName = "LeagueOnline";
    /// <summary>So viele Halbzüge stehen in <see cref="LeagueOnlineGame.Line"/> — wie tief der Baum geht.</summary>
    public const int LineMaxPlies = LeagueProfileStore.TreeMaxPlies;
    /// <summary>Mehr Halbzüge werden nicht gespeichert (Partien über 300 Züge gibt es, gebraucht werden sie nicht).</summary>
    public const int MaxStoredPlies = 600;
    public const int LichessPageSize = 500;
    public const int LichessPagesPerCall = 4;
    public const int ChessComArchivesPerCall = 12;

    /// <summary>Die Bedenkzeit-Klassen, in denen gespeichert wird — Filter im Eröffnungsbaum.</summary>
    public static readonly string[] Speeds = { "bullet", "blitz", "rapid", "classical", "correspondence" };

    public sealed record Game(string ExternalId, DateTime PlayedAt, string Speed, bool Rated, bool White, string Result,
        string? Opponent, int? OpponentRating, int? PlayerRating, IReadOnlyList<string> Moves);

    /// <summary>Eine Seite des Abrufs: die lesbaren Partien und bis wohin gelesen wurde (auch Übersprungenes zählt).</summary>
    public sealed record Page(List<Game> Games, long Cursor, int Rows);

    public sealed class RateLimitedException(string site) : Exception($"{site}: zu viele Anfragen");

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<LeagueOnlineSync> _logger;
    private readonly string _lichess;

    public LeagueOnlineSync(AppDbContext db, HttpClient http, ILogger<LeagueOnlineSync> logger, IConfiguration? config = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
        MaxYears = Math.Clamp(config?.GetValue<int?>("LeagueOnline:MaxYears") ?? 5, 1, 30);
    }

    /// <summary>So weit zurück wird geholt (<c>LeagueOnline:MaxYears</c>, Vorgabe 5).</summary>
    public int MaxYears { get; }

    private DateTime Horizon => DateTime.UtcNow.AddYears(-MaxYears);

    // ── Ein Konto ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Holt das nächste Stück eines Kontos und speichert es. <c>true</c> = es ist noch mehr da.</summary>
    public async Task<bool> SyncAccountAsync(LeagueOnlineAccount acc, CancellationToken ct)
    {
        var known = (await _db.LeagueOnlineGames.AsNoTracking().Where(g => g.AccountId == acc.Id).Select(g => g.ExternalId)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var more = false;
        try
        {
            more = acc.Site switch
            {
                LeagueOnlineSites.Lichess => await LichessAsync(acc, known, ct),
                LeagueOnlineSites.ChessCom => await ChessComAsync(acc, known, ct),
                _ => throw new InvalidOperationException($"Seite {acc.Site} wird nicht abgerufen."),
            };
            acc.SyncError = null;
        }
        catch (RateLimitedException) { throw; }
        catch (AccountMissingException)
        {
            acc.SyncError = "Konto nicht gefunden";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "LeagueHub: Online-Partien von {Site}/{User} nicht geholt", acc.Site, acc.UserName);
            acc.SyncError = e.Message.Length > 300 ? e.Message[..300] : e.Message;
        }
        acc.SyncedAt = DateTime.UtcNow;
        acc.SyncMore = more && acc.SyncError is null;
        acc.GameCount = await _db.LeagueOnlineGames.CountAsync(g => g.AccountId == acc.Id, ct);
        await _db.SaveChangesAsync(ct);
        return acc.SyncMore;
    }

    private sealed class AccountMissingException : Exception;

    private async Task<bool> LichessAsync(LeagueOnlineAccount acc, HashSet<string> known, CancellationToken ct)
    {
        var since = Math.Max(acc.SyncCursor + 1, new DateTimeOffset(Horizon).ToUnixTimeMilliseconds());
        for (var page = 0; page < LichessPagesPerCall; page++)
        {
            var url = $"{_lichess}/api/games/user/{Uri.EscapeDataString(acc.UserName)}?since={since}&sort=dateAsc&max={LichessPageSize}"
                      + "&moves=true&clocks=false&evals=false&opening=false&pgnInJson=false";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/x-ndjson");
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitedException("Lichess");
            if (resp.StatusCode == HttpStatusCode.NotFound) throw new AccountMissingException();
            resp.EnsureSuccessStatusCode();
            var p = ParseLichess(await resp.Content.ReadAsStringAsync(ct), acc.UserName);
            await StoreAsync(acc, p.Games, known, ct);
            if (p.Cursor > acc.SyncCursor) acc.SyncCursor = p.Cursor;
            await _db.SaveChangesAsync(ct);
            if (p.Rows < LichessPageSize || p.Cursor <= 0) return false;
            since = p.Cursor + 1;
        }
        return true;
    }

    private async Task<bool> ChessComAsync(LeagueOnlineAccount acc, HashSet<string> known, CancellationToken ct)
    {
        var user = Uri.EscapeDataString(acc.UserName.ToLowerInvariant());
        using var list = await _http.GetAsync($"https://api.chess.com/pub/player/{user}/games/archives", ct);
        if (list.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitedException("chess.com");
        if (list.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) throw new AccountMissingException();
        list.EnsureSuccessStatusCode();
        var from = acc.SyncCursor > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(acc.SyncCursor).UtcDateTime : Horizon;
        var months = ArchivesFrom(await list.Content.ReadAsStringAsync(ct), from.Year, from.Month);
        foreach (var url in months.Take(ChessComArchivesPerCall))
        {
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitedException("chess.com");
            if (resp.StatusCode == HttpStatusCode.NotFound) continue;
            resp.EnsureSuccessStatusCode();
            var p = ParseChessCom(await resp.Content.ReadAsStringAsync(ct), acc.UserName);
            await StoreAsync(acc, p.Games, known, ct);
            if (p.Cursor > acc.SyncCursor) acc.SyncCursor = p.Cursor;
            await _db.SaveChangesAsync(ct);
        }
        return months.Count > ChessComArchivesPerCall;
    }

    private Task StoreAsync(LeagueOnlineAccount acc, IEnumerable<Game> games, HashSet<string> known, CancellationToken ct)
    {
        var horizon = Horizon;
        foreach (var g in games)
        {
            if (g.PlayedAt < horizon || !known.Add(g.ExternalId)) continue;
            var moves = g.Moves.Take(MaxStoredPlies).ToList();
            _db.LeagueOnlineGames.Add(new LeagueOnlineGame
            {
                AccountId = acc.Id, FideId = acc.FideId, ExternalId = g.ExternalId, PlayedAt = g.PlayedAt, Speed = g.Speed,
                Rated = g.Rated, White = g.White, Result = g.Result, Opponent = Cut(g.Opponent, 60),
                OpponentRating = g.OpponentRating, PlayerRating = g.PlayerRating,
                Line = LineOf(moves), Moves = string.Join(' ', moves), Plies = moves.Count,
            });
        }
        return Task.CompletedTask;
    }

    /// <summary>Die ersten <see cref="LineMaxPlies"/> Halbzüge, höchstens 400 Zeichen (die Spalte).</summary>
    public static string LineOf(IReadOnlyList<string> moves)
    {
        var line = string.Join(' ', moves.Take(LineMaxPlies));
        while (line.Length > 400) line = line[..line.LastIndexOf(' ')];
        return line;
    }

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    /// <summary>Lichess-Export (ndjson). Cursor = jüngstes <c>createdAt</c> aller Zeilen, auch übersprungener.</summary>
    public static Page ParseLichess(string ndjson, string user)
    {
        var games = new List<Game>();
        long cursor = 0;
        var rows = 0;
        foreach (var raw in ndjson.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("createdAt", out var ca)) continue;
            rows++;
            var created = ca.GetInt64();
            if (created > cursor) cursor = created;
            if (Str(r, "variant") != "standard" || r.TryGetProperty("initialFen", out _)) continue;
            var speed = Str(r, "speed") switch
            {
                "ultraBullet" or "bullet" => "bullet",
                "blitz" => "blitz",
                "rapid" => "rapid",
                "classical" => "classical",
                "correspondence" => "correspondence",
                _ => null,
            };
            if (speed is null || !r.TryGetProperty("players", out var players)) continue;
            var whiteId = PlayerId(players, "white");
            var blackId = PlayerId(players, "black");
            bool white;
            if (string.Equals(whiteId, user, StringComparison.OrdinalIgnoreCase)) white = true;
            else if (string.Equals(blackId, user, StringComparison.OrdinalIgnoreCase)) white = false;
            else continue;
            var result = Str(r, "winner") switch
            {
                "white" => "1-0",
                "black" => "0-1",
                _ => Str(r, "status") is "draw" or "stalemate" or "outoftime" or "timeout" or "insufficientMaterialClaim" ? "1/2-1/2" : null,
            };
            var moves = (Str(r, "moves") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (result is null || moves.Length == 0) continue;
            var opp = white ? "black" : "white";
            games.Add(new Game(Str(r, "id") ?? "", DateTimeOffset.FromUnixTimeMilliseconds(created).UtcDateTime, speed,
                r.TryGetProperty("rated", out var rated) && rated.ValueKind == JsonValueKind.True, white, result,
                PlayerName(players, opp), Rating(players, opp), Rating(players, white ? "white" : "black"), moves));
        }
        return new Page(games.Where(g => g.ExternalId.Length > 0).ToList(), cursor, rows);
    }

    /// <summary>Die Monatsarchive ab (einschließlich) Jahr/Monat, aufsteigend — chess.com listet nur Monate mit Partien.</summary>
    public static List<string> ArchivesFrom(string json, int year, int month)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("archives", out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        var list = new List<(int Key, string Url)>();
        foreach (var a in arr.EnumerateArray())
        {
            var url = a.GetString() ?? "";
            var parts = url.TrimEnd('/').Split('/');
            if (parts.Length < 2 || !int.TryParse(parts[^2], out var y) || !int.TryParse(parts[^1], out var m)) continue;
            if (y * 100 + m >= year * 100 + month) list.Add((y * 100 + m, url));
        }
        return list.OrderBy(x => x.Key).Select(x => x.Url).ToList();
    }

    /// <summary>Ein chess.com-Monatsarchiv. Cursor = jüngstes <c>end_time</c> (ms) aller Partien, auch übersprungener.</summary>
    public static Page ParseChessCom(string json, string user)
    {
        var games = new List<Game>();
        long cursor = 0;
        var rows = 0;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("games", out var arr) || arr.ValueKind != JsonValueKind.Array) return new(games, 0, 0);
        foreach (var g in arr.EnumerateArray())
        {
            rows++;
            var end = g.TryGetProperty("end_time", out var et) && et.TryGetInt64(out var e) ? e * 1000 : 0;
            if (end > cursor) cursor = end;
            if (Str(g, "rules") != "chess") continue;
            var speed = Str(g, "time_class") switch
            {
                "bullet" => "bullet", "blitz" => "blitz", "rapid" => "rapid", "daily" => "correspondence", _ => null,
            };
            var pgn = Str(g, "pgn");
            if (speed is null || string.IsNullOrEmpty(pgn) || end == 0) continue;
            if (!g.TryGetProperty("white", out var w) || !g.TryGetProperty("black", out var b)) continue;
            bool white;
            if (string.Equals(Str(w, "username"), user, StringComparison.OrdinalIgnoreCase)) white = true;
            else if (string.Equals(Str(b, "username"), user, StringComparison.OrdinalIgnoreCase)) white = false;
            else continue;
            var parsed = PgnParser.SplitGames(pgn).FirstOrDefault();
            if (parsed.Headers is null || parsed.Headers.ContainsKey("FEN") || parsed.Headers.TryGetValue("SetUp", out var su) && su == "1") continue;
            var moves = PgnParser.ExtractMainlineSans(parsed.MoveText);
            if (moves.Count == 0) continue;
            var mine = Str(white ? w : b, "result");
            var result = mine == "win" ? (white ? "1-0" : "0-1")
                : mine is "agreed" or "repetition" or "stalemate" or "insufficient" or "50move" or "timevsinsufficient" ? "1/2-1/2"
                : white ? "0-1" : "1-0";
            var url = Str(g, "url") ?? Str(g, "uuid") ?? "";
            var id = url.TrimEnd('/').Split('/')[^1];
            games.Add(new Game(id, StartOf(parsed.Headers) ?? DateTimeOffset.FromUnixTimeMilliseconds(end).UtcDateTime, speed,
                g.TryGetProperty("rated", out var rated) && rated.ValueKind == JsonValueKind.True, white, result,
                Str(white ? b : w, "username"), Int(white ? b : w, "rating"), Int(white ? w : b, "rating"), moves));
        }
        return new Page(games.Where(x => x.ExternalId.Length > 0).ToList(), cursor, rows);
    }

    private static DateTime? StartOf(Dictionary<string, string> h) =>
        h.TryGetValue("UTCDate", out var d) && DateTime.TryParseExact(
            d + " " + (h.TryGetValue("UTCTime", out var t) ? t : "00:00:00"), "yyyy.MM.dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static string? PlayerId(JsonElement players, string side) =>
        players.TryGetProperty(side, out var p) && p.TryGetProperty("user", out var u) ? Str(u, "id") ?? Str(u, "name") : null;

    private static string? PlayerName(JsonElement players, string side) =>
        players.TryGetProperty(side, out var p) ? (p.TryGetProperty("user", out var u) ? Str(u, "name") : null) ?? (p.TryGetProperty("aiLevel", out var ai) ? $"Stockfish {ai}" : null) : null;

    private static int? Rating(JsonElement players, string side) =>
        players.TryGetProperty(side, out var p) ? Int(p, "rating") : null;

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    // ── Ein Durchgang über alle fälligen Konten ─────────────────────────────────────────────────

    /// <summary>Fällig: nie geholt, Rest vom letzten Mal (<c>SyncMore</c>) oder älter als <paramref name="interval"/>.
    /// Reihum, bis nichts mehr übrig ist oder <paramref name="budget"/> um ist. Drosselt eine Seite, endet der Durchgang.
    /// → noch Arbeit übrig?</summary>
    public async Task<bool> RunOnceAsync(TimeSpan interval, TimeSpan budget, CancellationToken ct)
    {
        var due = DateTime.UtcNow - interval;
        var ids = await _db.LeagueOnlineAccounts.AsNoTracking()
            .Where(a => a.SyncedAt == null || a.SyncMore || a.SyncedAt < due)
            .OrderBy(a => a.SyncedAt.HasValue).ThenBy(a => a.SyncedAt).Select(a => a.Id).ToListAsync(ct);
        var started = DateTime.UtcNow;
        var queue = new Queue<int>(ids);
        while (queue.Count > 0 && DateTime.UtcNow - started < budget)
        {
            var id = queue.Dequeue();
            var acc = await _db.LeagueOnlineAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (acc is null) continue;
            if (!LeagueOnlineSites.All.Contains(acc.Site)) continue;
            try
            {
                if (await SyncAccountAsync(acc, ct)) queue.Enqueue(id);
            }
            catch (RateLimitedException e)
            {
                _logger.LogWarning("LeagueHub: {Message} — Abruf der Online-Partien pausiert", e.Message);
                return true;
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return queue.Count > 0;
    }
}

/// <summary>
/// Takt des Abrufs: zwei Minuten nach dem Start, dann alle <c>LeagueOnline:IntervalHours</c> (Vorgabe 12) je Konto, sofort
/// nach einem Weckruf (neues/geändertes Konto) und in kurzen Abständen, solange ein Konto noch Rückstand hat. Abschaltbar mit
/// <c>LeagueOnline:Enabled=false</c>. Seit 0.607.0 läuft danach je Runde die Konto-Suche (<see cref="LeagueAccountFinder"/>,
/// abschaltbar mit <c>LeagueOnline:Suggestions=false</c>), seit 0.608.0 die Lichess-Übertragungen (<see cref="LeagueBroadcastImport"/>,
/// <c>LeagueBroadcasts:Enabled=false</c>).
/// </summary>
public sealed class LeagueOnlineSyncScheduler(IServiceScopeFactory scopes, LeagueOnlineSyncSignal signal, IConfiguration config,
    ILogger<LeagueOnlineSyncScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BacklogPause = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(30);
    /// <summary>Die Konto-Suche je Runde (sie fragt je Spieler rund zehnmal nach, mit Pausen).</summary>
    public static readonly TimeSpan SearchBudget = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("LeagueOnline:Enabled", true)) return;
        var suggestions = config.GetValue("LeagueOnline:Suggestions", true);
        var broadcasts = config.GetValue("LeagueBroadcasts:Enabled", true);
        var teamScout = config.GetValue("LeagueOnline:TeamScout", true);
        DateTime? lastDiscovery = null, lastPool = null;
        var interval = TimeSpan.FromHours(Math.Clamp(config.GetValue("LeagueOnline:IntervalHours", 12), 1, 168));
        try { await Task.Delay(StartDelay, ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            var more = false;
            try
            {
                using var scope = scopes.CreateScope();
                more = await scope.ServiceProvider.GetRequiredService<LeagueOnlineSync>().RunOnceAsync(interval, Budget, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                logger.LogError(e, "LeagueHub: Abruf der Online-Partien gescheitert");
            }
            // Lichess-Übertragungen (0.608.0): suchen höchstens alle DiscoverEvery, einspielen je Runde höchstens SearchBudget.
            if (broadcasts)
            {
                try
                {
                    var discover = lastDiscovery is null || DateTime.UtcNow - lastDiscovery > LeagueBroadcastImport.DiscoverEvery;
                    using var scope = scopes.CreateScope();
                    more |= await scope.ServiceProvider.GetRequiredService<LeagueBroadcastImport>().RunOnceAsync(SearchBudget, discover, ct);
                    if (discover) lastDiscovery = DateTime.UtcNow;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception e)
                {
                    logger.LogError(e, "LeagueHub: Lichess-Übertragungen gescheitert");
                }
            }
            // Danach die Konto-Suche (0.607.0): abwechselnd mit dem Abruf, je Runde höchstens SearchBudget.
            if (suggestions)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    more |= await scope.ServiceProvider.GetRequiredService<LeagueAccountFinder>().RunOnceAsync(SearchBudget, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception e)
                {
                    logger.LogError(e, "LeagueHub: Konto-Suche gescheitert");
                }
            }
            // Tiroler Lichess-Teams und ihre Team-Battles (0.612.0): Bestand höchstens alle PoolEvery neu, dann Konten prüfen.
            if (teamScout)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var scout = scope.ServiceProvider.GetRequiredService<LeagueTeamScout>();
                    if (lastPool is null || DateTime.UtcNow - lastPool > LeagueTeamScout.PoolEvery)
                    {
                        await scout.RefreshPoolAsync(ct);
                        lastPool = DateTime.UtcNow;
                    }
                    more |= await scout.RunOnceAsync(SearchBudget, refreshPool: false, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (LeagueOnlineSync.RateLimitedException e)
                {
                    logger.LogWarning("LeagueHub: {Message} — Team-Suche pausiert", e.Message);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "LeagueHub: Team-Suche gescheitert");
                }
            }
            try { await signal.WaitAsync(more ? BacklogPause : IdlePoll, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
