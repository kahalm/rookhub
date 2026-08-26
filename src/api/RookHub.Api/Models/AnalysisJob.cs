using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public enum AnalysisJobStatus
{
    /// <summary>Wartet auf eine freie Hintergrund-Engine.</summary>
    Queued = 0,
    /// <summary>Stream zum Broker offen, Engine rechnet.</summary>
    Running = 1,
    /// <summary>Unterbrochen (Live-Analyse hat Vorrang, Stream abgerissen, API-Neustart) — wird fortgesetzt.</summary>
    Paused = 2,
    /// <summary>Zieltiefe erreicht (oder Ziel unter das Erreichte gesenkt).</summary>
    Done = 3,
    /// <summary>Endgültig gescheitert (Engine nicht mehr registriert, Token weg).</summary>
    Failed = 4,
}

/// <summary>
/// Hintergrund-Analyseauftrag: „analysiere diese Stellung bis Tiefe N mit K Linien, sobald die
/// Hintergrund-Engine frei ist". Wird vom <c>AnalysisJobWorker</c> über den Lichess-Broker abgearbeitet
/// — pausiert, sobald der Nutzer live über eine externe Engine rechnet, und läuft nach einer Ruhephase
/// weiter. Das Ergebnis ist die letzte Broker-Zeile (<c>{time, depth, nodes, pvs}</c>) als opakes JSON,
/// das das Frontend mit derselben Abbildung wie die Live-Analyse anzeigt.
/// Anpassungs-Regeln (Service): Tiefe ↑ setzt einen fertigen Auftrag wieder in die Queue; Tiefe ↓ unter
/// das Erreichte = fertig; Linien ↓ kürzen das Ergebnis ohne Neustart; Linien ↑ starten die Suche neu,
/// das alte Ergebnis bleibt sichtbar, bis die neue Suche dessen Tiefe überholt.
/// </summary>
public class AnalysisJob
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(120)]
    public string Fen { get; set; } = string.Empty;

    /// <summary>Optionaler Name („Sizilianisch, Kritische Stellung"); Anzeige in der Auftragsliste.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>Lichess-Engine-ID (eei_…), auf der der Auftrag rechnet — beim Anlegen die Hintergrund-Engine.</summary>
    [Required, MaxLength(64)]
    public string EngineId { get; set; } = string.Empty;

    public int TargetDepth { get; set; }
    public int MultiPv { get; set; }

    public AnalysisJobStatus Status { get; set; } = AnalysisJobStatus.Queued;

    /// <summary>Tiefe des gespeicherten Ergebnisses. Eine Fortsetzung übernimmt nur Zeilen ≥ dieser Tiefe.</summary>
    public int ReachedDepth { get; set; }

    /// <summary>Letzte übernommene Broker-Zeile (opak, LONGTEXT) — null bis zur ersten Zeile.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Aufsummierte Rechenzeit über alle Läufe (Sekunden).</summary>
    public int SecondsSpent { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    /// <summary>Frühestens ab hier erneut versuchen (Backoff nach Broker-Fehlern); null = sofort.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Letzter Suchstart — der zuletzt gelaufene Auftrag hat die warme Hashtabelle („sticky").</summary>
    public DateTime? LastRunAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
