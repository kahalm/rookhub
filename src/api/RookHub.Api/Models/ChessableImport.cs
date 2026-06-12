namespace RookHub.Api.Models;

/// <summary>
/// Status-/Fortschrittssatz eines asynchronen Chessable-Kurs-Imports. Der eigentliche Import
/// (Kurs von piratechess holen + als Repertoire oder Buch anlegen) läuft als Hintergrund-Job;
/// das Frontend pollt diesen Satz bis <see cref="Status"/> != "running".
/// </summary>
public class ChessableImport
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Chessable-Buch-ID (bid).</summary>
    public string Bid { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;

    /// <summary>"repertoire" oder "book".</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>"running" | "completed" | "failed".</summary>
    public string Status { get; set; } = "running";
    public string? Error { get; set; }

    /// <summary>Bei Erfolg: RepertoireId bzw. BookId des angelegten Ergebnisses.</summary>
    public int? ResultId { get; set; }

    /// <summary>Importierte Einheiten (Buch: Puzzles; Repertoire: Linien des Kurses).</summary>
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Invalid { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
