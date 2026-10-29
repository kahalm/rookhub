using System.Text.Json;

namespace RookHub.Api.DTOs.Ebooks;

/// <summary>Neuer Brett-Ausschneide-Auftrag (Admin, 0.874.0). <see cref="Payload"/> = bisheriges Auftrags-JSON
/// (<c>diagrams</c>, <c>pages</c>); <see cref="FileId"/> leer = Original-PDF vor abgeleitetem PDF.</summary>
public class BoardCutJobCreateDto
{
    public int EbookId { get; set; }
    public int? FileId { get; set; }
    public JsonElement Payload { get; set; }
}

public class BoardCutJobStatusDto
{
    public Guid JobId { get; set; }
    public int EbookId { get; set; }
    public string State { get; set; } = "";
    public string? Worker { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Ablage des Ergebnisses relativ zu <c>Ebooks:Root</c> (<c>board-cells/&lt;EbookId&gt;</c>).</summary>
    public string ResultPath { get; set; } = "";
}

public class BoardWorkerClaimDto
{
    public string? Worker { get; set; }
}

public class BoardWorkerJobDto
{
    public Guid JobId { get; set; }
    public int EbookId { get; set; }
    public string? Title { get; set; }
    public JsonElement Payload { get; set; }
    public DiagramWorkerFileDto File { get; set; } = new();
    public DateTime LeaseUntil { get; set; }
    public int HeartbeatSeconds { get; set; }
}

public class BoardWorkerReleaseDto
{
    public string? Error { get; set; }
}

public class BoardWorkerResultAcceptedDto
{
    public int EbookId { get; set; }
    public int Crops { get; set; }
    public string ResultPath { get; set; } = "";
}
