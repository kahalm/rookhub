using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Roh abgelegte Chessable-<c>getReview</c>-Antwort EINER Linie (eine je Kurs-bid + Varianten-oid),
/// von der RepCheck-Extension beim Training mitgeschnitten. <c>getReview</c> ist die zweite Linien-
/// Quelle neben <c>getGame</c>: fast so reich (Hauptzüge, Alternativen, Kommentare, Pfeile/Kreise,
/// Schlüsselzüge) und kommt beim TRAINING sogar dann, wenn die SPA gar kein <c>getGame</c> anfragt.
///
/// <para>Bewusst NUR roh gespeichert (das <see cref="Json"/> wird beim Empfang NICHT geparst) — der
/// Aufbau zum Kurs (JSON → <see cref="Services.ChessableReviewParser"/> → PGN → BookPuzzle) passiert
/// erst beim Kurs-Aufbau als FALLBACK (getGame gewinnt, sonst getReview). Upsert je (User, bid, oid),
/// letzter Stand gewinnt.</para>
/// </summary>
public class ChessableReviewLine
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Chessable-Kurs-ID (bid, numerisch als String).</summary>
    [Required, MaxLength(12)]
    public string Bid { get; set; } = string.Empty;

    /// <summary>Chessable-Varianten-ID (oid, numerisch als String).</summary>
    [Required, MaxLength(32)]
    public string Oid { get; set; } = string.Empty;

    /// <summary>Roh-JSON der getReview-Antwort dieser Linie (für den Server OPAK, erst beim Kurs-Aufbau
    /// geparst). LONGTEXT.</summary>
    [Required]
    public string Json { get; set; } = string.Empty;

    /// <summary>Kapiteltitel der Linie (best-effort aus der Antwort mitgegeben; nur informativ).</summary>
    [MaxLength(300)]
    public string? ChapterTitle { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
