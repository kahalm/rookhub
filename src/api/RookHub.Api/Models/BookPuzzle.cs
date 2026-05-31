using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class BookPuzzle
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string LineId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BookFileName { get; set; } = string.Empty;

    /// <summary>FK auf <see cref="Models.Book"/>. Nullable für Altbestand (Backfill via Migration).</summary>
    public int? BookId { get; set; }
    public Book? Book { get; set; }

    [Required, MaxLength(20)]
    public string Round { get; set; } = string.Empty;

    [Required]
    public string Fen { get; set; } = string.Empty;

    [Required]
    public string Moves { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Chapter { get; set; }

    [MaxLength(5000)]
    public string? Comment { get; set; }

    [MaxLength(50)]
    public string? Difficulty { get; set; }

    public int? BookRating { get; set; }

    [MaxLength(200)]
    public string? Tags { get; set; }
}
