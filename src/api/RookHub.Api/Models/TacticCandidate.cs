namespace RookHub.Api.Models;

public enum TacticCandidateStatus
{
    /// <summary>Gefunden, die Lösung wird noch verlängert (ein Auftrag läuft oder steht an).</summary>
    Verifying = 0,
    /// <summary>Lösung fertig, noch nicht im Kurs.</summary>
    Done = 1,
    /// <summary>Im Kurs (<see cref="TacticCandidate.LineId"/>).</summary>
    Published = 2,
    /// <summary>Verworfen (<see cref="TacticCandidate.RejectReason"/>).</summary>
    Rejected = 3,
    /// <summary>Die Zweitprüfung (<c>TacticHarvest:SecondEngineId</c>, z. B. lc0) sieht es anders als die Erstprüfung — nicht
    /// in den Kursen, nur zum Ansehen (<see cref="TacticCandidate.RejectReason"/> nennt den Grund).</summary>
    Disputed = 4,
}

/// <summary>
/// Eine geerntete Taktik (0.657.0, „Taktiken aus Partien ernten"): eine Stellung aus einer fertigen Partie-Analyse, in der
/// der Gegner eben gepatzt hat und es genau EINEN Zug gibt, der das bestraft — Regeln nach dem Lichess-Puzzler
/// (<c>Services/Tactics/TacticHarvest.cs</c>). Die Lösung wird über den Engine-Broker verlängert, solange der beste Zug
/// eindeutig bleibt, und dann als Aufgabe in einen Kurs gelegt. Hängt an der Analyse (Cascade): verschwindet die Partie,
/// verschwindet die Taktik, und der Kurs räumt die Aufgabe beim nächsten Lauf weg.
/// </summary>
public class TacticCandidate
{
    public int Id { get; set; }
    public int GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }
    /// <summary>Halbzug der Aufgabenstellung (0-basiert, wie <see cref="GameAnalysisPosition.Ply"/>).</summary>
    public int Ply { get; set; }
    public GameAnalysisOrigin Origin { get; set; }
    /// <summary>Stellung VOR dem Fehler des Gegners — die Aufgabe beginnt mit ihm (StartPly 0).</summary>
    public string PrevFen { get; set; } = string.Empty;
    public string BlunderUci { get; set; } = string.Empty;
    /// <summary>Die Aufgabenstellung: der Löser am Zug.</summary>
    public string Fen { get; set; } = string.Empty;
    public bool SolverWhite { get; set; }
    /// <summary>Was in der Partie gespielt wurde — gleich dem ersten Lösungszug = „gefunden".</summary>
    public string GameMoveUci { get; set; } = string.Empty;
    public bool Found { get; set; }
    /// <summary><c>mate</c> oder <c>material</c>.</summary>
    public string Kind { get; set; } = "material";
    /// <summary>Bestätigte Lösung (UCI, Leerzeichen, beginnt und endet mit einem Zug des Lösers).</summary>
    public string Moves { get; set; } = string.Empty;
    /// <summary>Antwort des Gegners auf den letzten bestätigten Zug — nach ihr wird geprüft, ob es weitergeht.</summary>
    public string? PendingReplyUci { get; set; }
    /// <summary>Stellung nach <see cref="PendingReplyUci"/> (Löser am Zug), die gerade gerechnet wird.</summary>
    public string? NextFen { get; set; }
    public int? AnalysisJobId { get; set; }
    public int Attempts { get; set; }
    public TacticCandidateStatus Status { get; set; }
    public string? RejectReason { get; set; }
    /// <summary>Themen (CSV, z. B. <c>mateIn2,fork</c>).</summary>
    public string? Themes { get; set; }
    /// <summary>Bewertung nach dem ersten Lösungszug aus Sicht des Lösers (<c>+5.7</c> / <c>#3</c>).</summary>
    public string? EvalText { get; set; }
    /// <summary>Zweitprüfung (Phase 2, lc0): <c>null</c> = noch nicht entschieden (oder aus), <c>true</c> = bestätigt.
    /// Uneinig → <see cref="TacticCandidateStatus.Disputed"/>.</summary>
    public bool? SecondAgrees { get; set; }
    /// <summary>Bester Zug der Zweitprüfung in der Aufgabenstellung (UCI).</summary>
    public string? SecondBest { get; set; }
    /// <summary>Bewertung der Zweitprüfung nach dem ersten Lösungszug (<c>+5.7</c> / <c>#3</c>).</summary>
    public string? SecondEval { get; set; }
    /// <summary>Nächste zu prüfende Stellung der Zweitprüfung: 0 = Aufgabenstellung, 1 = Stellung vor dem Fehler, ab 2 die
    /// späteren Löserzüge.</summary>
    public int SecondStage { get; set; }
    public int? SecondJobId { get; set; }
    public int SecondAttempts { get; set; }
    /// <summary>Kandidaten der Zweitprüfung in der Aufgabenstellung (JSON) — die Stellung davor braucht sie zum Vergleich.</summary>
    public string? SecondHereJson { get; set; }
    /// <summary>Im Kurs: <see cref="BookPuzzle.LineId"/>.</summary>
    public string? LineId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
