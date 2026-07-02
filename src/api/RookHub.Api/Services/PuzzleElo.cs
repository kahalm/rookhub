using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Reine Puzzle-Elo-Mathematik, geteilt zwischen dem Versuchs-Recording (<see cref="PuzzleService"/>)
/// und der Statistik (<see cref="PuzzleStatsService"/>): Default-Elo je Visualisierungs-Level,
/// Lesen/Schreiben des Level-Elos am <see cref="AppUser"/>, provisorischer K-Faktor zur
/// Start-Kalibrierung und die Elo-Fortschreibung selbst. Zustandslos/statisch, gut testbar.
/// </summary>
public static class PuzzleElo
{
    public static int GetDefaultElo(int level) => Math.Max(100, 1500 - 100 * level);

    internal static int GetEloForLevel(AppUser user, int level) => level switch
    {
        0 => user.PuzzleElo,
        1 => user.PuzzleEloViz1 ?? GetDefaultElo(1),
        2 => user.PuzzleEloViz2 ?? GetDefaultElo(2),
        3 => user.PuzzleEloViz3 ?? GetDefaultElo(3),
        4 => user.PuzzleEloViz4 ?? GetDefaultElo(4),
        _ => user.PuzzleElo
    };

    internal static void SetEloForLevel(AppUser user, int level, int elo)
    {
        switch (level)
        {
            case 0: user.PuzzleElo = elo; break;
            case 1: user.PuzzleEloViz1 = elo; break;
            case 2: user.PuzzleEloViz2 = elo; break;
            case 3: user.PuzzleEloViz3 = elo; break;
            case 4: user.PuzzleEloViz4 = elo; break;
        }
    }

    internal static Dictionary<int, int> BuildEloDict(AppUser user) => new()
    {
        [0] = user.PuzzleElo,
        [1] = user.PuzzleEloViz1 ?? GetDefaultElo(1),
        [2] = user.PuzzleEloViz2 ?? GetDefaultElo(2),
        [3] = user.PuzzleEloViz3 ?? GetDefaultElo(3),
        [4] = user.PuzzleEloViz4 ?? GetDefaultElo(4),
    };

    /// <summary>Normaler (eingependelter) Elo-K-Faktor.</summary>
    internal const int BaseKFactor = 20;

    /// <summary>
    /// Provisorischer K-Faktor zur schnellen Start-Kalibrierung der Puzzle-Elo. Solange das Niveau
    /// noch nicht getroffen ist, größere Schritte in BEIDE Richtungen (<see cref="CalculateElo"/>
    /// skaliert Gewinn und Verlust gleich): ×4 bis mind. 5 gelöste UND 5 gescheiterte Versuche,
    /// ×2 bis 10 UND 10, danach normale Schrittweite. BEIDES nötig (gelöst und gescheitert), damit
    /// man wirklich einpendelt statt nur in eine Richtung davonzulaufen (viele leichte Treffer ohne
    /// einen einzigen Fehlschlag heißt: Niveau noch nicht gefunden → weiter große Schritte).
    /// </summary>
    internal static int ProvisionalKFactor(int solvedCount, int failedCount)
    {
        if (solvedCount < 5 || failedCount < 5) return BaseKFactor * 4;    // ×4
        if (solvedCount < 10 || failedCount < 10) return BaseKFactor * 2;  // ×2
        return BaseKFactor;
    }

    internal static (int newRating, int change) CalculateElo(int userRating, int puzzleRating, bool solved, int kFactor)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10.0, (puzzleRating - userRating) / 400.0));
        double actual = solved ? 1.0 : 0.0;
        int change = (int)Math.Round(kFactor * (actual - expected));
        int newRating = Math.Max(100, userRating + change);
        return (newRating, newRating - userRating);
    }
}
