using System.ComponentModel.DataAnnotations;

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

    /// <summary>Anzahl Fehlzüge (Abweichungen vom Lösungszug) in diesem Puzzle. 0 bei Alt-Datensätzen.</summary>
    public int WrongAttempts { get; set; }

    /// <summary>Anzahl genutzter Mausrutscher in diesem Puzzle (pro Puzzle höchstens 1). 0 bei Alt-Datensätzen.</summary>
    public int Mouseslips { get; set; }

    /// <summary>
    /// Spielmodus dieses Versuchs: <see cref="ModeTraining"/> (Brett eingefroren, Figuren werden NICHT
    /// gezogen — bisheriges Verhalten und Default für Altbestand) oder <see cref="ModeEasy"/> (Figuren
    /// normal ziehbar). Wird nur beim ERSTEN Versuch je (Post, User, Index) geschrieben (idempotent).
    /// </summary>
    [Required, MaxLength(10)]
    public string Mode { get; set; } = ModeTraining;

    /// <summary>Modus „Training": Brett eingefroren, Figuren nicht ziehbar (Default/Altbestand).</summary>
    public const string ModeTraining = "training";

    /// <summary>Modus „Einfach": Figuren normal ziehbar.</summary>
    public const string ModeEasy = "easy";

    /// <summary>Normalisiert einen vom Client gelieferten Modus — fehlt er oder ist er unbekannt, gilt „training".</summary>
    public static string NormalizeMode(string? mode) =>
        string.Equals(mode?.Trim(), ModeEasy, StringComparison.OrdinalIgnoreCase) ? ModeEasy : ModeTraining;

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
