using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Buch inkl. Pool-Flags und Puzzle-Anzahl (für die Admin-Bücher-Liste).</summary>
public class BookDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? Rating { get; set; }
    public int? MinElo { get; set; }
    public int? MaxElo { get; set; }
    public string? Tags { get; set; }
    public string? Description { get; set; }
    public bool ForDaily { get; set; }
    public bool ForRandom { get; set; }
    public bool ForBlind { get; set; }
    /// <summary>Art des Buchs (Puzzle/Study) fürs Trainingsziel-Routing.</summary>
    public BookKind Kind { get; set; }
    public int PuzzleCount { get; set; }
    /// <summary>Gruppen-Ids, die dieses Buch als Kurs sehen dürfen (für die Admin-Zuweisung).</summary>
    public List<int> AccessGroupIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Setzt die vollständige Liste der Gruppen, die ein Buch als Kurs sehen dürfen.</summary>
public class SetBookGroupsDto
{
    public List<int> GroupIds { get; set; } = new();
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
    public int? MinElo { get; set; }
    public int? MaxElo { get; set; }
    [MaxLength(200)]
    public string? Tags { get; set; }
    [MaxLength(2000)]
    public string? Description { get; set; }
    public bool? ForDaily { get; set; }
    public bool? ForRandom { get; set; }
    public bool? ForBlind { get; set; }
    /// <summary>Art des Buchs (Puzzle/Study); fürs Trainingsziel-Routing der Kurszeit.</summary>
    public BookKind? Kind { get; set; }
}

/// <summary>Ergebnis eines PGN-Buch-Imports.</summary>
public class BookImportResultDto
{
    public List<BookImportItemDto> Books { get; set; } = new();
    public int TotalImported { get; set; }
    public int TotalSkipped { get; set; }
    /// <summary>Spiele, die der Parser verworfen hat (kein FEN/Round, keine Mainline, Grundstellung ohne [%tqu]).</summary>
    public int TotalInvalid { get; set; }
}

public class BookImportItemDto
{
    public int BookId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Imported { get; set; }
    /// <summary>Duplikate (LineId schon in DB oder im selben Batch doppelt).</summary>
    public int Skipped { get; set; }
    /// <summary>Spiele, die der Parser verworfen hat (kein FEN/Round, keine Mainline, Grundstellung ohne [%tqu]).</summary>
    public int Invalid { get; set; }
}
