using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Tagespuzzle-Bestenlisten (Monats-Ladder + all-time Hall of Fame), aus <see cref="BookPuzzleService"/>
/// ausgegliedert. Wertet je (Tag, eingeloggtem User) den ersten Versuch am Tagespuzzle aus (dieselbe
/// Fairness-Regel wie die Solver-Liste), rankt die Löser pro Tag nach Zeit und summiert Punkte/🥇.
/// Rein lesend auf <see cref="AppDbContext"/>.
/// </summary>
public class DailyLeaderboardService
{
    private readonly AppDbContext _db;

    public DailyLeaderboardService(AppDbContext db) => _db = db;

    /// <summary>Eine gewertete Erstversuch-Lösung an einem Tagespuzzle (Rohzeile fürs Ranking).</summary>
    private sealed record DailyScoreRow(DateOnly Date, int UserId, int TimeSeconds, DateTime FirstAt);

    /// <summary>Tages-Rang-Bonus nach Erstversuch-Zeit: 🥇 +5 / 🥈 +3 / 🥉 +1, sonst 0.</summary>
    private static int RankBonus(int rank) => rank switch { 1 => 5, 2 => 3, 3 => 1, _ => 0 };

    /// <summary>
    /// Lädt für alle Tagespuzzles im optionalen Datumsfenster [<paramref name="from"/>,
    /// <paramref name="to"/>] je (Tag, eingeloggtem User) den ERSTEN Versuch AM UTC-TAG des
    /// Tagespuzzles und behält nur die gelösten. Der Tages-Fence ist Teil der Fairness-Regel:
    /// ohne ihn entschied der erste Versuch ÜBERHAUPT (auch Monate vor/nach dem Daily-Tag, z. B.
    /// beim Buch-Browsen über einen geteilten Link) über Credit, Zeit und 🥇 — ein Alt-Fail nahm
    /// einem echten Tages-Löser die Wertung, und Buch-Löser von früher bekamen Daily-Punkte.
    /// Ranking/Punkte berechnet der Aufrufer in-memory (kleine Datenmengen: ein Puzzle pro Tag).
    /// </summary>
    private async Task<List<DailyScoreRow>> LoadDailyFirstAttemptsAsync(DateOnly? from, DateOnly? to)
    {
        var dailyQ = _db.DailyPuzzles.AsQueryable();
        if (from.HasValue) dailyQ = dailyQ.Where(d => d.Date >= from.Value);
        if (to.HasValue) dailyQ = dailyQ.Where(d => d.Date <= to.Value);
        var dailies = await dailyQ.Select(d => new { d.Date, d.BookPuzzleId }).ToListAsync();
        if (dailies.Count == 0) return new();

        var puzzleIds = dailies.Select(d => d.BookPuzzleId).Distinct().ToList();

        // Versuche roh laden (nur Schlüsselfelder), grob aufs Gesamtfenster der Tage begrenzt;
        // der exakte Pro-Tag-Fence passiert in-memory (das Fenster variiert je Puzzle → nicht
        // sinnvoll provider-sicher in EINEM GroupBy ausdrückbar). Mengen bleiben klein
        // (ein Puzzle pro Tag, wenige Löser pro Tag).
        var winFrom = dailies.Min(d => d.Date).ToDateTime(TimeOnly.MinValue);
        var winTo = dailies.Max(d => d.Date).AddDays(1).ToDateTime(TimeOnly.MinValue);
        var attempts = await _db.BookPuzzleAttempts
            .Where(a => a.UserId != null && puzzleIds.Contains(a.BookPuzzleId)
                     && a.AttemptedAt >= winFrom && a.AttemptedAt < winTo)
            .Select(a => new { a.BookPuzzleId, UserId = a.UserId!.Value, a.Solved, a.TimeSeconds, a.AttemptedAt })
            .ToListAsync();

        var byPuzzle = attempts.GroupBy(a => a.BookPuzzleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<DailyScoreRow>();
        foreach (var d in dailies)
        {
            if (!byPuzzle.TryGetValue(d.BookPuzzleId, out var all)) continue;
            // Erster Versuch je User AM Daily-Tag; nur gelöste werten (Fairness wie Solver-Liste).
            var solvers = all
                .Where(a => DateOnly.FromDateTime(a.AttemptedAt) == d.Date)
                .GroupBy(a => a.UserId)
                .Select(g => g.OrderBy(a => a.AttemptedAt).First())
                .Where(a => a.Solved);
            foreach (var s in solvers)
                rows.Add(new DailyScoreRow(d.Date, s.UserId, s.TimeSeconds, s.AttemptedAt));
        }
        return rows;
    }

    /// <summary>
    /// Aggregiert die Erstversuch-Zeilen je User: pro Tag werden die Löser nach Zeit gerankt
    /// (Gleichstand → früherer Versuch zuerst), daraus Punkte (10 + Rang-Bonus), Lösungs- und
    /// 🥇-Zähler summiert.
    /// </summary>
    private static Dictionary<int, (int points, int solved, int golds)> AggregateScores(List<DailyScoreRow> rows)
    {
        var acc = new Dictionary<int, (int points, int solved, int golds)>();
        foreach (var day in rows.GroupBy(r => r.Date))
        {
            var ranked = day.OrderBy(r => r.TimeSeconds).ThenBy(r => r.FirstAt).ToList();
            foreach (var r in ranked)
            {
                // Competition-Ranking: Rang = 1 + Anzahl strikt SCHNELLERER Löser. Zeitgleiche Löser
                // bekommen denselben Rang/Bonus — und alle mit der Bestzeit zählen als 🥇 (statt dass
                // Submit-Reihenfolge/Mikrosekunden über Gold vs. Silber entscheiden, insb. bei TimeSeconds==0).
                var rank = 1 + ranked.Count(o => o.TimeSeconds < r.TimeSeconds);
                acc.TryGetValue(r.UserId, out var cur);
                acc[r.UserId] = (cur.points + 10 + RankBonus(rank), cur.solved + 1, cur.golds + (rank == 1 ? 1 : 0));
            }
        }
        return acc;
    }

    /// <summary>Namen + Discord-Profile der gegebenen User laden (für die Leaderboard-Anzeige).</summary>
    private async Task<(Dictionary<int, string> names, Dictionary<int, UserProfile> profiles)> ResolveUsersAsync(List<int> userIds)
    {
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username);
        var profiles = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);
        return (names, profiles);
    }

    private static (string name, string? discordId, string? discordUsername) ResolveIdentity(
        int userId, Dictionary<int, string> names, Dictionary<int, UserProfile> profiles)
    {
        profiles.TryGetValue(userId, out var prof);
        names.TryGetValue(userId, out var uname);
        return (prof?.DisplayName ?? uname ?? $"#{userId}", prof?.DiscordId, prof?.DiscordUsername);
    }

    /// <summary>
    /// Monats-Wertung des Tagespuzzles für <paramref name="year"/>/<paramref name="month"/>
    /// (1–12). Absteigend nach Punkten, dann gelösten Puzzles, dann Name.
    /// </summary>
    public async Task<DailyLadderDto> GetDailyLadderAsync(int year, int month)
    {
        if (month < 1 || month > 12)
            throw new InvalidOperationException("month must be between 1 and 12.");

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = await LoadDailyFirstAttemptsAsync(from, to);
        var perUser = AggregateScores(rows);
        var (names, profiles) = await ResolveUsersAsync(perUser.Keys.ToList());

        var entries = perUser
            .Select(kv =>
            {
                var (name, did, duser) = ResolveIdentity(kv.Key, names, profiles);
                return new DailyLadderEntryDto
                {
                    Name = name,
                    DiscordId = did,
                    DiscordUsername = duser,
                    Points = kv.Value.points,
                    Solved = kv.Value.solved,
                    Golds = kv.Value.golds
                };
            })
            .OrderByDescending(e => e.Points).ThenByDescending(e => e.Solved).ThenBy(e => e.Name)
            .ToList();

        return new DailyLadderDto { Period = $"{year:D4}-{month:D2}", Entries = entries };
    }

    /// <summary>
    /// All-time Hall of Fame des Tagespuzzles: meiste gelöste Dailies, meiste 🥇 (Tage als
    /// schnellster Erstversuch-Löser) und die schnellste je gelöste Lösung. Jede Liste auf
    /// <paramref name="top"/> Einträge begrenzt.
    /// </summary>
    public async Task<DailyHallOfFameDto> GetDailyHallOfFameAsync(int top = 5)
    {
        var rows = await LoadDailyFirstAttemptsAsync(null, null);
        var perUser = AggregateScores(rows);
        var (names, profiles) = await ResolveUsersAsync(perUser.Keys.ToList());

        List<HallOfFameEntryDto> RankBy(Func<(int points, int solved, int golds), int> pick) => perUser
            .Select(kv =>
            {
                var (name, did, duser) = ResolveIdentity(kv.Key, names, profiles);
                return new HallOfFameEntryDto { Name = name, DiscordId = did, DiscordUsername = duser, Value = pick(kv.Value) };
            })
            .Where(e => e.Value > 0)
            .OrderByDescending(e => e.Value).ThenBy(e => e.Name)
            .Take(top)
            .ToList();

        FastestSolveDto? fastest = null;
        var best = rows.Where(r => r.TimeSeconds > 0)
            .OrderBy(r => r.TimeSeconds).ThenBy(r => r.FirstAt).FirstOrDefault();
        if (best != null)
        {
            var (name, did, duser) = ResolveIdentity(best.UserId, names, profiles);
            fastest = new FastestSolveDto
            {
                Name = name,
                DiscordId = did,
                DiscordUsername = duser,
                TimeSeconds = best.TimeSeconds,
                Date = best.Date.ToString("yyyy-MM-dd")
            };
        }

        return new DailyHallOfFameDto
        {
            MostSolved = RankBy(v => v.solved),
            MostGolds = RankBy(v => v.golds),
            Fastest = fastest
        };
    }
}
