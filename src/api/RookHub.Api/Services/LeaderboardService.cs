using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Berechnet die Bestenlisten für vier Kategorien — einzigartige Standard-Puzzles
/// (inkl. gelöster Wochenpost-Puzzles), abgeschlossene Endlos-Läufe, gelöste Kurs-Linien
/// und einzigartige Tagespuzzles — je Periode
/// (weekly/monthly/alltime, UTC-Grenzen). Nur eingeloggte Nutzer; anonyme
/// Versuche (UserId == null) zählen nicht, weil sie keine Identität für die Liste haben.
/// </summary>
public class LeaderboardService
{
    private readonly AppDbContext _db;
    public LeaderboardService(AppDbContext db) => _db = db;

    public static readonly string[] Periods = { "weekly", "monthly", "alltime" };

    /// <summary>Untere (inklusive) UTC-Zeitgrenze der Periode; alltime = MinValue (keine Grenze).
    /// „weekly"/„monthly" sind ROLLIERENDE Fenster: die letzten 7 bzw. 31 Tage (taggenau, inkl. heute),
    /// NICHT Kalenderwoche/-monat.</summary>
    public static DateTime WindowStart(string period, DateTime nowUtc)
    {
        var today = nowUtc.Date;   // 00:00 UTC heute
        return period switch
        {
            // Letzte 7 Tage (heute + 6 vorherige).
            "weekly" => today.AddDays(-6),
            // Letzte 31 Tage (heute + 30 vorherige).
            "monthly" => today.AddDays(-30),
            _ => DateTime.MinValue,   // alltime
        };
    }

    public async Task<LeaderboardsDto> GetAsync(string period, int viewerId, int top = 5, int around = 2)
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

        // Wochenpost-Puzzles fließen in den allgemeinen Puzzle-Pool ein. WeeklyPostAttempt ist
        // idempotent je (WeeklyPostId, UserId, PuzzleIndex) → eine gelöste Zeile = ein einzigartig
        // gelöstes Wochenpost-Puzzle. Keine Überschneidung mit den Standard-Puzzles (andere Tabelle/
        // Identität), daher einfach je Nutzer auf die Standard-Zählung addieren.
        var weeklyPerUser = (await _db.WeeklyPostAttempts
            .Where(a => a.Solved && a.AttemptedAt >= from)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.UserId, x => x.Count);
        foreach (var kv in weeklyPerUser)
            puzzlePerUser[kv.Key] = puzzlePerUser.GetValueOrDefault(kv.Key) + kv.Value;

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

        // #daily puzzles: einzigartige gelöste Tagespuzzles je Nutzer. Daily = Buch-Puzzle, das
        // in DailyPuzzles einem Datum zugeordnet ist; gezählt wird ein gelöster BookPuzzleAttempt
        // (im Fenster nach Lösezeit), distinct je BookPuzzleId.
        var dailyIds = await _db.DailyPuzzles.Select(d => d.BookPuzzleId).Distinct().ToListAsync();
        var dailyPairs = await _db.BookPuzzleAttempts
            .Where(a => a.UserId != null && a.Solved && a.AttemptedAt >= from && dailyIds.Contains(a.BookPuzzleId))
            .Select(a => new { UserId = a.UserId!.Value, a.BookPuzzleId })
            .Distinct()
            .ToListAsync();
        var dailyPerUser = dailyPairs
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Identitäten (Name + Discord) für alle vorkommenden Nutzer einmal auflösen.
        var allIds = puzzlePerUser.Keys
            .Concat(endlessPerUser.Keys)
            .Concat(linesPerUser.Keys)
            .Concat(dailyPerUser.Keys)
            .Distinct()
            .ToList();
        var (names, profiles) = await ResolveUsersAsync(allIds);

        return new LeaderboardsDto
        {
            Period = period,
            Puzzles = BuildEntries(puzzlePerUser, names, profiles, viewerId, top, around),
            EndlessRuns = BuildEntries(endlessPerUser, names, profiles, viewerId, top, around),
            CourseLines = BuildEntries(linesPerUser, names, profiles, viewerId, top, around),
            DailyPuzzles = BuildEntries(dailyPerUser, names, profiles, viewerId, top, around),
        };
    }

    /// <summary>Baut die Kategorie als Top-<paramref name="top"/> PLUS dem Fenster ±<paramref name="around"/>
    /// um den Eintrag des Viewers. Jeder Eintrag trägt seinen ECHTEN Rang (1-basiert über die ganze
    /// Kategorie); die zurückgegebene Liste ist nach Rang sortiert und kann eine Lücke zwischen Top-Block
    /// und Viewer-Fenster haben. Steht der Viewer in den Top, gibt es einfach keine Lücke.</summary>
    private static List<LeaderboardEntryDto> BuildEntries(
        Dictionary<int, int> perUser,
        Dictionary<int, string> names,
        Dictionary<int, UserProfile> profiles,
        int viewerId,
        int top,
        int around)
    {
        // Vollständige, gerankte Reihenfolge (Count desc, dann Name) — Rang = Position+1.
        var ranked = perUser
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
                    IsMe = kv.Key == viewerId,
                };
            })
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Name)
            .ToList();
        for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;

        // Top-N immer; zusätzlich das ±-Fenster um den eigenen Platz (falls der Viewer gelistet ist).
        var meIdx = ranked.FindIndex(e => e.IsMe);
        return ranked
            .Where((e, i) => i < top || (meIdx >= 0 && Math.Abs(i - meIdx) <= around))
            .ToList(); // bereits nach Rang sortiert
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
