namespace RookHub.Api.Models;

/// <summary>
/// Lösungsversuch an einem Buch-Puzzle (Standalone-/Tagespuzzle). Grundlage für die
/// Tagespuzzle-Visualisierung auf Discord (wer/wie viele haben heute gelöst).
/// Entweder eingeloggt (<see cref="UserId"/>) oder anonym (<see cref="AnonymousSessionId"/>).
/// </summary>
public class BookPuzzleAttempt
{
    public int Id { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle BookPuzzle { get; set; } = null!;

    /// <summary>Eingeloggter User – null bei anonymen Versuchen.</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Bei anonymen (nicht eingeloggten) Versuchen gesetzt statt <see cref="UserId"/>.</summary>
    public string? AnonymousSessionId { get; set; }

    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    public DateTime AttemptedAt { get; set; }
}
