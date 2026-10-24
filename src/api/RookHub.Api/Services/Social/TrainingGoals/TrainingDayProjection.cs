using RookHub.Api.DTOs.Social;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Aus den Tages-Eimern und dem effektiven Ziel die Antworten bauen — Tracker, Tagesreihe, Heute — samt
/// Tagesstatus und Wochenrechnung. Reine Funktionen: „heute" wird übergeben, nichts liest die Uhr.
/// </summary>
internal static class TrainingDayProjection
{
    public const int MaxTrackerWeeks = 53;

    /// <summary>Erster Tag (00:00 UTC) des Tracker-Fensters: <paramref name="weeks"/> (1..53) ganze Wochen bis einschließlich heute.</summary>
    public static DateTime TrackerWindowStart(DateTime todayUtc, int weeks) =>
        todayUtc.AddDays(-(Math.Clamp(weeks, 1, MaxTrackerWeeks) * 7 - 1));

    /// <summary>Montag der ISO-Woche von <paramref name="todayUtc"/> (Wochenanfang = Montag).</summary>
    public static DateTime WeekStart(DateTime todayUtc)
    {
        var dow = ((int)todayUtc.DayOfWeek + 6) % 7; // 0 = Montag
        return todayUtc.AddDays(-dow);
    }

    public static TrackerResponseDto Tracker(TrainingGoalDto goal, Dictionary<DateOnly, DayBuckets> agg)
    {
        var bySource = new int[3];
        var byTheme = new int[5];
        var days = agg
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                Accumulate(bySource, kv.Value.Source);
                Accumulate(byTheme, kv.Value.Theme);
                return MapDay(kv.Key, kv.Value, goal);
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

    public static DailySeriesDto Series(TrainingGoalDto goal, Dictionary<DateOnly, DayBuckets> agg) => new()
    {
        Days = agg.OrderBy(kv => kv.Key).Select(kv => MapDay(kv.Key, kv.Value, goal)).ToList(),
    };

    /// <summary>Heutiger Fortschritt + Wochenstand (Partien und voll erfüllte Tage von Montag bis heute).</summary>
    public static TodayProgressDto Today(TrainingGoalDto goal, Dictionary<DateOnly, DayBuckets> agg, DateTime todayUtc)
    {
        var todayKey = DateOnly.FromDateTime(todayUtc);
        var weekStartKey = DateOnly.FromDateTime(WeekStart(todayUtc));
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

    /// <summary>EIN Mapping Aggregat-Tag → <see cref="TrackerDayDto"/> — genutzt von Tracker-Heatmap
    /// UND vollständiger Tagesreihe. Vorher zwei Feld-für-Feld-Kopien: ein neues Pro-Tag-Feld
    /// (wie seinerzeit HasManual) oder eine DayStatus-Änderung musste in beiden Projektionen
    /// nachgezogen werden, sonst widersprachen sich Heatmap und Historie still.</summary>
    public static TrackerDayDto MapDay(DateOnly date, DayBuckets agg, TrainingGoalDto goal) => new()
    {
        Date = date.ToString("yyyy-MM-dd"),
        TotalSeconds = agg.Total,
        BySource = SourceDto(agg.Source),
        ByTheme = ThemeDto(agg.Theme),
        PlayGames = agg.PlayGames,
        Status = DayStatus(agg.Total, goal.DailyMinutes),
        HasManual = agg.HasManual,
    };

    /// <summary>Tagesstatus gegenüber dem einen Tageszeit-Ziel: "none" wenn kein Ziel/keine Zeit,
    /// "full" wenn Ziel erreicht, sonst "partial".</summary>
    public static string DayStatus(int totalSeconds, int dailyMinutes)
    {
        if (dailyMinutes <= 0 || totalSeconds <= 0) return "none";
        return totalSeconds >= dailyMinutes * 60 ? "full" : "partial";
    }

    private static void Accumulate(int[] target, int[] add)
    {
        for (var i = 0; i < target.Length; i++) target[i] += add[i];
    }

    private static SourceBreakdownDto SourceDto(int[] s) => new()
    {
        RandomPuzzleSeconds = s[(int)TrainingSource.RandomPuzzle],
        CourseBookSeconds = s[(int)TrainingSource.CourseBook],
        ChessableSeconds = s[(int)TrainingSource.Chessable],
    };

    private static ThemeBreakdownDto ThemeDto(int[] t) => new()
    {
        OpeningSeconds = t[(int)TrainingTheme.Opening],
        MiddlegameSeconds = t[(int)TrainingTheme.Middlegame],
        EndgameSeconds = t[(int)TrainingTheme.Endgame],
        TacticsSeconds = t[(int)TrainingTheme.Tactics],
        OtherSeconds = t[(int)TrainingTheme.Other],
    };

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
}
