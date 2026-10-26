namespace RookHub.Api.DTOs.Games;

/// <summary>Eine mit „+" markierte Stellung (0.749.0).</summary>
public class MarkedPositionDto
{
    public int Id { get; set; }
    public string Fen { get; set; } = string.Empty;
    /// <summary>FEN ohne Zugzähler — daran erkennt die Seite, ob die Stellung auf dem Brett markiert ist.</summary>
    public string PositionKey { get; set; } = string.Empty;
    public string Context { get; set; } = "analysis";
    public int? SavedGameId { get; set; }
    public int? ClubGameId { get; set; }
    public string? ShareToken { get; set; }
    public int? Ply { get; set; }
    public string? BestUci { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>In einer Nebenvariante markiert (0.848.0): Abzweig-Halbzug der Partie und die Züge bis zur Stellung (UCI).</summary>
    public int? VariationFromPly { get; set; }
    public string? VariationUci { get; set; }
}

/// <summary>Stellung markieren. Herkunft optional; eine fremde <c>SavedGameId</c> wird verworfen.</summary>
public class MarkPositionInputDto
{
    public string Fen { get; set; } = string.Empty;
    public string? Context { get; set; }
    public int? SavedGameId { get; set; }
    public int? ClubGameId { get; set; }
    public string? ShareToken { get; set; }
    public int? Ply { get; set; }
    public string? BestUci { get; set; }
    /// <summary>0.848.0: in einer Nebenvariante — Abzweig-Halbzug der Partie (dann ohne <see cref="Ply"/>) und die Züge bis zur
    /// Stellung (UCI, Leerzeichen).</summary>
    public int? VariationFromPly { get; set; }
    public string? VariationUci { get; set; }
}

/// <summary>Anonyme Kennung (<c>X-Visitor-Id</c> der App), deren Markierungen ins Konto wandern (0.847.0).</summary>
public class ClaimMarkedSessionDto
{
    [System.ComponentModel.DataAnnotations.MaxLength(36)] public string? SessionId { get; set; }
}
