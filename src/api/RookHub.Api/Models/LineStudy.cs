namespace RookHub.Api.Models;

/// <summary>
/// „Ohne Engine analysieren" (0.855.0, Wunsch 2026-10-27: „analyze mit/ohne Engine — ohne soll keine Bewertung zeigen, sondern
/// mich Linien durchklicken lassen; die Linien nur für mich speichern und im Hintergrund analysieren; am Ende einschaltbar,
/// dann zeigt er für alle Linien die Bewertungen"). Ein Zugbaum je Nutzer und Partie (<see cref="GameKey"/>), gespeichert wie
/// der Analyse-Verlauf (<see cref="TreeJson"/> = flache Form <c>AnalysisTreeDto</c>). Jede Stellung darin rechnet der
/// Hintergrund (<see cref="LineStudyPosition"/>); die Zahlen gibt der Server erst nach <see cref="RevealedAt"/> heraus.
/// </summary>
public class LineStudy
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Kennung der Partie, vom Client gebildet (Ausgangsstellung + Züge der Partie, gehasht) — dieselbe Partie aus
    /// „Meine Partien", der Vereinsliste oder einem Teilen-Link landet so im selben Baum.</summary>
    public string GameKey { get; set; } = string.Empty;

    /// <summary>Ausgangsstellung des Baums.</summary>
    public string StartFen { get; set; } = string.Empty;

    /// <summary>Anzeigename („Weiß – Schwarz"), aus den PGN-Kopfdaten.</summary>
    public string? Title { get; set; }

    /// <summary>Der Zugbaum (geprüft wie im Analyse-Verlauf, <c>AnalysisHistoryService.CheckTree</c>).</summary>
    public string TreeJson { get; set; } = "{}";

    /// <summary>Index des Knotens, an dem man stand (-1 = Ausgangsstellung).</summary>
    public int Current { get; set; } = -1;

    /// <summary>Wann die Engine eingeschaltet wurde; <c>null</c> = noch „ohne Engine".</summary>
    public DateTime? RevealedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<LineStudyPosition> Positions { get; set; } = new();
}

public enum LineStudyPositionStatus
{
    /// <summary>Noch kein Auftrag (der Erntelauf legt ihn gedrosselt an).</summary>
    Pending = 0,
    /// <summary>Auftrag läuft (<see cref="LineStudyPosition.AnalysisJobId"/>).</summary>
    Queued = 1,
    /// <summary>Gerechnet (<see cref="LineStudyPosition.CandidatesJson"/>; leer bei Matt/Patt).</summary>
    Done = 2,
    /// <summary>Mehrfach gescheitert — bleibt ohne Bewertung.</summary>
    Failed = 3,
}

/// <summary>Eine Stellung eines <see cref="LineStudy"/>-Baums und ihre Bewertung aus dem Hintergrund.</summary>
public class LineStudyPosition
{
    public int Id { get; set; }

    public int LineStudyId { get; set; }
    public LineStudy? LineStudy { get; set; }

    /// <summary>Brett, Seite am Zug, Rochade, en passant — der Vergleichsschlüssel (eindeutig je Baum).</summary>
    public string PositionKey { get; set; } = string.Empty;

    /// <summary>Volle FEN, so wie sie die Engine bekommt.</summary>
    public string Fen { get; set; } = string.Empty;

    public LineStudyPositionStatus Status { get; set; } = LineStudyPositionStatus.Pending;

    /// <summary>Laufender Auftrag (nur bei <see cref="LineStudyPositionStatus.Queued"/>).</summary>
    public int? AnalysisJobId { get; set; }

    /// <summary>Kandidaten der Engine wie bei <c>GameAnalysisPosition.CandidatesJson</c> (Sicht der Seite am Zug).</summary>
    public string? CandidatesJson { get; set; }

    public int Depth { get; set; }

    /// <summary>Gescheiterte Läufe — ab <c>LineStudyService.MaxAttempts</c> gibt der Erntelauf auf.</summary>
    public int Attempts { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
