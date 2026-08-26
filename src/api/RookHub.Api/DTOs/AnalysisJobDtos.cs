namespace RookHub.Api.DTOs;

public class CreateAnalysisJobRequest
{
    public string? Fen { get; set; }
    public string? Title { get; set; }
    public int TargetDepth { get; set; } = 30;
    public int MultiPv { get; set; } = 3;
    /// <summary>Engine-ID; fehlend = die im Profil hinterlegte Hintergrund-Engine.</summary>
    public string? EngineId { get; set; }
}

public class UpdateAnalysisJobRequest
{
    public int? TargetDepth { get; set; }
    public int? MultiPv { get; set; }
    public string? Title { get; set; }
}

/// <summary>Auftrag inkl. Ergebnis: <c>ResultJson</c> ist die letzte Broker-Zeile (opak — das Frontend
/// bildet sie mit derselben Logik ab wie den Live-Stream), <c>Status</c> klein geschrieben.</summary>
public record AnalysisJobDto(
    int Id, string Fen, string? Title, string EngineId, int TargetDepth, int MultiPv, string Status,
    int ReachedDepth, string? ResultJson, int SecondsSpent, string? LastError,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? LastRunAt, DateTime? FinishedAt, string? EvalText = null);

public class SetBackgroundEngineRequest
{
    /// <summary>Engine-ID oder null/leer zum Entfernen.</summary>
    public string? EngineId { get; set; }
}

/// <summary>Mehrere Stellungen auf einmal vormerken (Mehrfachauswahl in „Gemerkte Stellungen") — eine Tiefe/Linienzahl für alle.</summary>
public class CreateAnalysisJobsBatchRequest
{
    /// <summary>Nullable, weil <c>"fens": null</c> im JSON den Initializer aushebelt — der Service macht daraus 400 statt 500.</summary>
    public List<string>? Fens { get; set; } = new();
    public int TargetDepth { get; set; } = 30;
    public int MultiPv { get; set; } = 3;
    public string? EngineId { get; set; }
}

/// <summary>Ergebnis des Batch-Anlegens: angelegte Aufträge + übersprungene Stellungen mit Grund
/// (<c>invalid</c> = keine legale FEN, <c>duplicate</c> = es gibt schon einen nicht gescheiterten Auftrag
/// zu dieser Stellung, <c>limit</c> = Deckel offener Aufträge erreicht).</summary>
public record AnalysisJobBatchResult(List<AnalysisJobDto> Created, List<AnalysisJobBatchSkipped> Skipped);
public record AnalysisJobBatchSkipped(string Fen, string Reason);
