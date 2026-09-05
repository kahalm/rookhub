namespace RookHub.Api.DTOs;

/// <summary>Anlegen einer Partie-Analyse — PGN plus optionale Abweichungen von den Vorgaben.</summary>
public class CreateGameAnalysisRequest
{
    public string? Pgn { get; set; }
    public string? Title { get; set; }
    /// <summary>Vorgabe 30 (siehe <c>GameAnalysisDefaults</c>); Tiefe 40 kostet grob das Zehnfache.</summary>
    public int? TargetDepth { get; set; }
    /// <summary>Vorgabe 5 = Protokoll-Maximum.</summary>
    public int? MultiPv { get; set; }
    /// <summary>Leer = Hintergrund-Engine aus dem Profil.</summary>
    public string? EngineId { get; set; }
}

public class GameAnalysisDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public string? Event { get; set; }
    public int TargetDepth { get; set; }
    public int MultiPv { get; set; }
    public string? EngineId { get; set; }
    /// <summary>pending · running · done · failed</summary>
    public string Status { get; set; } = "pending";
    public int PlyCount { get; set; }
    /// <summary>Wie viele Stellungen bereits ihre Kandidatenliste haben (Fortschritt).</summary>
    public int AnalyzedPlies { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Nur im Detail-Abruf gefüllt.</summary>
    public List<GameAnalysisPositionDto>? Positions { get; set; }
}

/// <summary>Eine Stellung der Partie. BEWUSST ohne Kandidatenliste: die ist die Grundlage der
/// späteren Punktepartie und bleibt serverseitig — wer sie ausliefert, liefert die Lösung mit.</summary>
public class GameAnalysisPositionDto
{
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public bool White { get; set; }
    public string San { get; set; } = string.Empty;
    public string Uci { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string? EvalText { get; set; }
    public int Depth { get; set; }
    public bool Analyzed { get; set; }
}
