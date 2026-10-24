using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Chessable;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;
using RookHub.Api.Services.Social.TrainingGoals;

namespace RookHub.Api.Services.Social;

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
///
/// Aufbau (Codereview N9-008): diese Klasse ist die Fassade für Controller, Bot-Statistik, Vereins-Fortschritt,
/// Spielzeit-Abgleich und Extension. Die Arbeit liegt in <c>Services/Social/TrainingGoals/</c> — Ziele
/// (<see cref="TrainingGoalStore"/>), Chessable-Aktivität (<see cref="ChessableActivityLog"/>), manuelle Einträge
/// (<see cref="ManualActivityLog"/>), Vorlagen + Timer (<see cref="ActivityTimerStore"/>), je Quelle Abfrage +
/// Zusammenführung (<see cref="TrainingAggregation"/>) und die reine Projektion auf Tracker/Tagesreihe/Heute
/// (<see cref="TrainingDayProjection"/>). Hier bleiben nur der Kurz-Cache der Tagesreihe und das Durchreichen.
/// </summary>
public class TrainingGoalService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache? _cache;
    private readonly TrainingGoalStore _goals;
    private readonly ChessableActivityLog _chessable;
    private readonly ManualActivityLog _manual;
    private readonly ActivityTimerStore _timer;

    // cache optional: bestehende Test-Konstruktionen (new TrainingGoalService(db)) bleiben gültig;
    // DI injiziert den registrierten IMemoryCache. Ohne Cache wird immer frisch gerechnet.
    public TrainingGoalService(AppDbContext db, IMemoryCache? cache = null)
    {
        _db = db;
        _cache = cache;
        _goals = new TrainingGoalStore(db, InvalidateDailySeries);
        _chessable = new ChessableActivityLog(db, InvalidateDailySeries);
        _manual = new ManualActivityLog(db, InvalidateDailySeries);
        _timer = new ActivityTimerStore(db, InvalidateDailySeries);
    }

    // ----- Ziel-Auflösung + Gruppen-Vorlage --------------------------------

    /// <summary>Effektives Ziel des Users: persönlich &gt; Gruppen-Vorlage (zuletzt aktualisierte) &gt; keins.</summary>
    public Task<TrainingGoalDto> GetEffectiveGoalAsync(int userId) => _goals.GetEffectiveGoalAsync(userId);

    /// <summary>Setzt/aktualisiert den persönlichen Override des Users und gibt das neue effektive Ziel zurück.</summary>
    public Task<TrainingGoalDto> SetPersonalGoalAsync(int userId, TrainingGoalInputDto dto) => _goals.SetPersonalGoalAsync(userId, dto);

    /// <summary>Entfernt den persönlichen Override → der User fällt auf die Gruppen-Vorlage (falls vorhanden) zurück.</summary>
    public Task<TrainingGoalDto> DeletePersonalGoalAsync(int userId) => _goals.DeletePersonalGoalAsync(userId);

    public Task<TrainingGoalDto> GetGroupGoalAsync(int groupId) => _goals.GetGroupGoalAsync(groupId);

    public Task<TrainingGoalDto> SetGroupGoalAsync(int groupId, TrainingGoalInputDto dto) => _goals.SetGroupGoalAsync(groupId, dto);

    public Task DeleteGroupGoalAsync(int groupId) => _goals.DeleteGroupGoalAsync(groupId);

    // ----- Chessable-Aktivität (von der Extension) -------------------------

    /// <summary>Chessable-Modus-/Navigations-Labels, die die Extension historisch fälschlich als Kursnamen meldete
    /// (siehe <see cref="ChessableActivityLog.IsModeLabel"/>).</summary>
    internal static bool IsChessableModeLabel(string? name) => ChessableActivityLog.IsModeLabel(name);

    /// <summary>Hängt ein Häppchen aktiver Chessable-Trainingszeit an (Quelle „chessable" des Trackers).</summary>
    public Task RecordChessableActivityAsync(int userId, ChessableActivityInputDto dto) => _chessable.RecordChessableActivityAsync(userId, dto);

    /// <summary>Kurs-History je Kurs-ID mit ermitteltem Thema; <paramref name="unassignedOnly"/> = nur Kurse ohne feststehendes Thema.</summary>
    public Task<List<ChessableCourseSummaryDto>> GetChessableCoursesAsync(int userId, bool unassignedOnly = false)
        => _chessable.GetChessableCoursesAsync(userId, unassignedOnly);

    public Task<bool> SetChessableCourseThemeAsync(int userId, string courseId, ChessableTheme theme)
        => _chessable.SetChessableCourseThemeAsync(userId, courseId, theme);

    public Task<bool> ClearChessableCourseThemeAsync(int userId, string courseId)
        => _chessable.ClearChessableCourseThemeAsync(userId, courseId);

    // ----- Manuelle Offline-Aktivitäten (selbst gemeldet) ------------------

    public Task<List<ManualActivityDto>> ListManualAsync(int userId, int take = 200) => _manual.ListManualAsync(userId, take);

    /// <summary>Legt einen manuellen Eintrag an. Wirft <see cref="ArgumentException"/> bei ungültigem Datum/Zukunft.</summary>
    public Task<ManualActivityDto> AddManualAsync(int userId, ManualActivityInputDto dto) => _manual.AddManualAsync(userId, dto);

    public Task<ManualActivityDto?> UpdateManualAsync(int userId, int id, ManualActivityInputDto dto) => _manual.UpdateManualAsync(userId, id, dto);

    public Task<bool> DeleteManualAsync(int userId, int id) => _manual.DeleteManualAsync(userId, id);

    // ----- Tracker / Heute -------------------------------------------------

    /// <summary>Tagesreihe (nur Tage mit Aktivität) der letzten <paramref name="weeks"/> Wochen + effektives Ziel
    /// + Perioden-Aufschlüsselung nach Quelle und Thema.</summary>
    public async Task<TrackerResponseDto> GetTrackerAsync(int userId, int weeks)
    {
        var goal = await GetEffectiveGoalAsync(userId);
        var windowStart = TrainingDayProjection.TrackerWindowStart(DateTime.UtcNow.Date, weeks);
        var agg = await TrainingAggregation.AggregateAsync(_db, userId, windowStart);
        return TrainingDayProjection.Tracker(goal, agg);
    }

    /// <summary>Vollständige Tagesreihe (nur Tage mit Aktivität) über die gesamte Historie — ohne das
    /// 53-Wochen-Tracker-Fenster. Liefert je Tag die Aufschlüsselung nach Quelle und Thema, damit das
    /// Frontend die Perioden-Auswahl (Tag/Woche/Monat/Jahr/Gesamt) inklusive Durchschalten rein
    /// client-seitig berechnen kann.</summary>
    public async Task<DailySeriesDto> GetDailySeriesAsync(int userId)
    {
        // Kurz-Cache je User: die Serie aggregiert die KOMPLETTE Historie über 8 Tabellen pro Request.
        // Gemessene Zeit (Puzzles, Kurse, Partien) ist praktisch append-only — 60 s Staleness ist dafür
        // unerheblich, spart aber die volle Aggregation bei jedem Seitenwechsel/Perioden-Umschalten.
        // Was der User auf der Seite SELBST ändert (Einträge, Timer, Ziel, Kurs-Thema), verwirft den
        // Eintrag sofort (InvalidateDailySeries), sonst widerspricht die Aufschlüsselung dem frischen Tracker.
        var cacheKey = DailySeriesKey(userId);
        if (_cache is not null && _cache.TryGetValue(cacheKey, out DailySeriesDto? cached) && cached is not null)
            return cached;

        var goal = await GetEffectiveGoalAsync(userId);
        var agg = await TrainingAggregation.AggregateAsync(_db, userId, DateTime.UnixEpoch);
        var dto = TrainingDayProjection.Series(goal, agg);
        _cache?.Set(cacheKey, dto, TimeSpan.FromSeconds(60));
        return dto;
    }

    private static string DailySeriesKey(int userId) => $"tg_daily_series_{userId}";

    /// <summary>Verwirft die gecachte Tagesreihe nach einer Änderung des Users in diesem Dienst.
    /// Die Seite lädt danach Tracker (frisch) und Tagesreihe gemeinsam neu — ohne das zeigte die
    /// Perioden-Aufschlüsselung bis zu 60 s den alten Stand (Codereview N9-005). Eine geänderte
    /// Gruppen-Vorlage trifft viele User und wartet bewusst die 60 s ab.</summary>
    private void InvalidateDailySeries(int userId) => _cache?.Remove(DailySeriesKey(userId));

    /// <summary>Heutiger Fortschritt (Tageszeit-Ziel + Aufschlüsselung) + Wochenstand
    /// (Spielen-Partien + voll erfüllte Tage) der laufenden ISO-Woche.</summary>
    public async Task<TodayProgressDto> GetTodayAsync(int userId)
    {
        var goal = await GetEffectiveGoalAsync(userId);
        var today = DateTime.UtcNow.Date;
        var agg = await TrainingAggregation.AggregateAsync(_db, userId, TrainingDayProjection.WeekStart(today));
        return TrainingDayProjection.Today(goal, agg, today);
    }

    /// <summary>Tagesstatus gegenüber dem einen Tageszeit-Ziel: "none" wenn kein Ziel/keine Zeit,
    /// "full" wenn Ziel erreicht, sonst "partial".</summary>
    internal static string DayStatus(int totalSeconds, int dailyMinutes) => TrainingDayProjection.DayStatus(totalSeconds, dailyMinutes);

    // ----- Aktivitäts-Vorlagen + Timer -------------------------------------

    public Task<List<ActivityPresetDto>> ListPresetsAsync(int userId) => _timer.ListPresetsAsync(userId);

    public Task<ActivityPresetDto> AddPresetAsync(int userId, ActivityPresetInputDto dto) => _timer.AddPresetAsync(userId, dto);

    public Task<ActivityPresetDto?> UpdatePresetAsync(int userId, int id, ActivityPresetInputDto dto) => _timer.UpdatePresetAsync(userId, id, dto);

    public Task<bool> DeletePresetAsync(int userId, int id) => _timer.DeletePresetAsync(userId, id);

    public Task<ActivityTimerDto?> GetTimerAsync(int userId) => _timer.GetTimerAsync(userId);

    /// <summary>Startet einen neuen Timer (ersetzt einen ggf. laufenden still, ohne Eintrag).</summary>
    public Task<ActivityTimerDto> StartTimerAsync(int userId, StartActivityTimerDto dto) => _timer.StartTimerAsync(userId, dto);

    /// <summary>Stoppt den laufenden Timer, erzeugt einen <see cref="ManualActivity"/>-Eintrag und entfernt den Timer;
    /// null, wenn kein Timer läuft (siehe <see cref="ActivityTimerStore.StopTimerAsync"/>).</summary>
    public Task<ManualActivityDto?> StopTimerAsync(int userId, StopActivityTimerDto dto) => _timer.StopTimerAsync(userId, dto);

    /// <summary>Wirft den laufenden Timer weg, ohne einen Eintrag zu erzeugen.</summary>
    public Task<bool> DiscardTimerAsync(int userId) => _timer.DiscardTimerAsync(userId);

    /// <summary>Spaltenbreite von <see cref="ManualActivity.Note"/> (siehe <see cref="ActivityTimerStore.ManualNoteMaxLength"/>).</summary>
    internal const int ManualNoteMaxLength = ActivityTimerStore.ManualNoteMaxLength;

    /// <summary>Notiz des Timer-Eintrags: „{Label} — {Notiz}", gekürzt auf <see cref="ManualNoteMaxLength"/>.</summary>
    internal static string ComposeTimerNote(string label, string? note) => ActivityTimerStore.ComposeTimerNote(label, note);
}
