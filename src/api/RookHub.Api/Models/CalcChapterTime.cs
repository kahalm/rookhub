using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Kapitel-Uhr des Kalkulations-Modus je Nutzer, Buch und Kapitel (1.020.1): die aufsummierte Rechenzeit. Vorher nur im
/// localStorage (<c>rookhub_calc_timer_&lt;bookId&gt;</c>), beim Abmelden geräumt — die Zeit war weg.
/// </summary>
public class CalcChapterTime
{
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Kapitelname wie im Buch (<c>""</c> = „ohne Kapitel“).</summary>
    [MaxLength(200)]
    public string Chapter { get; set; } = string.Empty;

    public int Seconds { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
