namespace RookHub.Api.DTOs;

public class BookPuzzleDto
{
    public int Id { get; set; }
    public string LineId { get; set; } = string.Empty;
    public string BookFileName { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    /// <summary>Halbzug-Index des Trainingsstarts; lösen ab moves[StartPly+1] (siehe BookPuzzle).</summary>
    public int StartPly { get; set; }
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    public string? Comment { get; set; }
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
}

public class BookPuzzleImportDto
{
    public string LineId { get; set; } = string.Empty;
    public string BookFileName { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    public string? Comment { get; set; }
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
}

public class BookInfoDto
{
    public string BookFileName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
    public int PuzzleCount { get; set; }
}
