using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

// --- Response DTOs ---

public class EndlessSyncResponseDto
{
    public EndlessProgressDto? Progress { get; set; }
    public List<EndlessSessionDto> Sessions { get; set; } = new();
}

public class EndlessProgressDto
{
    public int StartElo { get; set; }
    public string Themes { get; set; } = string.Empty;
    public int? FasttrackThreshold1 { get; set; }
    public int? FasttrackThreshold2 { get; set; }
    public int StockfishDepth { get; set; }
    public int Highscore { get; set; }
    public string? ActiveGameState { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EndlessSessionDto
{
    public int Id { get; set; }
    public long Timestamp { get; set; }
    public int TotalSolved { get; set; }
    public int MaxRating { get; set; }
    public int DurationSeconds { get; set; }
    public string ConfigJson { get; set; } = string.Empty;
    public string MistakeAtRatings { get; set; } = string.Empty;
    public string? Seed { get; set; }
    public string? ChainPuzzleIds { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>Spielweise des Laufs ("training"/"easy"); Altbestand liefert "training".</summary>
    public string Mode { get; set; } = Models.SolveMode.Training;
}

/// <summary>Vollständige Detail-Ansicht eines Laufs (History-Klick) inkl. der einzelnen Puzzle-Versuche.</summary>
public class EndlessSessionDetailDto : EndlessSessionDto
{
    public List<EndlessSessionPuzzleDto> Puzzles { get; set; } = new();
}

// --- Request DTOs ---

public class SaveEndlessProgressDto
{
    [Range(0, 5000)]
    public int StartElo { get; set; }

    [MaxLength(200)]
    public string Themes { get; set; } = string.Empty;

    public int? FasttrackThreshold1 { get; set; }
    public int? FasttrackThreshold2 { get; set; }

    [Range(1, 24)]
    public int StockfishDepth { get; set; } = 16;

    [Range(0, 100000)]
    public int Highscore { get; set; }

    [MaxLength(1_000_000)]
    public virtual string? ActiveGameState { get; set; }
}

/// <summary>Anonymer Zwilling von <see cref="SaveEndlessProgressDto"/> mit EIGENEM, engerem Deckel für
/// den Spielstand. Der anonyme Endpoint ist offen und die Session-Id frei wählbar: jede neue Kennung
/// legt eine eigene Zeile an, und <c>ActiveGameState</c> ist LONGTEXT — mit dem Konto-Deckel von 1 Mio.
/// Zeichen füllte ein Skript ohne Konto die gemeinsame Datenbank mit bis zu ~3 MB je Aufruf.</summary>
public class SaveAnonymousProgressDto : SaveEndlessProgressDto
{
    /// <summary>Obergrenze des anonymen Spielstands (Zeichen). Ein echter Stand
    /// (<c>syncActiveGameToServer</c> im Endless-Modus) hat ~300 Zeichen fest plus ~140 je Puzzle des
    /// Laufs (<c>puzzleAttempts</c>) — 64 K reichen für über 400 Puzzles. Die Kette steigt ab Puzzle 25
    /// um 15 Punkte je Puzzle und liegt lange vorher über dem Puzzle-Bestand.</summary>
    public const int MaxActiveGameStateLength = 64 * 1024;

    [MaxLength(MaxActiveGameStateLength)]
    public override string? ActiveGameState
    {
        get => base.ActiveGameState;
        set => base.ActiveGameState = value;
    }

    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string SessionId { get; set; } = string.Empty;
}

public class RecordEndlessSessionDto
{
    public long Timestamp { get; set; }

    [Range(0, 10000)]
    public int TotalSolved { get; set; }

    [Range(0, 100000)]
    public int MaxRating { get; set; }

    [Range(0, 100000)]
    public int DurationSeconds { get; set; }

    [MaxLength(5000)]
    public string ConfigJson { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MistakeAtRatings { get; set; } = string.Empty;

    /// <summary>Eindeutiger Seed des Gauntlet-Laufs (identifiziert die Kette für ein späteres Replay).</summary>
    [MaxLength(64)]
    public string? Seed { get; set; }

    /// <summary>Geordnete Puzzle-IDs der gespielten Kette als CSV (für späteres Replay).</summary>
    [MaxLength(20000)]
    public string? ChainPuzzleIds { get; set; }

    /// <summary>Spielweise des Laufs: "training" (Brett eingefroren bzw. höhere Visualisierungsstufe)
    /// oder "easy" (Figuren normal ziehbar). Fehlt sie oder ist sie unbekannt → "training".</summary>
    /// BEWUSST ohne Längenbegrenzung: ein unbekannter Wert soll auf "training" zurückfallen, nicht
    /// den ganzen Lauf mit 400 abweisen (die Spaltenlänge sichert das Modell).
    public string? Mode { get; set; }

    /// <summary>Optional: einzelne Puzzles der Session — persistiert als PuzzleAttemptsJson (Detail-Ansicht);
    /// geloggt (Start-/Lösungszeit) nur Einträge mit <c>StartedAt</c> &gt; 0.</summary>
    public List<EndlessSessionPuzzleDto> Puzzles { get; set; } = new();
}

/// <summary>Ein einzelnes Puzzle einer Endless-Session (Start-/Lösungszeit als Unix-Millis; 0 = unbekannt,
/// z. B. nach einem Fortsetzen rekonstruiert — dann gespeichert, aber nicht geloggt).</summary>
public class EndlessSessionPuzzleDto
{
    public int PuzzleId { get; set; }
    public string? LichessId { get; set; }
    public int Rating { get; set; }
    public bool Solved { get; set; }
    public long StartedAt { get; set; }
    public long EndedAt { get; set; }
}

public class RecordAnonymousSessionDto : RecordEndlessSessionDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string SessionId { get; set; } = string.Empty;
}

public class BulkImportSessionDto
{
    public List<RecordEndlessSessionDto> Sessions { get; set; } = new();
}

public class BulkImportAnonymousSessionDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string SessionId { get; set; } = string.Empty;

    public List<RecordEndlessSessionDto> Sessions { get; set; } = new();
}

public class ClaimEndlessSessionDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string AnonymousSessionId { get; set; } = string.Empty;
}

public class ArchiveSessionsDto
{
    [Required]
    public List<int> SessionIds { get; set; } = new();

    public bool Archive { get; set; } = true;
}

public class EndlessHistoryResponseDto
{
    public List<EndlessSessionDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>Läufe im Modus „training" über den GANZEN gefilterten Bestand (nicht nur die
    /// aktuelle Seite); Altbestand ohne Modus zählt hier. Zusammen mit <see cref="EasyCount"/>
    /// ergibt das <see cref="TotalCount"/>.</summary>
    public int TrainingCount { get; set; }
    /// <summary>Läufe im Modus „easy" über den ganzen gefilterten Bestand.</summary>
    public int EasyCount { get; set; }
}
