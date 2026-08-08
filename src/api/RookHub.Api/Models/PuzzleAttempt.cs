namespace RookHub.Api.Models;

public class PuzzleAttempt
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    public string? AnonymousSessionId { get; set; }

    public int PuzzleId { get; set; }
    public Puzzle Puzzle { get; set; } = null!;

    public bool Solved { get; set; }
    public int TimeSpentSeconds { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    public string? MoveLog { get; set; }

    public int? EloAfter { get; set; }
    public int? EloChange { get; set; }

    /// <summary>
    /// Visualisierungsstufe dieses Versuchs (0–4). Sie ist zugleich die EINZIGE Quelle der Spielweise:
    /// 0 = <see cref="SolveMode.Easy"/> (Figuren normal ziehbar), &gt; 0 = <see cref="SolveMode.Training"/>
    /// (Brett eingefroren/Blindstufen). Eine eigene Modus-Spalte gibt es hier bewusst NICHT — sie wäre
    /// doppelt geführter Zustand und würde den gesamten Altbestand (überwiegend Stufe 0) fälschlich
    /// als „training" ausweisen.
    /// </summary>
    public int VisualizationLevel { get; set; } = 0;

    public bool EvalShown { get; set; } = false;
    public int VizShowCount { get; set; } = 0;

    /// <summary>Höchste aufgedeckte Tipp-Stufe in diesem Versuch (0 = keine, 1–3). Analog BookPuzzleAttempt.</summary>
    public int HintsUsed { get; set; } = 0;
}
