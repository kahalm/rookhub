namespace RookHub.Api.DTOs;

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
}
