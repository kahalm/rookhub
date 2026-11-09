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
    /// <summary>Zugvergleich einer Vereinspartie (0.908.0, <see cref="Services.Engines.ClubMistakeComparisons"/>): angelegt,
    /// aber noch kein Auftrag — die Pumpe startet höchstens wenige gleichzeitig, als Stapel-Hintergrundarbeit.</summary>
    Waiting = 5,
}

/// <summary>Woher die Zahlen eines Vergleichs stammen (Wunsch 2026-11-09: „wenn du die Partie tief analysiert hast, warum die
/// Fehler nochmals eigens analysieren?"). <c>null</c> in der Spalte = vor dieser Unterscheidung angelegt und noch nicht geprüft.
/// Regeln: <see cref="Services.Engines.MoveComparisonFromAnalysis"/>.</summary>
public enum MoveComparisonSource
{
    /// <summary>Alles über eigene Engine-Aufträge (der Weg bis 0.937, und jeder selbst angelegte Vergleich).</summary>
    Engine = 0,
    /// <summary>Kandidaten und Antwort-Tests komplett aus der vertieften Partie-Analyse — kein einziger Auftrag.</summary>
    Analysis = 1,
    /// <summary>Ein Teil aus der Analyse, der Rest (meist ein, zwei Antwort-Tests) über Aufträge.</summary>
    Mixed = 2,
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
    /// <summary>Die Antwort ginge zwar, ist nach dem besten Zug aber ein ANDERER Zug (0.908.0): andere Figur auf dem Startfeld,
    /// oder sie schlug nach dem schwächeren Zug etwas und schlägt hier nichts — ohne Auftrag, nicht gezeigt, nicht erklärt.
    /// Regel: <see cref="Services.Engines.MoveComparisonService.ReplyTransfers"/>.</summary>
    Skipped = 4,
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

    /// <summary>Besitzer. Bei einem Vergleich einer Vereinspartie (<see cref="GameAnalysisId"/> gesetzt) der Besitzer der
    /// Analyse (das Haus-Engine-Konto) — er sieht ihn trotzdem nicht in seiner Liste, und er zählt nicht in seine Deckel.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Automatischer Vergleich „Fehlzug gegen besten Zug" zu einem Fehler dieser Analyse einer Vereinspartie (0.908.0,
    /// <see cref="Services.Engines.ClubMistakeComparisons"/>); <c>null</c> = ein Vergleich, den ein Nutzer selbst angelegt hat.
    /// Lesen darf, wer die Vereinspartie sieht; die Zeile geht mit der Analyse (Cascade) bzw. beim Korrigieren der Züge.</summary>
    public int? GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }

    /// <summary>Halbzug des Fehlers in der Partie (0 = erster Zug) — nur mit <see cref="GameAnalysisId"/>.</summary>
    public int? Ply { get; set; }

    [Required, MaxLength(120)]
    public string Fen { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Title { get; set; }

    public int Depth { get; set; }

    /// <summary>Sprache der Begründungen (Oberfläche beim Anlegen).</summary>
    [Required, MaxLength(8)]
    public string Language { get; set; } = "en";

    public MoveComparisonStatus Status { get; set; } = MoveComparisonStatus.Candidates;

    /// <summary>Herkunft der Zahlen (<see cref="MoveComparisonSource"/>); <c>null</c> = Altbestand, noch nicht geprüft — die
    /// Pumpe sieht bei wartenden Vergleichen einer Vereinspartie dann erst in der Analyse nach.</summary>
    public MoveComparisonSource? Source { get; set; }

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
