using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Eine Kalkulations-Ausgabe (Verwaltungs- und Betrachter-Sicht).</summary>
public class CalcEditionDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Chapter { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? VideoUrl { get; set; }
    public DateTime PublishAt { get; set; }
    public DateTime? TesterPreviewAt { get; set; }
    /// <summary>Ob die Ausgabe FÜR DEN ABRUFENDEN Betrachter aktuell freigegeben ist (now ≥ Termin).</summary>
    public bool Released { get; set; }
}

/// <summary>Anlegen/Ändern einer Ausgabe (Upsert je Buch+Kapitel).</summary>
public class CalcEditionInputDto
{
    [Required, MaxLength(300)]
    public string Chapter { get; set; } = string.Empty;
    [MaxLength(300)]
    public string? Title { get; set; }
    [MaxLength(500)]
    public string? VideoUrl { get; set; }
    [Required]
    public DateTime PublishAt { get; set; }
    public DateTime? TesterPreviewAt { get; set; }
}
