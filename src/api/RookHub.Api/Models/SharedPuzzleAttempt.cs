using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Versuch an einem per Teilen-Link mit „Track solves" geteilten Buch-Puzzle. Pro Besucher zählt
/// nur der ERSTE Versuch — erzwungen über den Unique-Index (<see cref="BookPuzzleId"/>,
/// <see cref="IdentityKey"/>). <see cref="Solved"/>=true nur bei sauberer Erstlösung; alles andere
/// (Fehlzug, Aufgeben, Reset) zählt als „failed".
/// </summary>
public class SharedPuzzleAttempt
{
    public int Id { get; set; }

    /// <summary>BookPuzzles.Id des geteilten Puzzles (bewusst ohne harte FK-Navigation, um doppelte
    /// Cascade-Pfade zu vermeiden; per Index abgedeckt).</summary>
    public int BookPuzzleId { get; set; }

    /// <summary>Identität des Besuchers: <c>u:{userId}</c> (eingeloggt) bzw. <c>s:{sessionId}</c> (anonym).
    /// Teil des Unique-Index → ein Besucher zählt genau einmal je Puzzle.</summary>
    [Required, MaxLength(64)]
    public string IdentityKey { get; set; } = string.Empty;

    public bool Solved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
