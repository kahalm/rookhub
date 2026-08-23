using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Sitzungsergebnis EINER trainierten Chessable-Linie: der rohe <c>moves</c>-Block aus Chessables
/// eigenem Session-Report (<c>POST /api/v1/saveProgressAndReturnNewProgressInfo</c>), den die
/// RepCheck-Extension beim Training mitschneidet. Enthält je Halbzug u. a. die falsch gespielten
/// Züge (<c>wrong</c>), Overstudy/Alternative-Flags, Level und Punkte. APPEND-ONLY (eine Zeile je
/// Linie und Trainingsdurchlauf, KEIN Upsert) — bewusst als Roh-Log gesammelt, Auswertung offen.
/// Für den Server OPAK (nur Form/Größe geprüft) — die Struktur gehört Chessable.
/// </summary>
public class ChessableSessionMove
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

    /// <summary>Rohes JSON-ARRAY der per-Zug-Ergebnisse dieser Linie aus dem Session-Report
    /// (Felder u. a. mid, level, points, wrong[], overstudied, alternativeSkipped, presented).</summary>
    public string MovesJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
