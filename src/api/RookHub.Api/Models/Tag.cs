using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Normalisiertes Puzzle-Thema (Lichess-Theme-Token). Eindeutig per Name.</summary>
public class Tag
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public ICollection<PuzzleTag> PuzzleTags { get; set; } = new List<PuzzleTag>();
}
