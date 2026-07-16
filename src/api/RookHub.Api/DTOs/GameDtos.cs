using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Eingabe für „Partie speichern" (<c>POST /api/extension/games</c>).
/// Die Extension schickt die SAN-Zugliste der aktuellen Partie plus Best-Effort-Metadaten;
/// der Server baut daraus das PGN. Zeitstempel/ShareToken werden serverseitig gesetzt.</summary>
public class SaveGameInputDto
{
    /// <summary>Herkunft: <c>chess.com</c> oder <c>lichess</c>.</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = string.Empty;

    /// <summary>SAN-Zugliste der Hauptlinie (z. B. <c>["e4","e5","Nf3"]</c>).</summary>
    [Required]
    public List<string> Moves { get; set; } = new();

    [MaxLength(120)]
    public string? ExternalId { get; set; }

    [MaxLength(120)]
    public string? White { get; set; }

    [MaxLength(120)]
    public string? Black { get; set; }

    [MaxLength(12)]
    public string? Result { get; set; }

    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    public DateTime? PlayedAt { get; set; }

    /// <summary>Elo/Rating des Weißspielers auf der Plattform (Best-Effort von der Extension gelesen).</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Elo/Rating des Schwarzspielers auf der Plattform.</summary>
    public int? BlackElo { get; set; }
}

/// <summary>Listeneintrag einer gespeicherten Partie (ohne PGN, für die Übersicht).</summary>
public class SavedGameDto
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public DateTime? PlayedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public int MoveCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Detail einer gespeicherten Partie inkl. PGN (Besitzer; zum Nachspielen/Analysieren).</summary>
public class SavedGameDetailDto : SavedGameDto
{
    public string Pgn { get; set; } = string.Empty;

    /// <summary>Elo/Rating des Weißspielers (aus dem PGN-Header <c>WhiteElo</c> gelesen).</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Elo/Rating des Schwarzspielers (aus dem PGN-Header <c>BlackElo</c> gelesen).</summary>
    public int? BlackElo { get; set; }
}

/// <summary>Öffentliche Sicht auf eine geteilte Partie (<c>GET /api/games/shared/{token}</c>).
/// Enthält bewusst keine User-/Besitzer-Daten.</summary>
public class SharedGameDto
{
    public string Source { get; set; } = string.Empty;
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public DateTime? PlayedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string Pgn { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Elo/Rating des Weißspielers (aus dem PGN-Header <c>WhiteElo</c> gelesen).</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Elo/Rating des Schwarzspielers (aus dem PGN-Header <c>BlackElo</c> gelesen).</summary>
    public int? BlackElo { get; set; }

    /// <summary>"white"/"black", wenn der TEILENDE Besitzer über seinen hinterlegten Plattform-
    /// Username (lichess bzw. chess.com, je nach Quelle) einer Seite zuordenbar ist; sonst null.
    /// Steuert die Brett-Orientierung der öffentlichen /g/-Seite + des OG-Vorschaubilds
    /// (Partie aus der Sicht des Teilenden). Kein zusätzlicher Identitäts-Leak — die
    /// Spielernamen stehen ohnehin im DTO.</summary>
    public string? OwnerSide { get; set; }
}
