namespace RookHub.Api.Models;

/// <summary>
/// Status-/Fortschrittssatz eines asynchronen Chessable-Kurs-Imports. Der eigentliche Import
/// (Kurs von piratechess holen + als Repertoire oder Buch anlegen) läuft als Hintergrund-Job;
/// das Frontend pollt diesen Satz bis <see cref="Status"/> != "running".
/// </summary>
public class ChessableImport
{
    public int Id { get; set; }

    /// <summary>Besitzer des Imports: dem das Ergebnis (Repertoire/Buch) gehört + Benachrichtigung.</summary>
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Optional: User, dessen Chessable-Bearer zum Holen verwendet wird (Admin-Download „im
    /// Namen eines Users"). null ⇒ es wird der Bearer von <see cref="UserId"/> genutzt.</summary>
    public int? BearerUserId { get; set; }

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

    // --- Live-Fortschritt der Hol-Phase (von piratechess gepollt, für die Anzeige) ---
    public int ChaptersDone { get; set; }
    public int ChaptersTotal { get; set; }
    public int LinesDone { get; set; }
    /// <summary>JobId des laufenden piratechess-Fetch-Jobs (zum Weiterpollen, auch nach Resume).</summary>
    public string? FetchJobId { get; set; }

    /// <summary>Wie oft der Job (auch via Resume) schon angelaufen ist — begrenzt Endlos-Resumes.</summary>
    public int Attempts { get; set; }

    /// <summary>Bei Erfolg: RepertoireId bzw. BookId des angelegten Ergebnisses.</summary>
    public int? ResultId { get; set; }

    /// <summary>Importierte Einheiten (Buch: Puzzles; Repertoire: Linien des Kurses).</summary>
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Invalid { get; set; }

    /// <summary>
    /// Round-Robin-„Runde" für die faire Queue-Reihenfolge, bei der Anlage eingefroren: Anzahl der
    /// zu diesem Zeitpunkt bereits aktiven (Status "running") Importe DESSELBEN Users. Der 1. Job
    /// eines Users hat Runde 0, sein 2. Runde 1 usw. Sortiert man wartende Jobs nach (QueueRound,
    /// CreatedAt), rückt der erste Job eines neu hinzukommenden Users direkt hinter den ersten des
    /// bereits wartenden Users — danach wird abgewechselt. Eingefroren ⇒ stabil über Abschlüsse hinweg.
    /// </summary>
    public int QueueRound { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Zeitpunkt, an dem der Job aus der Queue gezogen wurde und das Holen begann
    /// — für die Aufteilung Wartezeit (Created→Started) vs. Holzeit (Started→Completed).
    /// Null solange der Job noch in der Warteschlange liegt.</summary>
    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
