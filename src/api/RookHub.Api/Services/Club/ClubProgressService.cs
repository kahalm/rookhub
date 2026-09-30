using RookHub.Api.DTOs;

namespace RookHub.Api.Services.Club;

/// <summary>
/// Lernstand aus dem verknüpften Konto: was RookHub und KidHub ohnehin zählen, als Summen für das Karteiblatt. Bündelt nur
/// bestehende Dienste (<see cref="PuzzleStatsService"/>, <see cref="TrainingGoalService"/>, <see cref="KidsProgressService"/>)
/// — keine eigene Zählung, damit Trainer und Kind dieselben Zahlen sehen. Wer das sehen darf, entscheidet
/// <see cref="ClubService.LinkedAccountAsync"/>; die Verknüpfung selbst hat das Konto per Code eingewilligt.
/// </summary>
public class ClubProgressService(PuzzleStatsService puzzles, TrainingGoalService goals, KidsProgressService kids)
{
    /// <summary>Fenster der Trainingszeit: die letzten vier Wochen.</summary>
    public const int Weeks = 4;

    public async Task<ClubProgressDto> GetAsync(int userId, string username, CancellationToken ct = default)
    {
        var stats = await puzzles.GetStatsAsync(userId, null);
        var tracker = await goals.GetTrackerAsync(userId, Weeks);
        var kid = await kids.GetAsync(userId, ct);
        var active = tracker.Days.Where(d => d.TotalSeconds > 0).ToList();
        return new ClubProgressDto
        {
            Username = username,
            PuzzleAttempts = stats.TotalAttempts,
            PuzzlesSolved = stats.Solved,
            PuzzleAccuracy = stats.Accuracy,
            PuzzleElo = stats.PuzzleElo,
            BestStreak = stats.BestStreak,
            Minutes28 = (int)Math.Round(active.Sum(d => (long)d.TotalSeconds) / 60.0),
            ActiveDays28 = active.Count,
            LastActive = active.Count == 0 ? null : active.Max(d => d.Date),
            KidsLevelsDone = kid.Levels.Count(l => l.Stars > 0),
            KidsStars = kid.Levels.Sum(l => l.Stars),
            KidsCourseLines = kid.Courses.Sum(c => c.Solved.Count),
        };
    }
}
