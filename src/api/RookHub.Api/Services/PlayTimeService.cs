using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Zählt die von einem User auf Lichess und chess.com gespielten Rapid-/Classical-Partien
/// (öffentliche APIs, kein Login) und schreibt die Anzahl tagesweise in <see cref="PlayTimeDaily"/>
/// — Datenquelle für das wöchentliche Spielen-Ziel im Trainingsziele-Tracker. Ein
/// <see cref="PlayTimeSync"/>-Cursor je User/Plattform sorgt dafür, dass nur neue Partien gezählt
/// werden (kein Doppelzählen).
///
/// Was zählt: Lichess <c>speed</c> = <c>rapid</c> oder <c>classical</c>; chess.com <c>time_class</c>
/// = <c>rapid</c> (chess.com kennt keine eigene „classical"-Live-Klasse). Bullet/Blitz/Korrespondenz
/// zählen nicht. Tageszuordnung: Lichess über <c>createdAt</c>, chess.com über den PGN-Header UTCDate
/// (Fallback: <c>end_time</c>).
///
/// Konfiguration (appsettings/env): <c>PlayTime:FirstSyncLookbackDays</c> (Default 30).
/// </summary>
public class PlayTimeService
{
    public const string Lichess = "lichess";
    public const string ChessCom = "chesscom";

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly ILogger<PlayTimeService> _logger;
    private readonly int _firstSyncLookbackDays;

    public PlayTimeService(HttpClient http, AppDbContext db, IConfiguration config, ILogger<PlayTimeService> logger)
    {
        _http = http;
        _db = db;
        _logger = logger;
        _firstSyncLookbackDays = config.GetValue<int?>("PlayTime:FirstSyncLookbackDays") ?? 30;
    }

    /// <summary>Synchronisiert beide Plattformen des Users, sofern der jeweilige Benutzername gesetzt ist.</summary>
    public async Task SyncUserAsync(int userId, CancellationToken ct = default)
    {
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile == null) return;

