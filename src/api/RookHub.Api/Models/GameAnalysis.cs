using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public enum GameAnalysisStatus
{
    /// <summary>Stellungen angelegt, noch nicht (vollständig) eingereiht.</summary>
    Pending = 0,
    /// <summary>Mindestens ein Auftrag läuft bzw. wartet.</summary>
    Running = 1,
    /// <summary>Jede Stellung hat ihre Kandidatenliste.</summary>
    Done = 2,
    /// <summary>Abgebrochen — der Fehler steht in <see cref="GameAnalysis.LastError"/>.</summary>
    Failed = 3,
}

/// <summary>
/// Eine GANZE Partie, von der Hintergrund-Engine Stellung für Stellung durchgerechnet — die
/// Vorstufe der Punktepartie (siehe TODO.md) und für sich schon nützlich („diese Partie einmal
/// komplett analysieren" statt Stellung für Stellung von Hand einzureihen).
///
/// <para>Die Analyse selbst läuft über die bestehenden <see cref="AnalysisJob"/>s: derselbe
/// Broker-Pfad, derselbe Vorrang der Live-Analyse, dieselbe Fortsetzungs-Logik. Neu ist nur die
/// Klammer darum — Partie zerlegen, Aufträge in Blöcken nachfüttern, Ergebnisse einsammeln.</para>
///
/// <para><b>Warum die Ergebnisse kopiert werden</b> (in <see cref="GameAnalysisPosition.CandidatesJson"/>)
/// statt auf den Auftrag zu zeigen: <c>AnalysisJobService.MaxJobsPerUser</c> räumt die ÄLTESTEN
/// fertigen Aufträge weg, sobald ein Nutzer über 200 kommt. Eine 80-Halbzug-Partie erzeugt 80
/// Aufträge — ohne Kopie hätte der Trimmer die Analyse nach zweieinhalb Partien wieder aufgefressen.</para>
/// </summary>
public class GameAnalysis
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(200)] public string? Title { get; set; }

    /// <summary>Roh-PGN der Partie (Quelle; die Stellungen liegen daneben in eigenen Zeilen).</summary>
    [Required] public string Pgn { get; set; } = string.Empty;

    [MaxLength(120)] public string? White { get; set; }
    [MaxLength(120)] public string? Black { get; set; }
    [MaxLength(32)]  public string? Result { get; set; }
    [MaxLength(200)] public string? Event { get; set; }

    /// <summary>Startstellung der Partie (PGN-Header <c>[FEN]</c>, sonst die Grundstellung).</summary>
    [Required, MaxLength(120)] public string StartFen { get; set; } = string.Empty;

    public int TargetDepth { get; set; } = GameAnalysisDefaults.TargetDepth;

    /// <summary>Linien je Stellung — höchstens <c>AnalysisJobService.MaxMultiPv</c> (Protokoll-Limit 5).</summary>
    public int MultiPv { get; set; } = GameAnalysisDefaults.MultiPv;

    /// <summary>Engine, auf der gerechnet wird (Lichess <c>eei_…</c>); leer = Hintergrund-Engine des Profils.</summary>
    [MaxLength(64)] public string? EngineId { get; set; }

    public GameAnalysisStatus Status { get; set; } = GameAnalysisStatus.Pending;

    /// <summary>Anzahl der zu analysierenden Halbzüge (= Zeilen in <see cref="Positions"/>).</summary>
    public int PlyCount { get; set; }

    [MaxLength(500)] public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public List<GameAnalysisPosition> Positions { get; set; } = new();
}

/// <summary>Vorgabewerte einer Partie-Analyse — an EINER Stelle, damit Controller, Service und
/// Frontend nicht auseinanderlaufen.</summary>
public static class GameAnalysisDefaults
{
    /// <summary>Tiefe 30: Tiefe 40 kostet grob das Zehnfache (bei 5 Linien Minuten je Iteration) und
    /// macht aus einer Partie ein Nachtprojekt — 30 ist der Kompromiss aus Aussagekraft und Durchsatz.</summary>
    public const int TargetDepth = 30;

    /// <summary>5 = Protokoll-Maximum des Lichess-External-Engine-Protokolls (<c>work.multiPv</c> 1..5).</summary>
    public const int MultiPv = 5;

    /// <summary>Deckel für die Länge einer Partie (Halbzüge) — schützt vor einem PGN-Monster.</summary>
    public const int MaxPlies = 300;

    /// <summary>So viele Aufträge hält eine Partie gleichzeitig offen. Bewusst deutlich unter
    /// <c>AnalysisJobService.MaxOpenJobsPerUser</c> (50), damit daneben noch von Hand eingereiht
    /// werden kann und mehrere Partien nicht gegenseitig verhungern.</summary>
    public const int MaxOpenJobsPerGame = 12;
}
