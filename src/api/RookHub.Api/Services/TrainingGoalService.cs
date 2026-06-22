using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Trainingsziele („Trainingsunterstützung"): pro User ein effektives Tagesziel für Puzzles und
/// Buch/Kurs (Minuten), ein wöchentliches Spielen-Ziel (Anzahl Rapid-/Classical-Partien pro ISO-Woche)
/// sowie ein Wochenziel an voll erfüllten Tagen. Ein Tracker aggregiert die je Tag verbrachte Zeit
/// und markiert Tage als none/partial/full (Tagesstatus nur aus Puzzles + Buch).
///
/// Effektives Ziel = persönlicher <see cref="UserTrainingGoal"/>-Override, sonst die
/// <see cref="GroupTrainingGoal"/>-Vorlage einer Gruppe des Users. Bei Mitgliedschaft in mehreren
/// Gruppen mit Vorlage gewinnt die zuletzt aktualisierte (deterministische Regel).
///
/// Kategorien-Quellen:
///  • Puzzles = PuzzleAttempt (Standard) + EndlessSession (Endlos) + BookPuzzleAttempt (Tages-/Buch-Puzzle)
///             + CourseAttempt aus Büchern der Art Puzzle (alle Versuche, akkumuliert)
///  • Buch/Kurs = CourseAttempt aus Büchern der Art Study (Theorie-/Studienbücher; alle Versuche)
///  • Spielen = PlayTimeDaily (extern, Lichess/chess.com): gespielte Rapid-/Classical-Partien je Tag,
///    fürs Ziel über die laufende ISO-Woche summiert.
/// </summary>
public class TrainingGoalService
{
    private readonly AppDbContext _db;

    public TrainingGoalService(AppDbContext db) => _db = db;

    /// <summary>Obergrenze je Einzel-Puzzle gegen aufgeblähte Zeiten (z.B. Tab stundenlang offen).</summary>
    private const int PerPuzzleCapSeconds = 1800;   // 30 min
    /// <summary>Sanity-Obergrenze je Endlos-Session.</summary>
    private const int PerSessionCapSeconds = 14400; // 4 h
    /// <summary>Obergrenze je Chessable-Zeit-Häppchen (die Extension flusht in kleinen Intervallen).</summary>
    private const int PerChessableFlushCapSeconds = 3600; // 1 h
    private const int MaxTrackerWeeks = 53;

    // ----- Ziel-Auflösung --------------------------------------------------

    /// <summary>Effektives Ziel des Users: persönlich &gt; Gruppen-Vorlage (zuletzt aktualisierte) &gt; keins.</summary>
    public async Task<TrainingGoalDto> GetEffectiveGoalAsync(int userId)
    {
        var personal = await _db.UserTrainingGoals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId);
        if (personal != null)
            return Map(personal.PuzzleMinutes, personal.BookMinutes, personal.ChessableMinutes, personal.PlayGames,
                       personal.WeeklyDaysTarget, "personal", null);

        var tmpl = await _db.GroupTrainingGoals.AsNoTracking()
            .Where(g => _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == g.GroupId))
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new { g.PuzzleMinutes, g.BookMinutes, g.ChessableMinutes, g.PlayGames, g.WeeklyDaysTarget, GroupName = g.Group!.Name })
            .FirstOrDefaultAsync();
        if (tmpl != null)
            return Map(tmpl.PuzzleMinutes, tmpl.BookMinutes, tmpl.ChessableMinutes, tmpl.PlayGames,
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
        return Map(goal.PuzzleMinutes, goal.BookMinutes, goal.ChessableMinutes, goal.PlayGames, goal.WeeklyDaysTarget, "personal", null);
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
            : Map(g.PuzzleMinutes, g.BookMinutes, g.ChessableMinutes, g.PlayGames, g.WeeklyDaysTarget, "group", null);
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
        return Map(g.PuzzleMinutes, g.BookMinutes, g.ChessableMinutes, g.PlayGames, g.WeeklyDaysTarget, "group", null);
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

    // ----- Chessable-Aktivität (von der Extension) -------------------------

    /// <summary>Hängt ein Häppchen aktiver Chessable-Trainingszeit an (Zeitstempel serverseitig).
    /// Fließt über <see cref="AggregateAsync"/> in die Kategorie „Chessable" des Trackers.</summary>
    public async Task RecordChessableActivityAsync(int userId, ChessableActivityInputDto dto)
    {
        _db.ChessableActivities.Add(new ChessableActivity
        {
            UserId = userId,
            TimeSeconds = Math.Clamp(dto.SecondsActive, 0, PerChessableFlushCapSeconds),
            MovesTrained = Math.Max(0, dto.MovesTrained),
            AttemptedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    // ----- Manuelle Offline-Aktivitäten (selbst gemeldet) ------------------

    /// <summary>Eigene manuelle Einträge des Users (neueste zuerst), limitiert.</summary>
    public async Task<List<ManualActivityDto>> ListManualAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        return await _db.ManualActivities.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new ManualActivityDto
            {
                Id = m.Id,
                Date = m.Date.ToString("yyyy-MM-dd"),
                Kind = m.Kind,
                Amount = m.Amount,
                Note = m.Note,
            })
            .ToListAsync();
    }

    /// <summary>Legt einen manuellen Eintrag an. Wirft <see cref="ArgumentException"/> bei ungültigem Datum/Zukunft.</summary>
    public async Task<ManualActivityDto> AddManualAsync(int userId, ManualActivityInputDto dto)
    {
        var date = ParseManualDate(dto.Date);
        var entity = new ManualActivity
        {
            UserId = userId,
            Date = date,
            Kind = dto.Kind,
            Amount = ClampAmount(dto.Kind, dto.Amount),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.ManualActivities.Add(entity);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    /// <summary>Ändert einen eigenen manuellen Eintrag. Gibt null zurück, wenn er nicht existiert/nicht dem User gehört.</summary>
    public async Task<ManualActivityDto?> UpdateManualAsync(int userId, int id, ManualActivityInputDto dto)
    {
        var entity = await _db.ManualActivities.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (entity == null) return null;
        entity.Date = ParseManualDate(dto.Date);
        entity.Kind = dto.Kind;
        entity.Amount = ClampAmount(dto.Kind, dto.Amount);
        entity.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    /// <summary>Löscht einen eigenen manuellen Eintrag. true, wenn etwas gelöscht wurde.</summary>
    public async Task<bool> DeleteManualAsync(int userId, int id)
    {
        var entity = await _db.ManualActivities.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (entity == null) return false;
        _db.ManualActivities.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Parst yyyy-MM-dd und lehnt Zukunftsdaten (UTC) ab.</summary>
    private static DateOnly ParseManualDate(string raw)
    {
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date))
            throw new ArgumentException("Ungültiges Datum (erwartet yyyy-MM-dd).");
        if (date > DateOnly.FromDateTime(DateTime.UtcNow.Date))
            throw new ArgumentException("Datum darf nicht in der Zukunft liegen.");
        return date;
    }

    /// <summary>OTB-Partien: 1–<see cref="ManualGamesCap"/>; Minuten-Arten: 1–600.</summary>
    private static int ClampAmount(ManualActivityKind kind, int amount)
        => kind == ManualActivityKind.OtbGame
            ? Math.Clamp(amount, 1, ManualGamesCap)
            : Math.Clamp(amount, 1, 600);

    private static ManualActivityDto ToDto(ManualActivity m) => new()
    {
        Id = m.Id,
        Date = m.Date.ToString("yyyy-MM-dd"),
        Kind = m.Kind,
        Amount = m.Amount,
        Note = m.Note,
    };

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
                PuzzleSeconds = kv.Value.PuzzleSeconds,
                BookSeconds = kv.Value.BookSeconds,
                ChessableSeconds = kv.Value.ChessableSeconds,
                PlayGames = kv.Value.PlayGames,
                Status = DayStatus(kv.Value.PuzzleSeconds, kv.Value.BookSeconds, kv.Value.ChessableSeconds, goal),
                HasManual = kv.Value.HasManual,
            })
            .ToList();

        return new TrackerResponseDto { Goal = goal, Days = days };
    }

    /// <summary>Heutiger Fortschritt (Puzzles/Buch) + Wochenstand (Spielen-Partien + voll erfüllte Tage) der laufenden ISO-Woche.</summary>
    public async Task<TodayProgressDto> GetTodayAsync(int userId)
    {
        var goal = await GetEffectiveGoalAsync(userId);
        var today = DateTime.UtcNow.Date;
        var dow = ((int)today.DayOfWeek + 6) % 7; // 0 = Montag
        var weekStart = today.AddDays(-dow);

        var agg = await AggregateAsync(userId, weekStart);
        var todayKey = DateOnly.FromDateTime(today);
        var weekStartKey = DateOnly.FromDateTime(weekStart);
        var t = agg.TryGetValue(todayKey, out var tv) ? tv : default;

        // Spielen-Ziel ist wöchentlich: Partien der laufenden ISO-Woche (Mo–heute) summieren.
        var weekPlayGames = agg
            .Where(kv => kv.Key >= weekStartKey && kv.Key <= todayKey)
            .Sum(kv => kv.Value.PlayGames);

        var weekDaysMet = agg
            .Where(kv => kv.Key >= weekStartKey && kv.Key <= todayKey)
            .Count(kv => DayStatus(kv.Value.PuzzleSeconds, kv.Value.BookSeconds, kv.Value.ChessableSeconds, goal) == "full");

        return new TodayProgressDto
        {
            Goal = goal,
            Puzzles = Category(goal.PuzzleMinutes, t.PuzzleSeconds),
            Book = Category(goal.BookMinutes, t.BookSeconds),
            Chessable = Category(goal.ChessableMinutes, t.ChessableSeconds),
            Play = PlayCategory(goal.PlayGames, weekPlayGames),
            Status = DayStatus(t.PuzzleSeconds, t.BookSeconds, t.ChessableSeconds, goal),
            WeekDaysMet = weekDaysMet,
            WeeklyDaysTarget = goal.WeeklyDaysTarget,
        };
    }

    // ----- Aggregation -----------------------------------------------------

    private readonly record struct DayActivity(int PuzzleSeconds, int BookSeconds, int ChessableSeconds, int PlayGames, bool HasManual);

    /// <summary>Sanity-Obergrenze für manuell eingetragene OTB-Partien je Eintrag.</summary>
    private const int ManualGamesCap = 50;

    /// <summary>Summiert je UTC-Tag (ab <paramref name="windowStartUtc"/>) Sekunden für Puzzles/Buch/Chessable
    /// (Einzelversuche/Häppchen gegen Inflation gedeckelt) und die Anzahl Rapid-/Classical-Partien (Spielen).</summary>
    private async Task<Dictionary<DateOnly, DayActivity>> AggregateAsync(int userId, DateTime windowStartUtc)
    {
        var puzzle = new Dictionary<DateOnly, int>();
        var book = new Dictionary<DateOnly, int>();
        var chessable = new Dictionary<DateOnly, int>();
        var play = new Dictionary<DateOnly, int>();
        var manualDays = new HashSet<DateOnly>();

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

        // Kurs-Versuche (gelöst + fehlgeschlagen, akkumuliert): Routing nach Buch-Art —
        // Puzzle-Buch → Kategorie Puzzles, Studienbuch → Kategorie Buch/Kurs.
        var courseAttempts = await (
            from a in _db.CourseAttempts.AsNoTracking()
            where a.UserId == userId && a.AttemptedAt >= windowStartUtc
            join b in _db.Books.AsNoTracking() on a.BookId equals b.Id
            select new { a.AttemptedAt, a.TimeSeconds, b.Kind }).ToListAsync();
        foreach (var a in courseAttempts)
            Add(a.Kind == BookKind.Study ? book : puzzle, a.AttemptedAt, a.TimeSeconds, PerPuzzleCapSeconds);

        // Chessable: aktiv trainierte Zeit-Häppchen von der RepCheck-Extension.
        foreach (var a in await _db.ChessableActivities.AsNoTracking()
                     .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
                     .Select(a => new { a.AttemptedAt, a.TimeSeconds }).ToListAsync())
            Add(chessable, a.AttemptedAt, a.TimeSeconds, PerChessableFlushCapSeconds);

        // Spielen: externe Rapid-/Classical-Partien je Tag (Lichess/chess.com) — befüllt PlayTimeDaily.
        foreach (var p in await _db.PlayTimeDailies.AsNoTracking()
                     .Where(p => p.UserId == userId && p.Date >= DateOnly.FromDateTime(windowStartUtc.Date))
                     .Select(p => new { p.Date, p.Games }).ToListAsync())
            play[p.Date] = (play.TryGetValue(p.Date, out var v) ? v : 0) + Math.Max(0, p.Games);

        // Manuell eingetragene Offline-Aktivitäten: mappen je Art auf eine bestehende Kategorie.
        var windowStartDate = DateOnly.FromDateTime(windowStartUtc.Date);
        foreach (var m in await _db.ManualActivities.AsNoTracking()
                     .Where(m => m.UserId == userId && m.Date >= windowStartDate)
                     .Select(m => new { m.Date, m.Kind, m.Amount }).ToListAsync())
        {
            manualDays.Add(m.Date);
            switch (m.Kind)
            {
                case ManualActivityKind.OtbGame:
                    play[m.Date] = (play.TryGetValue(m.Date, out var g) ? g : 0) + Math.Clamp(m.Amount, 0, ManualGamesCap);
                    break;
                case ManualActivityKind.OfflinePuzzle:
                    Add(puzzle, m.Date.ToDateTime(TimeOnly.MinValue), m.Amount * 60, PerSessionCapSeconds);
                    break;
                case ManualActivityKind.OfflineStudy:
                case ManualActivityKind.Coaching:
                    Add(book, m.Date.ToDateTime(TimeOnly.MinValue), m.Amount * 60, PerSessionCapSeconds);
                    break;
            }
        }

        var keys = new HashSet<DateOnly>(puzzle.Keys);
        keys.UnionWith(book.Keys);
        keys.UnionWith(chessable.Keys);
        keys.UnionWith(play.Keys);
        keys.UnionWith(manualDays);
        return keys.ToDictionary(k => k, k => new DayActivity(
            puzzle.TryGetValue(k, out var pz) ? pz : 0,
            book.TryGetValue(k, out var bk) ? bk : 0,
            chessable.TryGetValue(k, out var ch) ? ch : 0,
            play.TryGetValue(k, out var pl) ? pl : 0,
            manualDays.Contains(k)));
    }

    // ----- Helfer ----------------------------------------------------------

    /// <summary>Tagesstatus aus den Tageszielen Puzzles + Buch + Chessable (Spielen ist ein Wochenziel, zählt hier nicht):
    /// "none" wenn keins erreicht, "full" wenn alle gesetzten erreicht, sonst "partial".</summary>
    internal static string DayStatus(int puzzleSec, int bookSec, int chessableSec, TrainingGoalDto goal)
    {
        int targets = 0, met = 0;
        if (goal.PuzzleMinutes > 0) { targets++; if (puzzleSec >= goal.PuzzleMinutes * 60) met++; }
        if (goal.BookMinutes > 0) { targets++; if (bookSec >= goal.BookMinutes * 60) met++; }
        if (goal.ChessableMinutes > 0) { targets++; if (chessableSec >= goal.ChessableMinutes * 60) met++; }
        if (targets == 0 || met == 0) return "none";
        return met == targets ? "full" : "partial";
    }

    private static CategoryProgressDto Category(int targetMinutes, int doneSeconds) => new()
    {
        TargetMinutes = targetMinutes,
        DoneSeconds = doneSeconds,
        Met = targetMinutes > 0 && doneSeconds >= targetMinutes * 60,
    };

    /// <summary>Fortschritt des wöchentlichen Spielen-Ziels (Partien dieser Woche vs. Zielanzahl).</summary>
    private static PlayProgressDto PlayCategory(int targetGames, int doneGames) => new()
    {
        TargetGames = targetGames,
        DoneGames = doneGames,
        Met = targetGames > 0 && doneGames >= targetGames,
    };

    private static void Apply(GroupTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.PuzzleMinutes = dto.PuzzleMinutes;
        g.BookMinutes = dto.BookMinutes;
        g.ChessableMinutes = dto.ChessableMinutes;
        g.PlayGames = dto.PlayGames;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static void Apply(UserTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.PuzzleMinutes = dto.PuzzleMinutes;
        g.BookMinutes = dto.BookMinutes;
        g.ChessableMinutes = dto.ChessableMinutes;
        g.PlayGames = dto.PlayGames;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static TrainingGoalDto Map(int puzzle, int book, int chessable, int playGames, int weekly, string source, string? groupName) => new()
    {
        PuzzleMinutes = puzzle,
        BookMinutes = book,
        ChessableMinutes = chessable,
        PlayGames = playGames,
        WeeklyDaysTarget = weekly,
        Source = source,
        GroupName = groupName,
    };
}
