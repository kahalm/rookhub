using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class Puzzle
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string LichessId { get; set; } = string.Empty;

    [Required]
    public string Fen { get; set; } = string.Empty;

    [Required]
    public string Moves { get; set; } = string.Empty;

    public int Rating { get; set; }
    public int RatingDeviation { get; set; }
    public int Popularity { get; set; }
    public int NbPlays { get; set; }

    [MaxLength(500)]
    public string? Themes { get; set; }

    [MaxLength(500)]
    public string? GameUrl { get; set; }

    [MaxLength(500)]
    public string? OpeningTags { get; set; }

    /// <summary>Von einem Nutzer als „dumme/schlechte Tipps" markiert. Standard-Puzzles haben on-the-fly
    /// berechnete Tipps (Check–Capture–Threat); dieses Flag meldet, dass der Hinweis hier irreführt.</summary>
    public bool HintsFlagged { get; set; }
}
