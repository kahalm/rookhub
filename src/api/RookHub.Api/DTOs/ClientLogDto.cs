namespace RookHub.Api.DTOs;

/// <summary>Client-seitiges Diagnose-Event (z. B. Browser-Engine-Crash/Hänger) für das Logging nach ES/Kibana.</summary>
public class ClientLogDto
{
    /// <summary>Kurzer Event-Schlüssel, z. B. "engine_analysis_crash", "engine_analysis_stall".</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Optionaler Zusatztext (Grund/Kontext).</summary>
    public string? Detail { get; set; }
    /// <summary>Optionaler App-Pfad, auf dem das Event auftrat.</summary>
    public string? Url { get; set; }
}
