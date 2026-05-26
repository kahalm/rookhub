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
}
