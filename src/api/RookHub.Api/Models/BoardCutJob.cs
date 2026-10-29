namespace RookHub.Api.Models;

/// <summary>
/// Brett-Ausschneide-Auftrag (0.874.0): Seiten eines E-Books rendern, die Diagramme als Bretter ausschneiden und in 64 Felder
/// zerlegen — der Rohstoff der Opus-Lese-Schleife (<c>ebook-board-training/fs</c>). Vorher lagen die Aufträge als Dateien im
/// WebDAV-Bereich; jetzt holt sie ein externer Worker (<c>tools/DiagramWorker</c>) mit seinem RookHub-Token über
/// <c>/api/board-worker</c>. <see cref="PayloadJson"/> ist das bisherige Auftrags-JSON (Seiten → Diagramme), der Server liest
/// daraus nichts; das Ergebnis (<c>cells.npz</c>, <c>prep.json</c>, <c>crops/</c>) landet unter
/// <c>&lt;Ebooks:Root&gt;/board-cells/&lt;EbookId&gt;/</c>.
/// </summary>
public class BoardCutJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;
    /// <summary>Zu rendernde Datei; null = Original-PDF vor abgeleitetem PDF (wie <c>prep.book_file</c>).</summary>
    public int? FileId { get; set; }
    /// <summary>Auftrags-JSON wie bisher: <c>{ "diagrams": n, "pages": { "&lt;Seite&gt;": [[id,x,y,w,h,fen,conf], …] } }</c>.</summary>
    public string PayloadJson { get; set; } = "{}";
    public BoardCutState State { get; set; } = BoardCutState.Pending;
    /// <summary>Leihe wie bei den Diagramm-Workern: <c>pat:&lt;Token-Id&gt;</c> bzw. <c>user:&lt;Id&gt;</c>, 30 min, per Lebenszeichen verlängert.</summary>
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseUntil { get; set; }
    /// <summary>Selbstgewählter Name des Rechners (nur Protokoll/Anzeige).</summary>
    public string? Worker { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}

public enum BoardCutState
{
    Pending = 0,
    Done = 1,
    Failed = 2,
    /// <summary>Durch einen neueren Auftrag für dasselbe Buch ersetzt oder vom Admin zurückgezogen.</summary>
    Cancelled = 3,
}
