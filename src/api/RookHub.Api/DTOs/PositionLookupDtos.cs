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

// ===== Baummodus: dieselben Treffer als zusammengeführter Zugbaum =====

/// <summary>Anfrage des Baummodus: FEN der Stellung + gewünschte Halbzug-Tiefe (0 = Server-Default).</summary>
public class PositionTreeRequestDto
{
    public string Fen { get; set; } = string.Empty;
    public int MaxDepth { get; set; }
}

/// <summary>Antwort des Baummodus: je Repertoire die ab der gesuchten Stellung möglichen
/// Fortsetzungen, über alle Linien/Varianten zusammengeführt.</summary>
public class PositionTreeResultDto
{
    public List<RepertoirePositionTreeDto> Repertoires { get; set; } = new();
}

public class RepertoirePositionTreeDto
{
    public int RepertoireId { get; set; }
    public string RepertoireName { get; set; } = string.Empty;
    /// <summary>Enum-Name des <see cref="Models.RepertoireKind"/> (None/Opening/Middlegame/Endgame).</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary><c>true</c>, wenn dieses Repertoire mit dem User geteilt wurde (nicht sein eigenes).</summary>
    public bool Shared { get; set; }
    /// <summary>Zugalternativen direkt ab der gesuchten Stellung (Wurzelebene des Baums).</summary>
    public List<PositionTreeNodeDto> Moves { get; set; } = new();
    /// <summary>Wie viele Linien-Vorkommen (Hauptlinie + Varianten) die Stellung enthalten.</summary>
    public int Occurrences { get; set; }
    /// <summary><c>true</c>, wenn der Baum an der Knoten-Obergrenze abgeschnitten wurde
    /// (Frontend zeigt dann einen Hinweis statt Vollständigkeit zu suggerieren).</summary>
    public bool Truncated { get; set; }
}

/// <summary>Ein Zug im Baum. <see cref="Children"/> sind die im Repertoire folgenden Antworten.</summary>
public class PositionTreeNodeDto
{
    /// <summary>SAN ohne Schach-/Bewertungszeichen (der PGN-Tokenizer strippt <c>+#!?</c>).</summary>
    public string San { get; set; } = string.Empty;
    /// <summary>Anzahl Linien-Pfade, die durch diesen Zug laufen (Verzweigungs-Gewicht).</summary>
    public int Count { get; set; }
    /// <summary><c>true</c>, wenn hier mindestens eine Linie endet.</summary>
    public bool IsEnd { get; set; }
    /// <summary>Kapitel/Linie/Index — nur gesetzt, wenn ab hier GENAU EINE Linie durchläuft
    /// (dann kann das Frontend „Trainieren"/„Ansehen" wie in der Listenansicht anbieten).</summary>
    public string? Chapter { get; set; }
    public string? LineName { get; set; }
    public int? GameIndex { get; set; }
    public List<PositionTreeNodeDto> Children { get; set; } = new();
}