        if (!string.IsNullOrWhiteSpace(profile.LichessUsername))
            await SyncPlatformAsync(userId, Lichess, profile.LichessUsername!.Trim(), ct);
        if (!string.IsNullOrWhiteSpace(profile.ChessComUsername))
            await SyncPlatformAsync(userId, ChessCom, profile.ChessComUsername!.Trim(), ct);
    }

    private async Task SyncPlatformAsync(int userId, string platform, string username, CancellationToken ct)
    {
        var sync = await _db.PlayTimeSyncs.FirstOrDefaultAsync(s => s.UserId == userId && s.Platform == platform, ct);
        var cursor = sync?.LastGameTimestamp ?? 0;
        try
        {
            var (gamesPerDay, newCursor) = platform == Lichess
                ? await FetchLichessAsync(username, cursor, ct)
                : await FetchChessComAsync(username, cursor, ct);

            await UpsertDailyAsync(userId, platform, gamesPerDay, ct);

            sync ??= AddSync(userId, platform);
            if (newCursor > sync.LastGameTimestamp) sync.LastGameTimestamp = newCursor;
            sync.LastSyncedAt = DateTime.UtcNow;
            sync.LastError = null;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PlayTime-Sync fehlgeschlagen: user={UserId} platform={Platform}", userId, platform);
            sync ??= AddSync(userId, platform);
            sync.LastSyncedAt = DateTime.UtcNow;
            sync.LastError = Truncate(ex.Message, 500);
            try { await _db.SaveChangesAsync(ct); } catch { _db.ChangeTracker.Clear(); }
        }
    }

    private PlayTimeSync AddSync(int userId, string platform)
    {
        var s = new PlayTimeSync { UserId = userId, Platform = platform };
        _db.PlayTimeSyncs.Add(s);
        return s;
    }

    /// <summary>Addiert die je-Tag-Partienanzahl auf die bestehenden Zeilen (Cursor verhindert Doppelzählen).</summary>
    private async Task UpsertDailyAsync(int userId, string platform, Dictionary<DateOnly, int> gamesPerDay, CancellationToken ct)
    {
        if (gamesPerDay.Count == 0) return;
        var dates = gamesPerDay.Keys.ToList();
        var existing = await _db.PlayTimeDailies
            .Where(p => p.UserId == userId && p.Platform == platform && dates.Contains(p.Date))
            .ToListAsync(ct);
        var byDate = existing.ToDictionary(p => p.Date);
        var now = DateTime.UtcNow;
        foreach (var (date, games) in gamesPerDay)
        {
            if (byDate.TryGetValue(date, out var row)) { row.Games += games; row.UpdatedAt = now; }
            else _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = userId, Platform = platform, Date = date, Games = games, UpdatedAt = now });
        }
    }

    // ----- Lichess ---------------------------------------------------------

    private async Task<(Dictionary<DateOnly, int>, long)> FetchLichessAsync(string username, long cursor, CancellationToken ct)
    {
        var since = cursor > 0
            ? cursor + 1
            : DateTimeOffset.UtcNow.AddDays(-_firstSyncLookbackDays).ToUnixTimeMilliseconds();
        var url = $"https://lichess.org/api/games/user/{Uri.EscapeDataString(username)}" +
                  $"?since={since}&moves=false&clocks=false&evals=false&opening=false&pgnInJson=false&max=300";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/x-ndjson");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);
        return ParseLichess(body);
    }

    /// <summary>Parst Lichess-NDJSON zur Anzahl Rapid-/Classical-Partien je UTC-Tag (über createdAt)
    /// + neuem Cursor (max lastMoveAt über ALLE Partien). Rein/testbar.</summary>
    public static (Dictionary<DateOnly, int> perDay, long newCursor) ParseLichess(string ndjson)
    {
        var perDay = new Dictionary<DateOnly, int>();
        long newCursor = 0;
        foreach (var rawLine in ndjson.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("createdAt", out var ca)) continue;
            var createdMs = ca.GetInt64();
            var lastMs = root.TryGetProperty("lastMoveAt", out var la) ? la.GetInt64() : createdMs;
            // Cursor verfolgt ALLE Partien (auch Bullet/Blitz/Korrespondenz), damit sie nicht erneut geladen werden.
            if (lastMs > newCursor) newCursor = lastMs;
            var speed = root.TryGetProperty("speed", out var sp) ? sp.GetString() : null;
            if (speed != "rapid" && speed != "classical") continue;   // nur Rapid/Classical zählen
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(createdMs).UtcDateTime);
            perDay[date] = (perDay.TryGetValue(date, out var v) ? v : 0) + 1;
        }
        return (perDay, newCursor);
    }

    // ----- chess.com -------------------------------------------------------

    private async Task<(Dictionary<DateOnly, int>, long)> FetchChessComAsync(string username, long cursor, CancellationToken ct)
    {
        // Aktuelles + vorheriges Monatsarchiv (deckt Monatsgrenzen ab); per Cursor gefiltert.
        var now = DateTimeOffset.UtcNow;
        var prev = now.AddMonths(-1);
        var months = new[] { (prev.Year, prev.Month), (now.Year, now.Month) };
        var user = Uri.EscapeDataString(username.ToLowerInvariant());
        var perDay = new Dictionary<DateOnly, int>();
        long newCursor = cursor;
        foreach (var (y, m) in months)
        {
            using var resp = await _http.GetAsync($"https://api.chess.com/pub/player/{user}/games/{y}/{m:D2}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) continue; // kein Archiv für den Monat
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var (md, mc) = ParseChessCom(body, cursor);
            foreach (var kv in md) perDay[kv.Key] = (perDay.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            if (mc > newCursor) newCursor = mc;
        }
        return (perDay, newCursor);
    }

    /// <summary>Parst ein chess.com-Monatsarchiv zur Anzahl Rapid-Partien je UTC-Tag (Datum aus PGN-Header
    /// UTCDate, Fallback end_time) + Cursor (max end_time·1000 über ALLE Partien). Rein/testbar.</summary>
    public static (Dictionary<DateOnly, int> perDay, long newCursor) ParseChessCom(string json, long cursor)
    {
        var perDay = new Dictionary<DateOnly, int>();
        long newCursor = cursor;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            return (perDay, newCursor);

        foreach (var g in games.EnumerateArray())
        {
            if (!g.TryGetProperty("end_time", out var et)) continue;
            var endSec = et.GetInt64();
            var endMs = endSec * 1000;
            if (endMs <= cursor) continue;               // bereits gezählt
            if (endMs > newCursor) newCursor = endMs;     // Cursor über ALLE Partien

            // chess.com hat keine eigene „classical"-Live-Klasse → nur "rapid" zählt als Rapid/Classical.
            if (!(g.TryGetProperty("time_class", out var tc) && tc.GetString() == "rapid")) continue;
            var pgn = g.TryGetProperty("pgn", out var p) ? p.GetString() : null;
            var date = ChessComGameDate(pgn, endSec);
            perDay[date] = (perDay.TryGetValue(date, out var v) ? v : 0) + 1;
        }
        return (perDay, newCursor);
    }

    /// <summary>UTC-Tag einer chess.com-Partie: PGN-Header UTCDate/UTCTime, Fallback auf end_time.</summary>
    private static DateOnly ChessComGameDate(string? pgn, long endSec)
    {
        var endDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(endSec).UtcDateTime);
        if (string.IsNullOrEmpty(pgn)) return endDate;
        var start = ParsePgnDateTime(PgnHeader(pgn, "UTCDate"), PgnHeader(pgn, "UTCTime"));
        return start.HasValue ? DateOnly.FromDateTime(start.Value) : endDate;
    }

    private static string? PgnHeader(string pgn, string tag)
    {
        var m = Regex.Match(pgn, "\\[" + tag + " \"([^\"]*)\"\\]");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static DateTime? ParsePgnDateTime(string? date, string? time)
    {
        if (string.IsNullOrEmpty(date) || string.IsNullOrEmpty(time)) return null;
        return DateTime.TryParseExact($"{date} {time}", "yyyy.MM.dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
