namespace RookHub.Api.Models;

/// <summary>
/// Per-User-Fortschritt eines Wochenposts: eine Zeile je <em>gespieltem</em> Puzzle (identifiziert über
/// den Index im on-the-fly aus dem PGN geparsten Puzzle-Sequenz). Unique über
/// (WeeklyPostId, UserId, PuzzleIndex) macht das Aufzeichnen idempotent — der erste Versuch zählt.
/// <see cref="Solved"/> hält das Ergebnis fest; ein Post gilt als „erledigt", wenn ALLE Puzzles
/// gespielt wurden (gelöst oder nicht).
/// </summary>
public class WeeklyPostAttempt
{
    public int Id { get; set; }

    public int WeeklyPostId { get; set; }
    public WeeklyPost? WeeklyPost { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>0-basierter Index des Puzzles in der geparsten Sequenz des Wochenposts.</summary>
    public int PuzzleIndex { get; set; }

    public bool Solved { get; set; }

    /// <summary>Am Puzzle verbrachte Zeit in Sekunden.</summary>
    public int TimeSeconds { get; set; }

    /// <summary>Höchste angesehene Tipp-Stufe in diesem Versuch (0 = keine, 1–3). &gt; 0 ⇒ mit Tipps gelöst.</summary>
    public int HintsUsed { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
