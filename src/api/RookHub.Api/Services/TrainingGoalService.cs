using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Trainingsziele („Trainingsunterstützung"): pro User <b>ein</b> effektives Tageszeit-Ziel
/// (<see cref="UserTrainingGoal.DailyMinutes"/>), das von allen Quellen gemeinsam gefüllt wird, plus
/// ein wöchentliches Spielen-Ziel (Anzahl Rapid-/Classical-Partien pro ISO-Woche) und ein Wochenziel
/// an voll erfüllten Tagen. Ein Tracker aggregiert die je Tag verbrachte Trainingszeit und markiert
/// Tage als none/partial/full (gegenüber dem Tageszeit-Ziel).
///
/// Effektives Ziel = persönlicher <see cref="UserTrainingGoal"/>-Override, sonst die
/// <see cref="GroupTrainingGoal"/>-Vorlage einer Gruppe des Users. Bei Mitgliedschaft in mehreren
/// Gruppen mit Vorlage gewinnt die zuletzt aktualisierte (deterministische Regel).
///
/// Die verbrachte Zeit wird zusätzlich nach <b>Quelle</b> und <b>Thema</b> aufgezeichnet:
///  • Quelle: randomPuzzle (Standard-/Endlos-/Tages-/Einzel-Puzzle + manuelles Offline-Puzzle),
///    courseBook (alle Kurs-Versuche + manuelles Offline-Studium/Coaching), chessable (RepCheck-Zeit).
///  • Thema: Eröffnung/Mittelspiel/Endspiel/Taktik aus dem jeweils verfügbaren Signal (Lichess-Themes,
///    BookPuzzle-Tags/Chapter, Chessable-CourseKind); nicht klassifizierbare Zeit → „other".
///
/// Spielen (PlayTimeDaily + manuelle OTB-Partien) bleibt eine reine Partienzahl und füttert den
/// Zeit-Topf nicht.
/// </summary>
public class TrainingGoalService
{
    private readonly AppDbContext _db;

    public TrainingGoalService(AppDbContext db) => _db = db;

    /// <summary>Obergrenze je Einzel-Puzzle gegen aufgeblähte Zeiten (z.B. Tab stundenlang offen).</summary>
    private const int PerPuzzleCapSeconds = 1800;   // 30 min
    /// <summary>Sanity-Obergrenze je Endlos-Session / manuellem Minuten-Eintrag.</summary>
    private const int PerSessionCapSeconds = 14400; // 4 h
    /// <summary>Obergrenze je Chessable-Zeit-Häppchen (die Extension flusht in kleinen Intervallen).</summary>
    private const int PerChessableFlushCapSeconds = 3600; // 1 h
    /// <summary>Sanity-Obergrenze für manuell eingetragene OTB-Partien je Eintrag.</summary>
    private const int ManualGamesCap = 50;
    private const int MaxTrackerWeeks = 53;

    // ----- Ziel-Auflösung --------------------------------------------------

    /// <summary>Effektives Ziel des Users: persönlich &gt; Gruppen-Vorlage (zuletzt aktualisierte) &gt; keins.</summary>
    public async Task<TrainingGoalDto> GetEffectiveGoalAsync(int userId)
    {
        var personal = await _db.UserTrainingGoals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId);
        if (personal != null)
            return Map(personal.DailyMinutes, personal.PlayGames, personal.WeeklyDaysTarget, "personal", null);

        var tmpl = await _db.GroupTrainingGoals.AsNoTracking()
            .Where(g => _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == g.GroupId))
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new { g.DailyMinutes, g.PlayGames, g.WeeklyDaysTarget, GroupName = g.Group!.Name })
            .FirstOrDefaultAsync();
        if (tmpl != null)
            return Map(tmpl.DailyMinutes, tmpl.PlayGames, tmpl.WeeklyDaysTarget, "group", tmpl.GroupName);

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
        return Map(goal.DailyMinutes, goal.PlayGames, goal.WeeklyDaysTarget, "personal", null);
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
            : Map(g.DailyMinutes, g.PlayGames, g.WeeklyDaysTarget, "group", null);
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
        return Map(g.DailyMinutes, g.PlayGames, g.WeeklyDaysTarget, "group", null);
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
    /// Fließt über <see cref="AggregateAsync"/> in die Quelle „chessable" des Trackers.</summary>
    public async Task RecordChessableActivityAsync(int userId, ChessableActivityInputDto dto)
    {
        var courseId = string.IsNullOrWhiteSpace(dto.CourseId) ? null : dto.CourseId.Trim();
        var courseName = string.IsNullOrWhiteSpace(dto.CourseName) ? null : dto.CourseName.Trim();
        _db.ChessableActivities.Add(new ChessableActivity
        {
            UserId = userId,
            TimeSeconds = Math.Clamp(dto.SecondsActive, 0, PerChessableFlushCapSeconds),
            MovesTrained = Math.Max(0, dto.MovesTrained),
            CourseKind = dto.CourseKind,
            CourseId = courseId?.Length > 32 ? courseId[..32] : courseId,
            CourseName = courseName?.Length > 200 ? courseName[..200] : courseName,
            AttemptedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    // ----- Chessable-Kurs-History + manuelle Themen-Zuordnung --------------

    /// <summary>Gruppiert die Chessable-Aktivitäten des Users nach Kurs-ID (nur Aktivitäten MIT Kurs-ID)
    /// und liefert pro Kurs Zeit/Züge + ermitteltes Thema (manuelle Zuordnung &gt; Repertoire-Auto &gt; keins).
    /// <paramref name="unassignedOnly"/> = nur Kurse ohne feststehendes Thema.</summary>
    public async Task<List<ChessableCourseSummaryDto>> GetChessableCoursesAsync(int userId, bool unassignedOnly = false)
    {
        var acts = await _db.ChessableActivities.AsNoTracking()
            .Where(a => a.UserId == userId && a.CourseId != null && a.CourseId != "")
            .Select(a => new { a.CourseId, a.CourseName, a.CourseKind, a.TimeSeconds, a.MovesTrained, a.AttemptedAt })
            .ToListAsync();

        var assignments = await _db.ChessableCourseThemes.AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToDictionaryAsync(t => t.CourseId, t => t.Theme);

        var summaries = acts
            .GroupBy(a => a.CourseId!)
            .Select(g =>
            {
                var latest = g.OrderByDescending(a => a.AttemptedAt).First();
                var autoKind = g.Select(a => a.CourseKind).FirstOrDefault(k => k != null);
                var hasManual = assignments.TryGetValue(g.Key, out var manual);
                return new ChessableCourseSummaryDto
                {
                    CourseId = g.Key,
                    CourseName = latest.CourseName,
                    TotalSeconds = g.Sum(a => Math.Min(a.TimeSeconds, PerChessableFlushCapSeconds)),
                    TotalMoves = g.Sum(a => a.MovesTrained),
                    ActivityCount = g.Count(),
                    LastActivityAt = latest.AttemptedAt,
                    AssignedTheme = hasManual ? ThemeName(manual) : null,
                    AutoTheme = autoKind?.ToString().ToLowerInvariant(),
                    IsAssigned = hasManual || autoKind != null,
                };
            })
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();

        return unassignedOnly ? summaries.Where(s => !s.IsAssigned).ToList() : summaries;
    }

    /// <summary>Setzt/aktualisiert die manuelle Themen-Zuordnung eines Chessable-Kurses (Upsert je User+Kurs).
    /// Übernimmt einen Kursnamen, wenn (noch) keiner gespeichert ist. Liefert false, wenn die Kurs-ID leer ist.</summary>
    public async Task<bool> SetChessableCourseThemeAsync(int userId, string courseId, ChessableTheme theme)
    {
        courseId = (courseId ?? string.Empty).Trim();
        if (courseId.Length == 0) return false;
        if (courseId.Length > 32) courseId = courseId[..32];

        var existing = await _db.ChessableCourseThemes
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CourseId == courseId);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            // Kursnamen aus der jüngsten Aktivität dieses Kurses übernehmen (best-effort).
            var name = await _db.ChessableActivities.AsNoTracking()
                .Where(a => a.UserId == userId && a.CourseId == courseId && a.CourseName != null)
                .OrderByDescending(a => a.AttemptedAt)
                .Select(a => a.CourseName)
                .FirstOrDefaultAsync();
            _db.ChessableCourseThemes.Add(new ChessableCourseTheme
            {
                UserId = userId, CourseId = courseId, CourseName = name,
                Theme = theme, CreatedAt = now, UpdatedAt = now,
            });
        }
        else
        {
            existing.Theme = theme;
            existing.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Entfernt die manuelle Themen-Zuordnung eines Kurses (Rückfall auf Auto/unzugeordnet).
    /// Liefert false, wenn keine Zuordnung existierte.</summary>
    public async Task<bool> ClearChessableCourseThemeAsync(int userId, string courseId)
    {
        courseId = (courseId ?? string.Empty).Trim();
        var existing = await _db.ChessableCourseThemes
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CourseId == courseId);
        if (existing == null) return false;
        _db.ChessableCourseThemes.Remove(existing);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string ThemeName(ChessableTheme t) => t switch
    {
        ChessableTheme.Opening => "opening",
        ChessableTheme.Middlegame => "middlegame",
        ChessableTheme.Endgame => "endgame",
        ChessableTheme.Tactics => "tactics",
        _ => "opening",
    };

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
                Theme = m.Theme,
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
            Theme = dto.Theme,
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
        entity.Theme = dto.Theme;
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
        Theme = m.Theme,
    };

    // ----- Tracker / Heute -------------------------------------------------

    /// <summary>Tagesreihe (nur Tage mit Aktivität) der letzten <paramref name="weeks"/> Wochen + effektives Ziel
    /// + Perioden-Aufschlüsselung nach Quelle und Thema.</summary>
    public async Task<TrackerResponseDto> GetTrackerAsync(int userId, int weeks)
    {
        weeks = Math.Clamp(weeks, 1, MaxTrackerWeeks);
        var goal = await GetEffectiveGoalAsync(userId);
        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-(weeks * 7 - 1));

        var agg = await AggregateAsync(userId, windowStart);

        var bySource = new int[3];
        var byTheme = new int[5];
        var days = agg
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                Accumulate(bySource, kv.Value.Source);
                Accumulate(byTheme, kv.Value.Theme);
                return new TrackerDayDto
                {
                    Date = kv.Key.ToString("yyyy-MM-dd"),
                    TotalSeconds = kv.Value.Total,
                    BySource = SourceDto(kv.Value.Source),
                    ByTheme = ThemeDto(kv.Value.Theme),
                    PlayGames = kv.Value.PlayGames,
                    Status = DayStatus(kv.Value.Total, goal.DailyMinutes),
                    HasManual = kv.Value.HasManual,
                };
            })
            .ToList();

        return new TrackerResponseDto
        {
            Goal = goal,
            Days = days,
            BreakdownBySource = SourceDto(bySource),
            BreakdownByTheme = ThemeDto(byTheme),
        };
    }

    /// <summary>Vollständige Tagesreihe (nur Tage mit Aktivität) über die gesamte Historie — ohne das
    /// 53-Wochen-Tracker-Fenster. Liefert je Tag die Aufschlüsselung nach Quelle und Thema, damit das
    /// Frontend die Perioden-Auswahl (Tag/Woche/Monat/Jahr/Gesamt) inklusive Durchschalten rein
    /// client-seitig berechnen kann.</summary>
    public async Task<DailySeriesDto> GetDailySeriesAsync(int userId)
    {
        var goal = await GetEffectiveGoalAsync(userId);
        var agg = await AggregateAsync(userId, DateTime.UnixEpoch);

        var days = agg
            .OrderBy(kv => kv.Key)
            .Select(kv => new TrackerDayDto
            {
                Date = kv.Key.ToString("yyyy-MM-dd"),
                TotalSeconds = kv.Value.Total,
                BySource = SourceDto(kv.Value.Source),
                ByTheme = ThemeDto(kv.Value.Theme),
                PlayGames = kv.Value.PlayGames,
                Status = DayStatus(kv.Value.Total, goal.DailyMinutes),
                HasManual = kv.Value.HasManual,
            })
            .ToList();

        return new DailySeriesDto { Days = days };
    }

    /// <summary>Heutiger Fortschritt (Tageszeit-Ziel + Aufschlüsselung) + Wochenstand
    /// (Spielen-Partien + voll erfüllte Tage) der laufenden ISO-Woche.</summary>
    public async Task<TodayProgressDto> GetTodayAsync(int userId)
    {
        var goal = await GetEffectiveGoalAsync(userId);
        var today = DateTime.UtcNow.Date;
        var dow = ((int)today.DayOfWeek + 6) % 7; // 0 = Montag
        var weekStart = today.AddDays(-dow);

        var agg = await AggregateAsync(userId, weekStart);
        var todayKey = DateOnly.FromDateTime(today);
        var weekStartKey = DateOnly.FromDateTime(weekStart);
        var t = agg.TryGetValue(todayKey, out var tv) ? tv : new DayBuckets();

        // Spielen-Ziel ist wöchentlich: Partien der laufenden ISO-Woche (Mo–heute) summieren.
        var weekPlayGames = agg
            .Where(kv => kv.Key >= weekStartKey && kv.Key <= todayKey)
            .Sum(kv => kv.Value.PlayGames);

        var weekDaysMet = agg
            .Where(kv => kv.Key >= weekStartKey && kv.Key <= todayKey)
            .Count(kv => DayStatus(kv.Value.Total, goal.DailyMinutes) == "full");

        return new TodayProgressDto
        {
            Goal = goal,
            Daily = Category(goal.DailyMinutes, t.Total),
            BySource = SourceDto(t.Source),
            ByTheme = ThemeDto(t.Theme),
            Play = PlayCategory(goal.PlayGames, weekPlayGames),
            Status = DayStatus(t.Total, goal.DailyMinutes),
            WeekDaysMet = weekDaysMet,
            WeeklyDaysTarget = goal.WeeklyDaysTarget,
        };
    }

    // ----- Aggregation -----------------------------------------------------

    /// <summary>Aufzeichnungs-Quelle einer Zeit-Scheibe (Index in <see cref="DayBuckets.Source"/>).</summary>
    private enum Src { RandomPuzzle = 0, CourseBook = 1, Chessable = 2 }
    /// <summary>Thema einer Zeit-Scheibe (Index in <see cref="DayBuckets.Theme"/>).</summary>
    private enum Thm { Opening = 0, Middlegame = 1, Endgame = 2, Tactics = 3, Other = 4 }

    /// <summary>Tages-Eimer: Gesamtsekunden + Sekunden je Quelle/Thema + Partien + Manuell-Marker.</summary>
    private sealed class DayBuckets
    {
        public int Total;
        public readonly int[] Source = new int[3];
        public readonly int[] Theme = new int[5];
        public int PlayGames;
        public bool HasManual;
    }

    /// <summary>Summiert je UTC-Tag (ab <paramref name="windowStartUtc"/>) die Trainingssekunden
    /// (gegen Inflation gedeckelt), aufgeschlüsselt nach Quelle und Thema, plus die Anzahl
    /// Rapid-/Classical-Partien (Spielen, separat) und den Manuell-Marker.</summary>
    private async Task<Dictionary<DateOnly, DayBuckets>> AggregateAsync(int userId, DateTime windowStartUtc)
    {
        var acc = new Dictionary<DateOnly, DayBuckets>();

        DayBuckets Day(DateOnly key)
        {
            if (!acc.TryGetValue(key, out var d)) { d = new DayBuckets(); acc[key] = d; }
            return d;
        }

        void AddTime(DateTime when, int seconds, int cap, Src src, Thm thm)
        {
            var s = Math.Clamp(seconds, 0, cap);
            if (s <= 0) return;
            var d = Day(DateOnly.FromDateTime(when.Date));
            d.Total += s;
            d.Source[(int)src] += s;
            d.Theme[(int)thm] += s;
        }

        // Quelle „randomPuzzle": Standard-Puzzle-Versuche — Thema aus Lichess-Themes (Phase, sonst Taktik).
        foreach (var a in await (
                     from a in _db.PuzzleAttempts.AsNoTracking()
                     where a.UserId == userId && a.AttemptedAt >= windowStartUtc
                     join p in _db.Puzzles.AsNoTracking() on a.PuzzleId equals p.Id
                     select new { a.AttemptedAt, a.TimeSpentSeconds, p.Themes }).ToListAsync())
            AddTime(a.AttemptedAt, a.TimeSpentSeconds, PerPuzzleCapSeconds, Src.RandomPuzzle,
                    PhaseFromText(a.Themes) ?? Thm.Tactics);

        // Quelle „randomPuzzle": Tages-/Einzel-Buch-Puzzle — Thema aus BookPuzzle-Tags/Chapter, sonst Taktik.
        foreach (var a in await (
                     from a in _db.BookPuzzleAttempts.AsNoTracking()
                     where a.UserId == userId && a.AttemptedAt >= windowStartUtc
                     join bp in _db.BookPuzzles.AsNoTracking() on a.BookPuzzleId equals bp.Id
                     select new { a.AttemptedAt, a.TimeSeconds, bp.Tags, bp.Chapter }).ToListAsync())
            AddTime(a.AttemptedAt, a.TimeSeconds, PerPuzzleCapSeconds, Src.RandomPuzzle,
                    PhaseFromText(a.Tags, a.Chapter) ?? Thm.Tactics);

        // Quelle „randomPuzzle": Endlos-Sessions — inhaltlich Taktik.
        foreach (var s in await _db.EndlessSessions.AsNoTracking()
                     .Where(s => s.UserId == userId && s.CreatedAt >= windowStartUtc)
                     .Select(s => new { s.CreatedAt, s.DurationSeconds }).ToListAsync())
            AddTime(s.CreatedAt, s.DurationSeconds, PerSessionCapSeconds, Src.RandomPuzzle, Thm.Tactics);

        // Quelle „randomPuzzle": Wochenpost-Puzzle-Versuche — der Kurator legt sie als Taktik-Set an,
        // daher fest als Taktik verbucht (kein Kind-/Tag-Routing). Cap wie Standard-/Buch-Puzzle.
        foreach (var a in await _db.WeeklyPostAttempts.AsNoTracking()
                     .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
                     .Select(a => new { a.AttemptedAt, a.TimeSeconds }).ToListAsync())
            AddTime(a.AttemptedAt, a.TimeSeconds, PerPuzzleCapSeconds, Src.RandomPuzzle, Thm.Tactics);

        // Quelle „courseBook": alle Kurs-Versuche — Thema aus Tags/Chapter; sonst Puzzle-Buch→Taktik, Studienbuch→other.
        foreach (var a in await (
                     from a in _db.CourseAttempts.AsNoTracking()
                     where a.UserId == userId && a.AttemptedAt >= windowStartUtc
                     join b in _db.Books.AsNoTracking() on a.BookId equals b.Id
                     join bp in _db.BookPuzzles.AsNoTracking() on a.BookPuzzleId equals bp.Id
                     select new { a.AttemptedAt, a.TimeSeconds, b.Kind, bp.Tags, bp.Chapter }).ToListAsync())
            AddTime(a.AttemptedAt, a.TimeSeconds, PerPuzzleCapSeconds, Src.CourseBook,
                    PhaseFromText(a.Tags, a.Chapter) ?? (a.Kind == BookKind.Study ? Thm.Other : Thm.Tactics));

        // Quelle „chessable": aktiv trainierte Zeit-Häppchen — Thema aus manueller Kurs-Zuordnung
        // (Vorrang), sonst aus CourseKind (Repertoire-Auto), sonst other.
        var chessableThemes = await _db.ChessableCourseThemes.AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToDictionaryAsync(t => t.CourseId, t => t.Theme);
        foreach (var a in await _db.ChessableActivities.AsNoTracking()
                     .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
                     .Select(a => new { a.AttemptedAt, a.TimeSeconds, a.CourseKind, a.CourseId }).ToListAsync())
        {
            Thm thm = a.CourseId != null && chessableThemes.TryGetValue(a.CourseId, out var manual)
                ? ThemeFromChessable(manual)
                : ThemeFromCourseKind(a.CourseKind);
            AddTime(a.AttemptedAt, a.TimeSeconds, PerChessableFlushCapSeconds, Src.Chessable, thm);
        }

        // Spielen: externe Rapid-/Classical-Partien je Tag (Lichess/chess.com) — separat, kein Zeit-Topf.
        foreach (var p in await _db.PlayTimeDailies.AsNoTracking()
                     .Where(p => p.UserId == userId && p.Date >= DateOnly.FromDateTime(windowStartUtc.Date))
                     .Select(p => new { p.Date, p.Games }).ToListAsync())
            Day(p.Date).PlayGames += Math.Max(0, p.Games);

        // Manuell eingetragene Offline-Aktivitäten: mappen je Art auf Quelle/Thema (Minuten) bzw. Spielen.
        var windowStartDate = DateOnly.FromDateTime(windowStartUtc.Date);
        foreach (var m in await _db.ManualActivities.AsNoTracking()
                     .Where(m => m.UserId == userId && m.Date >= windowStartDate)
                     .Select(m => new { m.Date, m.Kind, m.Amount, m.Theme }).ToListAsync())
        {
            Day(m.Date).HasManual = true;
            switch (m.Kind)
            {
                case ManualActivityKind.OtbGame:
                    // Themen-Zuordnung ist bei OtbGame zeitunwirksam (füttert nur PlayGames).
                    Day(m.Date).PlayGames += Math.Clamp(m.Amount, 0, ManualGamesCap);
                    break;
                case ManualActivityKind.OfflinePuzzle:
                    AddTime(m.Date.ToDateTime(TimeOnly.MinValue), m.Amount * 60, PerSessionCapSeconds, Src.RandomPuzzle,
                        m.Theme.HasValue ? ThemeFromChessable(m.Theme.Value) : Thm.Tactics);
                    break;
                case ManualActivityKind.OfflineStudy:
                case ManualActivityKind.Coaching:
                    AddTime(m.Date.ToDateTime(TimeOnly.MinValue), m.Amount * 60, PerSessionCapSeconds, Src.CourseBook,
                        m.Theme.HasValue ? ThemeFromChessable(m.Theme.Value) : Thm.Other);
                    break;
            }
        }

        return acc;
    }

    // ----- Theme-Klassifikation (best-effort) ------------------------------

    /// <summary>Erkennt eine Partiephase aus freiem Text (Lichess-Themes, BookPuzzle-Tags/Chapter).
    /// Reihenfolge Endspiel → Mittelspiel → Eröffnung (Lichess <c>rookEndgame</c> etc. matchen „endgame").
    /// Gibt null zurück, wenn kein Phasen-Signal gefunden wurde.</summary>
    private static Thm? PhaseFromText(params string?[] texts)
    {
        foreach (var raw in texts)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var t = raw.ToLowerInvariant();
            if (t.Contains("endgame") || t.Contains("endspiel")) return Thm.Endgame;
            if (t.Contains("middlegame") || t.Contains("middle game") || t.Contains("mittelspiel")) return Thm.Middlegame;
            if (t.Contains("opening") || t.Contains("eröffnung") || t.Contains("eroeffnung")) return Thm.Opening;
        }
        return null;
    }

    private static Thm ThemeFromCourseKind(RepertoireKind? k) => k switch
    {
        RepertoireKind.Opening => Thm.Opening,
        RepertoireKind.Middlegame => Thm.Middlegame,
        RepertoireKind.Endgame => Thm.Endgame,
        _ => Thm.Other,
    };

    private static Thm ThemeFromChessable(ChessableTheme t) => t switch
    {
        ChessableTheme.Opening => Thm.Opening,
        ChessableTheme.Middlegame => Thm.Middlegame,
        ChessableTheme.Endgame => Thm.Endgame,
        ChessableTheme.Tactics => Thm.Tactics,
        _ => Thm.Other,
    };

    // ----- Helfer ----------------------------------------------------------

    private static void Accumulate(int[] target, int[] add)
    {
        for (var i = 0; i < target.Length; i++) target[i] += add[i];
    }

    private static SourceBreakdownDto SourceDto(int[] s) => new()
    {
        RandomPuzzleSeconds = s[(int)Src.RandomPuzzle],
        CourseBookSeconds = s[(int)Src.CourseBook],
        ChessableSeconds = s[(int)Src.Chessable],
    };

    private static ThemeBreakdownDto ThemeDto(int[] t) => new()
    {
        OpeningSeconds = t[(int)Thm.Opening],
        MiddlegameSeconds = t[(int)Thm.Middlegame],
        EndgameSeconds = t[(int)Thm.Endgame],
        TacticsSeconds = t[(int)Thm.Tactics],
        OtherSeconds = t[(int)Thm.Other],
    };

    /// <summary>Tagesstatus gegenüber dem einen Tageszeit-Ziel: "none" wenn kein Ziel/keine Zeit,
    /// "full" wenn Ziel erreicht, sonst "partial".</summary>
    internal static string DayStatus(int totalSeconds, int dailyMinutes)
    {
        if (dailyMinutes <= 0 || totalSeconds <= 0) return "none";
        return totalSeconds >= dailyMinutes * 60 ? "full" : "partial";
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
        g.DailyMinutes = dto.DailyMinutes;
        g.PlayGames = dto.PlayGames;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static void Apply(UserTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.DailyMinutes = dto.DailyMinutes;
        g.PlayGames = dto.PlayGames;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static TrainingGoalDto Map(int dailyMinutes, int playGames, int weekly, string source, string? groupName) => new()
    {
        DailyMinutes = dailyMinutes,
        PlayGames = playGames,
        WeeklyDaysTarget = weekly,
        Source = source,
        GroupName = groupName,
    };

    // ----- Aktivitäts-Vorlagen + Timer -------------------------------------
    //
    // Vorlagen (ActivityPreset) sind wiederverwendbare Kurztexte („Coaching mit Trainer X", „Buch Y")
    // mit einer <see cref="ManualActivityKind"/> — vom User selbst gepflegt, per Dashboard-„+"-Knopf
    // schnell startbar. Der Timer (ActivityTimer, max. 1 je User) läuft solange, bis der User ihn
    // stoppt (Backdate möglich via <see cref="StopActivityTimerDto.EndedAt"/>, falls das Ausschalten
    // vergessen wurde) — dann entsteht ein <see cref="ManualActivity"/>-Eintrag mit gerundeten Minuten
    // und die Vorlage bleibt für den nächsten Start bestehen. Vorlagen dürfen nur Minuten-Arten
    // tragen (OtbGame ist als getakteter Timer nicht sinnvoll — dafür bleibt der bestehende Manual-Add).

    private static bool IsTimerKind(ManualActivityKind kind) => kind switch
    {
        ManualActivityKind.OfflinePuzzle => true,
        ManualActivityKind.OfflineStudy => true,
        ManualActivityKind.Coaching => true,
        _ => false,
    };

    public async Task<List<ActivityPresetDto>> ListPresetsAsync(int userId)
        => await _db.ActivityPresets.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Id)
            .Select(p => new ActivityPresetDto { Id = p.Id, Label = p.Label, Kind = p.Kind, Theme = p.Theme })
            .ToListAsync();

    public async Task<ActivityPresetDto> AddPresetAsync(int userId, ActivityPresetInputDto dto)
    {
        var label = (dto.Label ?? string.Empty).Trim();
        if (label.Length == 0) throw new ArgumentException("Label darf nicht leer sein.");
        if (!IsTimerKind(dto.Kind)) throw new ArgumentException("Für Timer-Vorlagen sind nur Minuten-Arten erlaubt.");
        var entity = new ActivityPreset
        {
            UserId = userId, Label = label, Kind = dto.Kind, Theme = dto.Theme,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.ActivityPresets.Add(entity);
        await _db.SaveChangesAsync();
        return new ActivityPresetDto { Id = entity.Id, Label = entity.Label, Kind = entity.Kind, Theme = entity.Theme };
    }

    public async Task<ActivityPresetDto?> UpdatePresetAsync(int userId, int id, ActivityPresetInputDto dto)
    {
        var entity = await _db.ActivityPresets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (entity == null) return null;
        var label = (dto.Label ?? string.Empty).Trim();
        if (label.Length == 0) throw new ArgumentException("Label darf nicht leer sein.");
        if (!IsTimerKind(dto.Kind)) throw new ArgumentException("Für Timer-Vorlagen sind nur Minuten-Arten erlaubt.");
        entity.Label = label;
        entity.Kind = dto.Kind;
        entity.Theme = dto.Theme;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new ActivityPresetDto { Id = entity.Id, Label = entity.Label, Kind = entity.Kind, Theme = entity.Theme };
    }

    public async Task<bool> DeletePresetAsync(int userId, int id)
    {
        var entity = await _db.ActivityPresets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (entity == null) return false;
        _db.ActivityPresets.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ActivityTimerDto?> GetTimerAsync(int userId)
    {
        var t = await _db.ActivityTimers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        return t == null ? null : ToTimerDto(t);
    }

    /// <summary>Startet einen neuen Timer (ersetzt einen ggf. laufenden — der laufende wird STILL
    /// verworfen, ohne einen ManualActivity-Eintrag zu erzeugen; damit versehentlicher Verlust nicht
    /// passiert, sollte der Client vorher warnen).</summary>
    public async Task<ActivityTimerDto> StartTimerAsync(int userId, StartActivityTimerDto dto)
    {
        string label;
        ManualActivityKind kind;
        ChessableTheme? theme;

        if (dto.PresetId is int pid)
        {
            var preset = await _db.ActivityPresets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.UserId == userId)
                ?? throw new ArgumentException("Vorlage nicht gefunden.");
            label = preset.Label;
            kind = preset.Kind;
            theme = dto.Theme ?? preset.Theme;   // Preset-Thema als Default, Client-Override möglich
        }
        else
        {
            label = (dto.Label ?? string.Empty).Trim();
            if (label.Length == 0) throw new ArgumentException("Label darf nicht leer sein.");
            if (dto.Kind is not { } k || !IsTimerKind(k)) throw new ArgumentException("Für Timer sind nur Minuten-Arten erlaubt.");
            kind = k;
            theme = dto.Theme;
        }

        var existing = await _db.ActivityTimers.FirstOrDefaultAsync(x => x.UserId == userId);
        if (existing != null) _db.ActivityTimers.Remove(existing);
        var timer = new ActivityTimer { UserId = userId, Label = label, Kind = kind, Theme = theme, StartedAt = DateTime.UtcNow };
        _db.ActivityTimers.Add(timer);
        await _db.SaveChangesAsync();
        return ToTimerDto(timer);
    }

    /// <summary>Stoppt den laufenden Timer, erzeugt einen <see cref="ManualActivity"/>-Eintrag
    /// mit der gerechneten Dauer (in Minuten, gerundet, geklemmt) und entfernt den Timer. 404 wenn
    /// kein Timer läuft. <see cref="StopActivityTimerDto.StartedAt"/> UND <see cref="StopActivityTimerDto.EndedAt"/>
    /// dürfen zurückdatiert werden — der Client hält Start/Ende/Dauer selbst konsistent, der Server
    /// validiert nur Start ≤ Ende ≤ jetzt.</summary>
    public async Task<ManualActivityDto?> StopTimerAsync(int userId, StopActivityTimerDto dto)
    {
        var timer = await _db.ActivityTimers.FirstOrDefaultAsync(x => x.UserId == userId);
        if (timer == null) return null;

        var now = DateTime.UtcNow;
        var startedAt = timer.StartedAt;
        if (!string.IsNullOrWhiteSpace(dto.StartedAt))
        {
            if (!DateTime.TryParse(dto.StartedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsedStart))
                throw new ArgumentException("Ungültige Startzeit (ISO 8601 erwartet).");
            startedAt = parsedStart.ToUniversalTime();
            if (startedAt > now) throw new ArgumentException("Start darf nicht in der Zukunft liegen.");
        }

        var endedAt = now;
        if (!string.IsNullOrWhiteSpace(dto.EndedAt))
        {
            if (!DateTime.TryParse(dto.EndedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsedEnd))
                throw new ArgumentException("Ungültiges Enddatum (ISO 8601 erwartet).");
            endedAt = parsedEnd.ToUniversalTime();
            if (endedAt > now) endedAt = now;
        }
        if (endedAt < startedAt) throw new ArgumentException("Ende darf nicht vor dem Start liegen.");

        var seconds = (int)Math.Round((endedAt - startedAt).TotalSeconds);
        var minutes = Math.Max(1, Math.Min(600, (int)Math.Round(seconds / 60.0)));
        var date = DateOnly.FromDateTime(endedAt);

        var manual = new ManualActivity
        {
            UserId = userId,
            Date = date,
            Kind = timer.Kind,
            Amount = minutes,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? timer.Label : $"{timer.Label} — {dto.Note!.Trim()}",
            Theme = dto.Theme ?? timer.Theme,   // Override > Timer > null
            CreatedAt = DateTime.UtcNow,
        };
        _db.ManualActivities.Add(manual);
        _db.ActivityTimers.Remove(timer);
        await _db.SaveChangesAsync();

        return new ManualActivityDto
        {
            Id = manual.Id,
            Date = manual.Date.ToString("yyyy-MM-dd"),
            Kind = manual.Kind,
            Amount = manual.Amount,
            Note = manual.Note,
            Theme = manual.Theme,
        };
    }

    /// <summary>Wirft den laufenden Timer weg, ohne einen Eintrag zu erzeugen. true wenn etwas
    /// entfernt wurde, sonst false (kein Timer war aktiv).</summary>
    public async Task<bool> DiscardTimerAsync(int userId)
    {
        var timer = await _db.ActivityTimers.FirstOrDefaultAsync(x => x.UserId == userId);
        if (timer == null) return false;
        _db.ActivityTimers.Remove(timer);
        await _db.SaveChangesAsync();
        return true;
    }

    private static ActivityTimerDto ToTimerDto(ActivityTimer t)
    {
        var elapsed = (int)Math.Max(0, Math.Round((DateTime.UtcNow - t.StartedAt).TotalSeconds));
        return new ActivityTimerDto
        {
            Label = t.Label,
            Kind = t.Kind,
            Theme = t.Theme,
            StartedAt = DateTime.SpecifyKind(t.StartedAt, DateTimeKind.Utc).ToString("o"),
            ElapsedSeconds = elapsed,
        };
    }
}
