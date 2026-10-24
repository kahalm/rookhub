using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;
using RookHub.Api.Services.Social.TrainingGoals;

namespace RookHub.Api.Tests.Social.TrainingGoals;

/// <summary>Die reinen Teile der Trainingsziele ohne Datenbank: Wochenrechnung, Fenster, Zusammenführung, Projektion.</summary>
public class TrainingDayProjectionTests
{
    [Theory]
    [InlineData("2026-10-19", "2026-10-19")]   // Montag
    [InlineData("2026-10-21", "2026-10-19")]   // Mittwoch
    [InlineData("2026-10-25", "2026-10-19")]   // Sonntag gehört zur Woche davor
    [InlineData("2026-10-26", "2026-10-26")]
    public void WeekStart_IsMonday(string today, string monday) =>
        Assert.Equal(DateTime.Parse(monday), TrainingDayProjection.WeekStart(DateTime.Parse(today)));

    [Theory]
    [InlineData(1, "2026-10-19")]
    [InlineData(0, "2026-10-19")]      // auf 1 geklemmt
    [InlineData(2, "2026-10-12")]
    [InlineData(99, "2025-10-20")]     // auf 53 geklemmt: 53*7-1 = 370 Tage zurück
    public void TrackerWindowStart_ClampsWeeks(int weeks, string expected) =>
        Assert.Equal(DateTime.Parse(expected), TrainingDayProjection.TrackerWindowStart(new DateTime(2026, 10, 25), weeks));

    [Fact]
    public void Merge_ManualAndPlay_FeedsTheRightBuckets_AndTodayCountsTheWeek()
    {
        var days = new TrainingDays();
        var mon = new DateOnly(2026, 10, 19);
        var sun = new DateOnly(2026, 10, 18);
        ManualProgress.Merge(
        [
            new ManualProgress.Row(mon, ManualActivityKind.OfflinePuzzle, 10, null),             // 600 s Taktik
            new ManualProgress.Row(mon, ManualActivityKind.Coaching, 5, ChessableTheme.Endgame),  // 300 s Endspiel
            new ManualProgress.Row(sun, ManualActivityKind.OtbGame, 99, null),                    // 50 Partien, Vorwoche
        ], days);
        PlayProgress.Merge([new PlayProgress.Row(mon, 2), new PlayProgress.Row(mon, -3)], days);

        var goal = new TrainingGoalDto { DailyMinutes = 15, PlayGames = 3, Source = "personal" };
        var today = TrainingDayProjection.Today(goal, days.Days, new DateTime(2026, 10, 20));

        Assert.Equal(0, today.Daily.DoneSeconds);       // heute (Dienstag) nichts
        Assert.Equal(2, today.Play.DoneGames);          // Montag 2 (negativ = 0), Sonntag zählt nicht
        Assert.Equal(1, today.WeekDaysMet);             // Montag 900 s = 15 min → voll

        var tracker = TrainingDayProjection.Tracker(goal, days.Days);
        Assert.Equal(["2026-10-18", "2026-10-19"], tracker.Days.Select(d => d.Date));
        Assert.Equal(50, tracker.Days[0].PlayGames);
        Assert.True(tracker.Days[0].HasManual);
        Assert.Equal(600, tracker.BreakdownByTheme.TacticsSeconds);
        Assert.Equal(300, tracker.BreakdownByTheme.EndgameSeconds);
        Assert.Equal(600, tracker.BreakdownBySource.RandomPuzzleSeconds);
        Assert.Equal(300, tracker.BreakdownBySource.CourseBookSeconds);
    }

    [Fact]
    public void AddCourseTime_SplitsAfterTheCap_RemainderFirst()
    {
        var days = new TrainingDays();
        var at = new DateTime(2026, 10, 20, 23, 59, 59);
        days.AddCourseTime(at, 5000, [TrainingTheme.Opening, TrainingTheme.Endgame, TrainingTheme.Tactics, TrainingTheme.Other, TrainingTheme.Middlegame, TrainingTheme.Opening, TrainingTheme.Opening]);

        var day = Assert.Single(days.Days).Value;
        Assert.Equal(1800, day.Total);
        Assert.Equal(1800, day.Source[(int)TrainingSource.CourseBook]);
        Assert.Equal(1800 / 7 * 3 + 1800 % 7, day.Theme[(int)TrainingTheme.Opening]);
    }
}
