using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Trainingsziele („Trainingsunterstützung"): pro User ein effektives Tagesziel je Kategorie
/// (Puzzles, Buch/Kurs, Spielen) + Wochenziel; ein Tracker aggregiert die je Tag verbrachte Zeit
/// und markiert Tage als none/partial/full.
///
/// Effektives Ziel = persönlicher <see cref="UserTrainingGoal"/>-Override, sonst die
/// <see cref="GroupTrainingGoal"/>-Vorlage einer Gruppe des Users. Bei Mitgliedschaft in mehreren
/// Gruppen mit Vorlage gewinnt die zuletzt aktualisierte (deterministische Regel).
///
/// Kategorien-Quellen:
///  • Puzzles = PuzzleAttempt (Standard) + EndlessSession (Endlos) + BookPuzzleAttempt (Tages-/Buch-Puzzle)
///  • Buch/Kurs = CoursePuzzleResult.TimeSeconds
///  • Spielen = PlayTimeDaily (extern, Lichess/chess.com — befüllt ab Phase C)
/// </summary>
public class TrainingGoalService
{
    private readonly AppDbContext _db;

    public TrainingGoalService(AppDbContext db) => _db = db;

    /// <summary>Obergrenze je Einzel-Puzzle gegen aufgeblähte Zeiten (z.B. Tab stundenlang offen).</summary>
    private const int PerPuzzleCapSeconds = 1800;   // 30 min
    /// <summary>Sanity-Obergrenze je Endlos-Session.</summary>
    private const int PerSessionCapSeconds = 14400; // 4 h
    private const int MaxTrackerWeeks = 53;

    // ----- Ziel-Auflösung --------------------------------------------------

    /// <summary>Effektives Ziel des Users: persönlich &gt; Gruppen-Vorlage (zuletzt aktualisierte) &gt; keins.</summary>
    public async Task<TrainingGoalDto> GetEffectiveGoalAsync(int userId)
    {
        var personal = await _db.UserTrainingGoals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId);
        if (personal != null)
            return Map(personal.PuzzleMinutes, personal.BookMinutes, personal.PlayMinutes,
                       personal.WeeklyDaysTarget, "personal", null);

        var tmpl = await _db.GroupTrainingGoals.AsNoTracking()
            .Where(g => _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == g.GroupId))
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new { g.PuzzleMinutes, g.BookMinutes, g.PlayMinutes, g.WeeklyDaysTarget, GroupName = g.Group!.Name })
            .FirstOrDefaultAsync();
        if (tmpl != null)
            return Map(tmpl.PuzzleMinutes, tmpl.BookMinutes, tmpl.PlayMinutes,
                       tmpl.WeeklyDaysTarget, "group", tmpl.GroupName);

        return new TrainingGoalDto { Source = "none" };
    }

    /// <summary>Setzt/aktualisiert den persönlichen Override des Users und gibt das neue effektive Ziel zurück.</summary>
    public async Task<TrainingGoalDto> SetPersonalGoalAsync(int userId, TrainingGoalInputDto dto)
    {
        var goal = await _db.UserTrainingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
        var now = DateTime.UtcNow;
        if (goal == null)
        {
            goal = new UserTrainingGoal { UserId = userId, CreatedAt = now };
            _db.UserTrainingGoals.Add(goal);
        }
        Apply(goal, dto);
        goal.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return Map(goal.PuzzleMinutes, goal.BookMinutes, goal.PlayMinutes, goal.WeeklyDaysTarget, "personal", null);
    }

    /// <summary>Entfernt den persönlichen Override → der User fällt auf die Gruppen-Vorlage (falls vorhanden) zurück.</summary>
    public async Task<TrainingGoalDto> DeletePersonalGoalAsync(int userId)
    {
        var goal = await _db.UserTrainingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
        if (goal != null)
        {
            _db.UserTrainingGoals.Remove(goal);
            await _db.SaveChangesAsync();
        }
        return await GetEffectiveGoalAsync(userId);
    }

    // ----- Gruppen-Vorlage (Admin) -----------------------------------------

    public async Task<TrainingGoalDto> GetGroupGoalAsync(int groupId)
    {
        var g = await _db.GroupTrainingGoals.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId);
        return g == null
            ? new TrainingGoalDto { Source = "none" }
            : Map(g.PuzzleMinutes, g.BookMinutes, g.PlayMinutes, g.WeeklyDaysTarget, "group", null);
    }

    public async Task<TrainingGoalDto> SetGroupGoalAsync(int groupId, TrainingGoalInputDto dto)
    {
        var g = await _db.GroupTrainingGoals.FirstOrDefaultAsync(x => x.GroupId == groupId);
        var now = DateTime.UtcNow;
        if (g == null)
        {
            g = new GroupTrainingGoal { GroupId = groupId, CreatedAt = now };
            _db.GroupTrainingGoals.Add(g);
        }
        Apply(g, dto);
        g.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return Map(g.PuzzleMinutes, g.BookMinutes, g.PlayMinutes, g.WeeklyDaysTarget, "group", null);
    }

    public async Task DeleteGroupGoalAsync(int groupId)
    {
        var g = await _db.GroupTrainingGoals.FirstOrDefaultAsync(x => x.GroupId == groupId);
        if (g != null)
        {
            _db.GroupTrainingGoals.Remove(g);
            await _db.SaveChangesAsync();
        }
    }

    // ----- Tracker / Heute -------------------------------------------------

    /// <summary>Tagesreihe (nur Tage mit Aktivität) der letzten <paramref name="weeks"/> Wochen + effektives Ziel.</summary>
    public async Task<TrackerResponseDto> GetTrackerAsync(int userId, int weeks)
    {
        weeks = Math.Clamp(weeks, 1, MaxTrackerWeeks);
        var goal = await GetEffectiveGoalAsync(userId);
        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-(weeks * 7 - 1));

        var agg = await AggregateAsync(userId, windowStart);
        var days = agg
            .OrderBy(kv => kv.Key)
            .Select(kv => new TrackerDayDto
            {
                Date = kv.Key.ToString("yyyy-MM-dd"),
                PuzzleSeconds = kv.Value.Puzzle,
                BookSeconds = kv.Value.Book,
                PlaySeconds = kv.Value.Play,
                Status = DayStatus(kv.Value.Puzzle, kv.Value.Book, kv.Value.Play, goal),
            })
            .ToList();

        return new TrackerResponseDto { Goal = goal, Days = days };
    }

    /// <summary>Heutiger Fortschritt je Kategorie + voll erfüllte Tage der laufenden ISO-Woche.</summary>
    public async Task<TodayProgressDto> GetTodayAsync(int userId)
    {
        var goal = await GetEffectiveGoalAsync(userId);
        var today = DateTime.UtcNow.Date;
        var dow = ((int)today.DayOfWeek + 6) % 7; // 0 = Montag
        var weekStart = today.AddDays(-dow);

        var agg = await AggregateAsync(userId, weekStart);
        var todayKey = DateOnly.FromDateTime(today);
        var t = agg.TryGetValue(todayKey, out var tv) ? tv : default;

        var weekDaysMet = agg
            .Where(kv => kv.Key >= DateOnly.FromDateTime(weekStart) && kv.Key <= todayKey)
            .Count(kv => DayStatus(kv.Value.Puzzle, kv.Value.Book, kv.Value.Play, goal) == "full");

        return new TodayProgressDto
        {
            Goal = goal,
            Puzzles = Category(goal.PuzzleMinutes, t.Puzzle),
            Book = Category(goal.BookMinutes, t.Book),
            Play = Category(goal.PlayMinutes, t.Play),
            Status = DayStatus(t.Puzzle, t.Book, t.Play, goal),
            WeekDaysMet = weekDaysMet,
            WeeklyDaysTarget = goal.WeeklyDaysTarget,
        };
    }

    // ----- Aggregation -----------------------------------------------------

    private readonly record struct DaySeconds(int Puzzle, int Book, int Play);

    /// <summary>Summiert je UTC-Tag (ab <paramref name="windowStartUtc"/>) die Sekunden je Kategorie.
    /// Einzelne Versuche werden gegen Inflation gedeckelt.</summary>
    private async Task<Dictionary<DateOnly, DaySeconds>> AggregateAsync(int userId, DateTime windowStartUtc)
    {
        var puzzle = new Dictionary<DateOnly, int>();
        var book = new Dictionary<DateOnly, int>();
        var play = new Dictionary<DateOnly, int>();

        static void Add(Dictionary<DateOnly, int> acc, DateTime when, int seconds, int cap)
        {
            var s = Math.Clamp(seconds, 0, cap);
            if (s <= 0) return;
            var key = DateOnly.FromDateTime(when.Date);
            acc[key] = (acc.TryGetValue(key, out var v) ? v : 0) + s;
        }

        // Puzzles: Standard-Puzzle-Versuche.
        foreach (var a in await _db.PuzzleAttempts.AsNoTracking()
                     .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
                     .Select(a => new { a.AttemptedAt, a.TimeSpentSeconds }).ToListAsync())
            Add(puzzle, a.AttemptedAt, a.TimeSpentSeconds, PerPuzzleCapSeconds);

        // Puzzles: Tages-/Buch-Puzzle-Versuche.
        foreach (var a in await _db.BookPuzzleAttempts.AsNoTracking()
                     .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
                     .Select(a => new { a.AttemptedAt, a.TimeSeconds }).ToListAsync())
            Add(puzzle, a.AttemptedAt, a.TimeSeconds, PerPuzzleCapSeconds);

        // Puzzles: Endlos-Sessions.
        foreach (var s in await _db.EndlessSessions.AsNoTracking()
                     .Where(s => s.UserId == userId && s.CreatedAt >= windowStartUtc)
                     .Select(s => new { s.CreatedAt, s.DurationSeconds }).ToListAsync())
            Add(puzzle, s.CreatedAt, s.DurationSeconds, PerSessionCapSeconds);

        // Buch/Kurs: gelöste Kurs-Puzzles mit erfasster Zeit.
        foreach (var r in await _db.CoursePuzzleResults.AsNoTracking()
                     .Where(r => r.UserId == userId && r.SolvedAt >= windowStartUtc)
                     .Select(r => new { r.SolvedAt, r.TimeSeconds }).ToListAsync())
            Add(book, r.SolvedAt, r.TimeSeconds, PerPuzzleCapSeconds);

        // Spielen: externe Spielzeit (Lichess/chess.com) — Phase C befüllt PlayTimeDaily.
        foreach (var p in await _db.PlayTimeDailies.AsNoTracking()
                     .Where(p => p.UserId == userId && p.Date >= DateOnly.FromDateTime(windowStartUtc.Date))
                     .Select(p => new { p.Date, p.Seconds }).ToListAsync())
            play[p.Date] = (play.TryGetValue(p.Date, out var v) ? v : 0) + Math.Max(0, p.Seconds);

        var keys = new HashSet<DateOnly>(puzzle.Keys);
        keys.UnionWith(book.Keys);
        keys.UnionWith(play.Keys);
        return keys.ToDictionary(k => k, k => new DaySeconds(
            puzzle.TryGetValue(k, out var pz) ? pz : 0,
            book.TryGetValue(k, out var bk) ? bk : 0,
            play.TryGetValue(k, out var pl) ? pl : 0));
    }

    // ----- Helfer ----------------------------------------------------------

    /// <summary>"none" wenn kein Ziel/keine Kategorie erreicht, "full" wenn alle Ziel-Kategorien erreicht, sonst "partial".</summary>
    internal static string DayStatus(int puzzleSec, int bookSec, int playSec, TrainingGoalDto goal)
    {
        int targets = 0, met = 0;
        if (goal.PuzzleMinutes > 0) { targets++; if (puzzleSec >= goal.PuzzleMinutes * 60) met++; }
        if (goal.BookMinutes > 0) { targets++; if (bookSec >= goal.BookMinutes * 60) met++; }
        if (goal.PlayMinutes > 0) { targets++; if (playSec >= goal.PlayMinutes * 60) met++; }
        if (targets == 0 || met == 0) return "none";
        return met == targets ? "full" : "partial";
    }

    private static CategoryProgressDto Category(int targetMinutes, int doneSeconds) => new()
    {
        TargetMinutes = targetMinutes,
        DoneSeconds = doneSeconds,
        Met = targetMinutes > 0 && doneSeconds >= targetMinutes * 60,
    };

    private static void Apply(GroupTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.PuzzleMinutes = dto.PuzzleMinutes;
        g.BookMinutes = dto.BookMinutes;
        g.PlayMinutes = dto.PlayMinutes;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static void Apply(UserTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.PuzzleMinutes = dto.PuzzleMinutes;
        g.BookMinutes = dto.BookMinutes;
        g.PlayMinutes = dto.PlayMinutes;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static TrainingGoalDto Map(int puzzle, int book, int play, int weekly, string source, string? groupName) => new()
    {
        PuzzleMinutes = puzzle,
        BookMinutes = book,
        PlayMinutes = play,
        WeeklyDaysTarget = weekly,
        Source = source,
        GroupName = groupName,
    };
}
