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

/// <summary>Ein Mitglied des Serien-Verteilers (Verwaltungssicht).</summary>
public class CalcSeriesMemberDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsTester { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Mitglied hinzufügen/ändern (Upsert je Buch+Nutzer, per Benutzername).</summary>
public class CalcSeriesMemberInputDto
{
    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;
    public bool IsTester { get; set; }
}

/// <summary>Ein „Gesehen"-Vermerk (Verwaltungssicht): welches Mitglied welche Ausgabe wann geöffnet hat.</summary>
public class CalcEditionViewDto
{
    public int EditionId { get; set; }
    public string Chapter { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime ViewedAt { get; set; }
}
