using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Request zum Favorisieren/Entfernen (POST/DELETE-Body bzw. Query).</summary>
public class ToggleFavoriteDto
{
    public int PuzzleId { get; set; }
    public PuzzleSource Source { get; set; } = PuzzleSource.Standard;
}

/// <summary>Ein geliebtes Puzzle inkl. Metadaten zum Nachspielen (Deep-Link) und Analysieren (Fen+Moves).</summary>
public class FavoritePuzzleDto
{
    public int Id { get; set; }
    public int PuzzleId { get; set; }
    public string Source { get; set; } = nameof(PuzzleSource.Standard);
    public int Rating { get; set; }
    public string? Themes { get; set; }
    public string? Title { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
