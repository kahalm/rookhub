namespace RookHub.Api.DTOs.Ebooks;

// Externe Diagramm-Worker (0.870.0, /api/diagram-worker): ein Rechner außerhalb des Servers (Laptop) holt ein wartendes Buch,
// erkennt die Diagramme mit tools/DiagramCli (derselbe Erkennungscode wie der Server) und schickt das Ergebnis zurück.

/// <summary>Rumpf von <c>POST /api/diagram-worker/claim</c> (optional): Name des Rechners und Version des Erkennungsprogramms,
/// nur fürs Log.</summary>
public class DiagramWorkerClaimDto
{
    public string? Worker { get; set; }
    public string? CliVersion { get; set; }
}

/// <summary>Ein übernommenes Buch.</summary>
public class DiagramWorkerJobDto
{
    /// <summary>Kennung der Leihe — alle weiteren Aufrufe laufen über sie.</summary>
    public Guid JobId { get; set; }
    public int EbookId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Ergänzen statt ersetzen: das Programm mit <c>--supplement</c> aufrufen (unplausible Lesungen als „unread“ behalten).</summary>
    public bool Supplement { get; set; }
    public int? PageCount { get; set; }
    public DateTime LeaseUntil { get; set; }
    /// <summary>Wie oft der Worker spätestens ein Lebenszeichen schicken soll (Sekunden).</summary>
    public int HeartbeatSeconds { get; set; }
    public List<DiagramWorkerFileDto> Files { get; set; } = new();
}

/// <summary>Eine Datei des Auftrags. <see cref="Role"/>: <c>primary</c> (wird gelesen), <c>reading</c> (leichte Lesefassung
/// zum Rendern von Scans), <c>epub</c> (echtes EPUB zum Verknüpfen).</summary>
public class DiagramWorkerFileDto
{
    public int FileId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>Relativer Pfad zum Herunterladen (mit demselben API-Token, nur solange die Leihe gilt).</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>Rumpf von <c>POST …/jobs/{job}/release</c>: Buch zurückgeben. Mit <see cref="Error"/> nehmen externe Worker es
/// nicht wieder; der Server erkennt es dann selbst.</summary>
public class DiagramWorkerReleaseDto
{
    public string? Error { get; set; }
}

/// <summary>Ergebnis der Erkennung — genau das, was <c>tools/DiagramCli</c> schreibt (<c>POST …/jobs/{job}/result</c>).</summary>
public class DiagramWorkerResultDto
{
    public int EbookId { get; set; }
    /// <summary>Schach-Font oder Scan-/Bild-Bretter gefunden (sonst „Unsupported“).</summary>
    public bool FontFound { get; set; }
    public int Pages { get; set; }
    public int Solutions { get; set; }
    public string? CliVersion { get; set; }
    public List<DiagramWorkerDiagramDto> Diagrams { get; set; } = new();
}

/// <summary>Ein erkanntes Diagramm (Felder wie <c>EbookDiagram</c>, ohne Ids und Prüfergebnisse).</summary>
public class DiagramWorkerDiagramDto
{
    public int Page { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Caption { get; set; }
    public bool SideKnown { get; set; }
    public string? ImageRef { get; set; }
    public float? Confidence { get; set; }
    public int? EpubChapter { get; set; }
    public string? FontFamily { get; set; }
    public string? SolutionUci { get; set; }
    public string? SolutionSan { get; set; }
    public string? SolutionText { get; set; }
    public int? SolutionPage { get; set; }
    public string? SolutionSource { get; set; }
}

/// <summary>Antwort auf ein angenommenes Ergebnis.</summary>
public class DiagramWorkerResultAcceptedDto
{
    public int EbookId { get; set; }
    public string State { get; set; } = string.Empty;
    public int DiagramCount { get; set; }
}
