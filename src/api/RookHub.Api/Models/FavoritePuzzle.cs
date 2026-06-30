namespace RookHub.Api.Models;

/// <summary>
/// Ein vom User „geliebtes"/favorisiertes Puzzle. Polymorph wie <see cref="PuzzleChallenge"/>:
/// je nach <see cref="Source"/> referenziert <see cref="PuzzleId"/> eine <c>Puzzles.Id</c>
/// (Standard/Endless) oder eine <c>BookPuzzles.Id</c> (Buch/Kurs/Tagespuzzle) — bewusst ohne harten FK.
/// Wiederfindbar unter <c>/favorites</c> (Nachspielen + Analysieren).
/// </summary>
public class FavoritePuzzle
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>ID des Puzzles — je nach <see cref="Source"/> eine <c>Puzzles.Id</c> oder <c>BookPuzzles.Id</c>.
    /// Polymorph, daher kein FK; Existenz wird im <c>FavoriteService</c> je Quelle geprüft.</summary>
    public int PuzzleId { get; set; }

    /// <summary>Quelle des Puzzles (Standard vs. Buch) — steuert Validierung, Metadaten-Lookup und Deep-Link.</summary>
    public PuzzleSource Source { get; set; } = PuzzleSource.Standard;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
