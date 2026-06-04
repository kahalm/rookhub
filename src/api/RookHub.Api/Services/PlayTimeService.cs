using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Erfasst die externe Spielzeit eines Users von Lichess und chess.com (öffentliche APIs,
/// kein Login) und schreibt sie tagesweise in <see cref="PlayTimeDaily"/> — Datenquelle für
/// die Kategorie „Spielen" im Trainingsziele-Tracker. Ein <see cref="PlayTimeSync"/>-Cursor je
/// User/Plattform sorgt dafür, dass nur neue Partien gezählt werden (kein Doppelzählen).
///
/// Genauigkeit: Lichess liefert <c>createdAt</c>/<c>lastMoveAt</c> je Partie → exakte Dauer.
/// chess.com nur Monatsarchive → Dauer wird aus den PGN-Headern (UTCDate/UTCTime ↔ EndDate/
/// EndTime) geschätzt (Best-Effort). Korrespondenz-/Daily-Partien werden ausgeschlossen,
/// Einzelpartien gegen Ausreißer gedeckelt.
///
/// Konfiguration (appsettings/env): <c>PlayTime:PerGameCapSeconds</c> (Default 1800),
/// <c>PlayTime:FirstSyncLookbackDays</c> (Default 30).
/// </summary>
public class PlayTimeService
{
    public const string Lichess = "lichess";
    public const string ChessCom = "chesscom";

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly ILogger<PlayTimeService> _logger;
    private readonly int _perGameCap;
    private readonly int _firstSyncLookbackDays;

    public PlayTimeService(HttpClient http, AppDbContext db, IConfiguration config, ILogger<PlayTimeService> logger)
    {
        _http = http;
        _db = db;
        _logger = logger;
        _perGameCap = config.GetValue<int?>("PlayTime:PerGameCapSeconds") ?? 1800;
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
            var (perDay, newCursor) = platform == Lichess
                ? await FetchLichessAsync(username, cursor, ct)
                : await FetchChessComAsync(username, cursor, ct);

            await UpsertDailyAsync(userId, platform, perDay, ct);

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

    /// <summary>Addiert die je-Tag-Sekunden auf die bestehenden Zeilen (Cursor verhindert Doppelzählen).</summary>
    private async Task UpsertDailyAsync(int userId, string platform, Dictionary<DateOnly, int> perDay, CancellationToken ct)
    {
        if (perDay.Count == 0) return;
        var dates = perDay.Keys.ToList();
        var existing = await _db.PlayTimeDailies
            .Where(p => p.UserId == userId && p.Platform == platform && dates.Contains(p.Date))
            .ToListAsync(ct);
        var byDate = existing.ToDictionary(p => p.Date);
        var now = DateTime.UtcNow;
        foreach (var (date, seconds) in perDay)
        {
            if (byDate.TryGetValue(date, out var row)) { row.Seconds += seconds; row.UpdatedAt = now; }
            else _db.PlayTimeDailies.Add(new PlayTimeDaily { UserId = userId, Platform = platform, Date = date, Seconds = seconds, UpdatedAt = now });
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
        return ParseLichess(body, _perGameCap);
    }

    /// <summary>Parst Lichess-NDJSON zu Sekunden je UTC-Tag + neuem Cursor (max lastMoveAt). Rein/testbar.</summary>
    public static (Dictionary<DateOnly, int> perDay, long newCursor) ParseLichess(string ndjson, int perGameCap)
    {
        var perDay = new Dictionary<DateOnly, int>();
        long newCursor = 0;
        foreach (var rawLine in ndjson.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("createdAt", out var ca) || !root.TryGetProperty("lastMoveAt", out var la)) continue;
            var createdMs = ca.GetInt64();
            var lastMs = la.GetInt64();
            // Cursor verfolgt ALLE Partien (auch Korrespondenz), damit sie nicht erneut geladen werden.
            if (lastMs > newCursor) newCursor = lastMs;
            if (root.TryGetProperty("speed", out var sp) && sp.GetString() == "correspondence") continue;
            var seconds = (int)Math.Clamp((lastMs - createdMs) / 1000, 0, perGameCap);
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(createdMs).UtcDateTime);
            if (seconds > 0) perDay[date] = (perDay.TryGetValue(date, out var v) ? v : 0) + seconds;
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
            var (md, mc) = ParseChessCom(body, cursor, perGameCap: _perGameCap);
            foreach (var kv in md) perDay[kv.Key] = (perDay.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            if (mc > newCursor) newCursor = mc;
        }
        return (perDay, newCursor);
    }

    /// <summary>Parst ein chess.com-Monatsarchiv zu Sekunden je UTC-Tag (Best-Effort aus PGN-Headern) + Cursor (max end_time·1000). Rein/testbar.</summary>
    public static (Dictionary<DateOnly, int> perDay, long newCursor) ParseChessCom(string json, long cursor, int perGameCap)
    {
        var perDay = new Dictionary<DateOnly, int>();
        long newCursor = cursor;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            return (perDay, newCursor);

        foreach (var g in games.EnumerateArray())
        {
            if (g.TryGetProperty("time_class", out var tc) && tc.GetString() == "daily") continue; // Korrespondenz
            if (!g.TryGetProperty("end_time", out var et)) continue;
            var endMs = et.GetInt64() * 1000;
            if (endMs <= cursor) continue;               // bereits gezählt
            if (endMs > newCursor) newCursor = endMs;

            var pgn = g.TryGetProperty("pgn", out var p) ? p.GetString() : null;
            var (date, seconds) = EstimateChessComDuration(pgn, et.GetInt64(), perGameCap);
            if (seconds > 0) perDay[date] = (perDay.TryGetValue(date, out var v) ? v : 0) + seconds;
        }
        return (perDay, newCursor);
    }

    private static (DateOnly date, int seconds) EstimateChessComDuration(string? pgn, long endSec, int perGameCap)
    {
        var endDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(endSec).UtcDateTime);
        if (string.IsNullOrEmpty(pgn)) return (endDate, 0);

        var start = ParsePgnDateTime(PgnHeader(pgn, "UTCDate"), PgnHeader(pgn, "UTCTime"));
        var end = ParsePgnDateTime(PgnHeader(pgn, "EndDate") ?? PgnHeader(pgn, "UTCDate"), PgnHeader(pgn, "EndTime"));
        var date = start.HasValue ? DateOnly.FromDateTime(start.Value) : endDate;
        if (start.HasValue && end.HasValue)
            return (date, (int)Math.Clamp((end.Value - start.Value).TotalSeconds, 0, perGameCap));
        return (date, 0);
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
