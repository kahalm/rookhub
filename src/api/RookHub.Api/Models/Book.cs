using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein importiertes Puzzle-Buch (aus einer PGN-Datei). Gruppiert mehrere
/// <see cref="BookPuzzle"/> und legt fest, in welchen Pools (Daily/Random/Blind)
/// die Puzzles dieses Buchs ausgewählt werden dürfen.
/// </summary>
public class Book
{
    public int Id { get; set; }

    /// <summary>Eindeutiger Dateiname der Quelle, z. B. "1001 Deadly Checkmates.pgn".</summary>
    [Required, MaxLength(200)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Difficulty { get; set; }

    /// <summary>Schwierigkeit 1–10 (wie in der schach-bot books.json), optional.</summary>
    public int? Rating { get; set; }

    [MaxLength(200)]
    public string? Tags { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Für das deterministische Tagespuzzle nutzbar.</summary>
    public bool ForDaily { get; set; }

    /// <summary>Für /randompuzzle nutzbar.</summary>
    public bool ForRandom { get; set; }

    /// <summary>Für /blindpuzzle nutzbar.</summary>
    public bool ForBlind { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<BookPuzzle> Puzzles { get; set; } = new();
}
