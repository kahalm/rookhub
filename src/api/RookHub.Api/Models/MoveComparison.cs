using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public enum MoveComparisonStatus
{
    /// <summary>Die Kandidatenzüge werden gerechnet (Stellung nach jedem Zug, drei Linien).</summary>
    Candidates = 0,
    /// <summary>Die besten Antworten auf die schwächeren Züge werden gegen den besten gerechnet.</summary>
    Replies = 1,
    /// <summary>Alles gerechnet, das Sprachmodell schreibt die Begründungen.</summary>
    Explaining = 2,
    Done = 3,
    Failed = 4,
}

public enum MoveComparisonLineKind
{
    /// <summary>Stellung nach einem Kandidatenzug.</summary>
    Candidate = 0,
    /// <summary>Eine der besten Antworten auf einen SCHWÄCHEREN Kandidaten, gespielt nach dem BESTEN.</summary>
    Reply = 1,
}

public enum MoveComparisonLineState
{
    Pending = 0,
    Done = 1,
    Failed = 2,
    /// <summary>Die Antwort geht nach dem besten Zug gar nicht — selbst schon eine Auskunft.</summary>
    Illegal = 3,
}

/// <summary>
/// „Züge vergleichen" (0.602.0, Wunsch 2026-09-29: „soll diese durchrechnen und schaun, warum Zug 1 besser ist als
/// Zug 2 — vor allem im Vergleich: was sind bei Zug 2 die besten Züge, und warum gehen die bei Zug 1 nicht so gut").
/// Eine Stellung, 2–4 Kandidatenzüge; gerechnet über die Hintergrund-Aufträge (eigene oder Haus-Engine), erklärt vom
/// Sprachmodell auf eigener Hardware. Ablauf in <see cref="Services.MoveComparisonService"/>.
/// </summary>
public class MoveComparison
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(120)]
    public string Fen { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Title { get; set; }

    public int Depth { get; set; }

    /// <summary>Sprache der Begründungen (Oberfläche beim Anlegen).</summary>
    [Required, MaxLength(8)]
    public string Language { get; set; } = "en";

    public MoveComparisonStatus Status { get; set; } = MoveComparisonStatus.Candidates;

    /// <summary>Wessen Engine rechnet (<c>null</c> = der Nutzer selbst) — wie <see cref="AnalysisJob.EngineOwnerUserId"/>.</summary>
    public int? EngineOwnerUserId { get; set; }

    /// <summary>Der beste Kandidat (UCI), sobald die Kandidaten gerechnet sind.</summary>
    [MaxLength(10)]
    public string? BestUci { get; set; }

    [MaxLength(80)]
    public string? Model { get; set; }

    [MaxLength(200)]
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public List<MoveComparisonLine> Lines { get; set; } = new();
}

/// <summary>Eine gerechnete (oder zu rechnende) Stellung des Vergleichs.</summary>
public class MoveComparisonLine
{
    public int Id { get; set; }

    public int MoveComparisonId { get; set; }
    public MoveComparison? Comparison { get; set; }

    public MoveComparisonLineKind Kind { get; set; }

    /// <summary>Candidate: der Kandidatenzug. Reply: der SCHWÄCHERE Kandidat, zu dessen besten Antworten die Zeile gehört.</summary>
    [Required, MaxLength(10)]
    public string CandidateUci { get; set; } = string.Empty;

    /// <summary>Reply: die Antwort des Gegners, gespielt nach dem BESTEN Kandidaten.</summary>
    [MaxLength(10)]
    public string? ReplyUci { get; set; }

    /// <summary>Candidate: Reihenfolge der Auswahl. Reply: Rang der Antwort nach dem schwächeren Zug (0 = beste).</summary>
    public int Ordinal { get; set; }

    /// <summary>Die gerechnete Stellung — leer bei <see cref="MoveComparisonLineState.Illegal"/>.</summary>
    [MaxLength(120)]
    public string Fen { get; set; } = string.Empty;

    public MoveComparisonLineState State { get; set; } = MoveComparisonLineState.Pending;

    /// <summary>Der laufende Auftrag (kein FK). Fertig gerechnet wird das Ergebnis hierher kopiert und der Auftrag
    /// gelöscht — sonst stünde er in der Auftragsliste und fiele irgendwann dem Trimmer zum Opfer.</summary>
    public int? AnalysisJobId { get; set; }

    /// <summary>Letzte Broker-Zeile des Auftrags (opak, Weiß-Sicht wie überall).</summary>
    public string? ResultJson { get; set; }

    public int ReachedDepth { get; set; }

    /// <summary>Candidate (nicht der beste): warum der beste Zug besser ist als dieser — englische SAN, die
    /// Figurenbuchstaben der Sprache setzt erst das Lesen.</summary>
    [MaxLength(1500)]
    public string? Explanation { get; set; }
}
