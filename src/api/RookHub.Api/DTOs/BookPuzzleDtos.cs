namespace RookHub.Api.DTOs;

public class RecordBookAttemptDto
{
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
}

public class RecordAnonymousBookAttemptDto : RecordBookAttemptDto
{
    /// <summary>Anonyme Geräte-/Sitzungs-ID (gleiche wie bei Standard-Puzzle/Endless).</summary>
    public string SessionId { get; set; } = string.Empty;
}

public class ClaimBookSessionDto
{
    public string SessionId { get; set; } = string.Empty;
}

public class BookSolverDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    public int TimeSeconds { get; set; }
}

public class BookPuzzleResultsDto
{
    /// <summary>Anzahl eingeloggter (namentlicher) Löser.</summary>
    public int SolvedCount { get; set; }
    /// <summary>Anzahl anonymer Löser (distinct Sessions), die gelöst haben.</summary>
    public int AnonymousSolvedCount { get; set; }
    public int AttemptCount { get; set; }
    public List<BookSolverDto> Solvers { get; set; } = new();
}

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
    /// <summary>Stabile Buch-ID (für gezielte Abfragen, z. B. „Zufallspuzzle aus Buch X").</summary>
    public int? BookId { get; set; }
    public string BookFileName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
    public int PuzzleCount { get; set; }
}
