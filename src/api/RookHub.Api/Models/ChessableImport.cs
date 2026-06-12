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

    /// <summary>Feinphase innerhalb "running": "queued" | "fetching" | "importing". Für Anzeige + Resume.</summary>
    public string Phase { get; set; } = "queued";
    public string? Error { get; set; }

    /// <summary>
    /// Das von piratechess geholte Kurs-PGN (Checkpoint): einmal geholt, wird es hier persistiert,
    /// damit ein Resume nach einem Neustart NICHT erneut über die VPN bei Chessable abrufen muss.
    /// Wird nach erfolgreichem Import wieder geleert (Platz sparen).
    /// </summary>
    public string? FetchedPgn { get; set; }

    /// <summary>Anzahl Linien im Kurs (aus dem Fetch; für Repertoire-Ergebnismeldung beim Resume).</summary>
    public int LineCount { get; set; }

    /// <summary>Wie oft der Job (auch via Resume) schon angelaufen ist — begrenzt Endlos-Resumes.</summary>
    public int Attempts { get; set; }

    /// <summary>Bei Erfolg: RepertoireId bzw. BookId des angelegten Ergebnisses.</summary>
    public int? ResultId { get; set; }

    /// <summary>Importierte Einheiten (Buch: Puzzles; Repertoire: Linien des Kurses).</summary>
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Invalid { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
