using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Berechnet die Bestenlisten für drei Kategorien — einzigartige Standard-Puzzles,
/// abgeschlossene Endlos-Läufe und gelöste Kurs-Linien — je Periode
/// (daily/weekly/monthly/alltime, UTC-Grenzen). Nur eingeloggte Nutzer; anonyme
/// Versuche (UserId == null) zählen nicht, weil sie keine Identität für die Liste haben.
/// </summary>
public class LeaderboardService
{
    private readonly AppDbContext _db;
    public LeaderboardService(AppDbContext db) => _db = db;

    public static readonly string[] Periods = { "daily", "weekly", "monthly", "alltime" };

    /// <summary>Untere (inklusive) UTC-Zeitgrenze der Periode; alltime = MinValue (keine Grenze).</summary>
    public static DateTime WindowStart(string period, DateTime nowUtc)
    {
        var today = nowUtc.Date;   // 00:00 UTC heute
        return period switch
        {
            "daily" => today,
            // ISO-Woche: Montag als Wochenstart.
            "weekly" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            "monthly" => new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => DateTime.MinValue,   // alltime
        };
    }

    public async Task<LeaderboardsDto> GetAsync(string period, int top = 100)
    {
        if (!Periods.Contains(period)) period = "alltime";
        var from = WindowStart(period, DateTime.UtcNow);

        // #puzzles: einzigartige gelöste Standard-Puzzles je Nutzer.
        // Distinct-Paare (UserId, PuzzleId) ziehen (übersetzt zu SELECT DISTINCT — provider-sicher),
        // dann je Nutzer zählen. Vermeidet COUNT(DISTINCT …) im GroupBy.
        var puzzlePairs = await _db.PuzzleAttempts
            .Where(a => a.UserId != null && a.Solved && a.AttemptedAt >= from)
            .Select(a => new { UserId = a.UserId!.Value, a.PuzzleId })
            .Distinct()
            .ToListAsync();
        var puzzlePerUser = puzzlePairs
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.Count());

        // #endlessruns: abgeschlossene Endlos-Läufe je Nutzer (jede Session = ein Lauf).
        var endlessPerUser = (await _db.EndlessSessions
            .Where(s => s.UserId != null && s.CreatedAt >= from)
            .GroupBy(s => s.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.UserId, x => x.Count);

        // #lines from books: gelöste Kurs-Linien je Nutzer. CoursePuzzleResult ist idempotent
        // (eine Zeile je erstmalig gelöstem (UserId, BookPuzzleId)) → das IST die einzigartige Zählung.
        var linesPerUser = (await _db.CoursePuzzleResults
            .Where(r => r.SolvedAt >= from)
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.UserId, x => x.Count);

        // Identitäten (Name + Discord) für alle vorkommenden Nutzer einmal auflösen.
        var allIds = puzzlePerUser.Keys
            .Concat(endlessPerUser.Keys)
            .Concat(linesPerUser.Keys)
            .Distinct()
            .ToList();
        var (names, profiles) = await ResolveUsersAsync(allIds);

        return new LeaderboardsDto
        {
            Period = period,
            Puzzles = BuildEntries(puzzlePerUser, names, profiles, top),
            EndlessRuns = BuildEntries(endlessPerUser, names, profiles, top),
            CourseLines = BuildEntries(linesPerUser, names, profiles, top),
        };
    }

    private static List<LeaderboardEntryDto> BuildEntries(
        Dictionary<int, int> perUser,
        Dictionary<int, string> names,
        Dictionary<int, UserProfile> profiles,
        int top)
    {
        return perUser
            .Select(kv =>
            {
                profiles.TryGetValue(kv.Key, out var prof);
                names.TryGetValue(kv.Key, out var uname);
                return new LeaderboardEntryDto
                {
                    Name = prof?.DisplayName ?? uname ?? $"#{kv.Key}",
                    DiscordId = prof?.DiscordId,
                    DiscordUsername = prof?.DiscordUsername,
                    Count = kv.Value,
                };
            })
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Name)
            .Take(top)
            .ToList();
    }

    private async Task<(Dictionary<int, string> names, Dictionary<int, UserProfile> profiles)> ResolveUsersAsync(List<int> userIds)
    {
        if (userIds.Count == 0)
            return (new Dictionary<int, string>(), new Dictionary<int, UserProfile>());
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(u => u.Id, u => u.Username);
        var profiles = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);
        return (names, profiles);
    }
}
