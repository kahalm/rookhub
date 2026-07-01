namespace RookHub.Api.Models;

/// <summary>
/// Eine vom User auf chessable.com „gemerkte" Stellung (Button „Remember line" in der
/// RepCheck-Extension): die aktuelle FEN samt etwas Kontext (Chessable-Kurs-ID, Seiten-URL).
/// Append-only Sammelbecken — aktuell ohne festen Verwendungszweck (Anzeige/Weiterverarbeitung
/// folgt später). Gespeichert über <c>POST /api/extension/remember-line</c>.
/// </summary>
public class RememberedPosition
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Die gemerkte Stellung als FEN.</summary>
    public string Fen { get; set; } = string.Empty;

    /// <summary>Chessable-Kurs-ID (aus der URL/React-State), falls erkannt.</summary>
    public string? CourseId { get; set; }

    /// <summary>Lesbarer Kursname. Bevorzugt über den Chessable-Bearer aufgelöst (autoritativ:
    /// Extension via Chessable-API bzw. serverseitig aus der gecachten Kursliste des Users);
    /// sonst der von der Extension best-effort aus dem Seiten-DOM gelesene Titel.</summary>
    public string? CourseName { get; set; }

    /// <summary>Seiten-URL, von der gemerkt wurde (Kontext).</summary>
    public string? SourceUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
