namespace RookHub.Api.DTOs;

/// <summary>Anfrage: „In welchen Repertoire-Linien kommt diese Stellung vor?" — FEN der Stellung.</summary>
public class PositionLookupRequestDto
{
    public string Fen { get; set; } = string.Empty;
}

/// <summary>Antwort: Treffer gruppiert nach Repertoire → Linie (Kapitel/Linienname).</summary>
public class PositionLookupResultDto
{
    public List<RepertoirePositionMatchDto> Repertoires { get; set; } = new();
}

public class RepertoirePositionMatchDto
{
    public int RepertoireId { get; set; }
    public string RepertoireName { get; set; } = string.Empty;
    /// <summary>Enum-Name des <see cref="Models.RepertoireKind"/> (None/Opening/Middlegame/Endgame).</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary><c>true</c>, wenn dieses Repertoire mit dem User geteilt wurde (nicht sein eigenes).</summary>
    public bool Shared { get; set; }
    public List<RepertoireLineMatchDto> Lines { get; set; } = new();
}

public class RepertoireLineMatchDto
{
    /// <summary>Kapitel = PGN-<c>[Black]</c>-Header (Chessable-Konvention). Kann leer sein.</summary>
    public string Chapter { get; set; } = string.Empty;
    /// <summary>Linienname = PGN-<c>[White]</c>-Header. Kann leer sein.</summary>
    public string LineName { get; set; } = string.Empty;
    /// <summary>0-basierter Index der Linie innerhalb des kombinierten Repertoire-PGN
    /// (gleiche Reihenfolge wie <c>GET /api/repertoires/{id}/pgn</c> → <c>parsePgnText</c>).</summary>
    public int GameIndex { get; set; }
    /// <summary>Anzahl Halbzüge bis zur Stellung auf der Hauptlinie (0 = Ausgangsstellung);
    /// <c>-1</c>, wenn die Stellung nur in einer Variante vorkommt.</summary>
    public int Ply { get; set; }
}
