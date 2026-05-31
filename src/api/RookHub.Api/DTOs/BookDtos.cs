using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Buch inkl. Pool-Flags und Puzzle-Anzahl (für die Admin-Bücher-Liste).</summary>
public class BookDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? Rating { get; set; }
    public string? Tags { get; set; }
    public string? Description { get; set; }
    public bool ForDaily { get; set; }
    public bool ForRandom { get; set; }
    public bool ForBlind { get; set; }
    public int PuzzleCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Editierbare Felder eines Buchs (Pool-Flags + Metadaten).</summary>
public class UpdateBookDto
{
    [MaxLength(200)]
    public string? DisplayName { get; set; }
    [MaxLength(50)]
    public string? Difficulty { get; set; }
    [Range(1, 10)]
    public int? Rating { get; set; }
    [MaxLength(200)]
    public string? Tags { get; set; }
    [MaxLength(2000)]
    public string? Description { get; set; }
    public bool? ForDaily { get; set; }
    public bool? ForRandom { get; set; }
    public bool? ForBlind { get; set; }
}

/// <summary>Ergebnis eines PGN-Buch-Imports.</summary>
public class BookImportResultDto
{
    public List<BookImportItemDto> Books { get; set; } = new();
    public int TotalImported { get; set; }
    public int TotalSkipped { get; set; }
}

public class BookImportItemDto
{
    public int BookId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Skipped { get; set; }
}
