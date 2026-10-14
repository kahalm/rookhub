using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein Woodpecker-Training (Smith/Tikkanen, „The Woodpecker Method") über einen oder mehrere Kurse:
/// Zyklus 1 löst in vier Wochen so viele Aufgaben wie möglich (das ist der SATZ, <see cref="WoodpeckerSetItem"/>),
/// jeder weitere Zyklus löst denselben Satz in zufälliger Reihenfolge in der halben Zeit. Die Regeln stehen fest in
/// <c>Services/Courses/WoodpeckerRules.cs</c> — der Nutzer stellt nichts ein außer Kursen und (rückdatierbarem) Start.
/// </summary>
public class WoodpeckerPlan
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Methode abgeschlossen: ein Zyklus ab 2 an einem Tag geschafft, oder der 7. Zyklus beendet.</summary>
    public DateTime? CompletedAt { get; set; }

    public List<WoodpeckerPlanBook> Books { get; set; } = new();
    public List<WoodpeckerCycle> Cycles { get; set; } = new();
}

/// <summary>Ein Kurs (<see cref="Book"/>) des Plans; <see cref="Position"/> = Reihenfolge in Zyklus 1.</summary>
public class WoodpeckerPlanBook
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public WoodpeckerPlan? Plan { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public int Position { get; set; }
}

/// <summary>Ein Zyklus (Nummer 1–7). Frist = <see cref="StartedAt"/> + Tage laut Buch (28, 14, 7, 4, 2, 1, 1).</summary>
public class WoodpeckerCycle
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public WoodpeckerPlan? Plan { get; set; }
    public int Number { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>Eine Aufgabe des Satzes — entsteht in Zyklus 1 mit dem ersten Versuch an ihr.
/// <see cref="BookId"/> denormalisiert, damit das Löschen eines Kurses den Eintrag mitnimmt.</summary>
public class WoodpeckerSetItem
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public WoodpeckerPlan? Plan { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }
    /// <summary>Reihenfolge, in der die Aufgabe in den Satz kam.</summary>
    public int Position { get; set; }
}

/// <summary>Der (erste und einzige gewertete) Versuch an einer Aufgabe in einem Zyklus.</summary>
public class WoodpeckerAttempt
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public WoodpeckerPlan? Plan { get; set; }
    public int Cycle { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
