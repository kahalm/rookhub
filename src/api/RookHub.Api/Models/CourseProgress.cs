using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Leichter Per-Kurs-Zustand eines Users für ein <see cref="Book"/>: zuletzt genutzter
/// Modus + Zeitstempel. Der eigentliche Fortschritt (welche Puzzles gelöst sind) liegt in
/// <see cref="CoursePuzzleResult"/>; eine Zeile je (UserId, BookId).
/// </summary>
public class CourseProgress
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Zuletzt genutzter Modus: "sequential" oder "random".</summary>
    [MaxLength(20)]
    public string? LastMode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Zeitpunkt des letzten Kurs-Resets (oder <c>null</c>, nie zurückgesetzt). Nur Versuche
    /// (<see cref="CourseAttempt"/>) ab diesem Zeitpunkt zählen für die im Kurs angezeigte
    /// Zeit + Erst-Versuch-Trefferquote — nach einem Reset gilt jedes Puzzle wieder als
    /// „erster Versuch". Die CourseAttempts selbst bleiben erhalten (Trainingsziel-Zeit-Tracker).
    /// </summary>
    public DateTime? ResetAt { get; set; }
}
