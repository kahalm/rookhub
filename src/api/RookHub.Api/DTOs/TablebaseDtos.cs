namespace RookHub.Api.DTOs;

/// <summary>Antwort der Endspiel-Datenbank (0.729.0). <c>status</c>: <c>ok</c> / <c>tooManyPieces</c> (mehr als 7 Steine) /
/// <c>invalid</c> (keine legale FEN) / <c>unavailable</c> (Lichess antwortete nicht) / <c>rateLimited</c> (Lichess drosselt).
/// <c>category</c> aus Sicht der Seite am Zug: win, cursed-win (gewonnen, aber Remis nach der 50-Züge-Regel), maybe-win,
/// draw, maybe-loss, blessed-loss, loss, unknown. <c>moves</c> in der Reihenfolge von Lichess (beste zuerst), ihre
/// Ergebnisse aus Sicht der ziehenden Seite.</summary>
public class TablebaseResultDto
{
    public string Status { get; set; } = "ok";
    public string? Category { get; set; }
    /// <summary>Distance to zeroing (Halbzüge bis zum nächsten Schlag-/Bauernzug in bester Spielweise).</summary>
    public int? Dtz { get; set; }
    /// <summary>Distance to mate (nur wo Lichess es kennt, bis 5 Steine).</summary>
    public int? Dtm { get; set; }
    public bool Checkmate { get; set; }
    public bool Stalemate { get; set; }
    public bool InsufficientMaterial { get; set; }
    public List<TablebaseMoveDto> Moves { get; set; } = new();
}

public class TablebaseMoveDto
{
    public string Uci { get; set; } = string.Empty;
    public string San { get; set; } = string.Empty;
    public string Category { get; set; } = "unknown";
    public int? Dtz { get; set; }
    public int? Dtm { get; set; }
    public bool Zeroing { get; set; }
    public bool Checkmate { get; set; }
    public bool Stalemate { get; set; }
}
