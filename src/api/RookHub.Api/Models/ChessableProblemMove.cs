using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Schwierige Züge" einer Chessable-Linie für EINEN User — geerntet aus den Antworten, die die
/// RepCheck-Extension ohnehin mitschneidet (getGame: <c>game.problemMoves.thisUser</c> +
/// <c>lastReviewed</c>; getList: <c>nHard</c> je Linie). Eine Zeile je (User, Kurs-bid,
/// Varianten-oid), per Upsert aktuell gehalten (Training UND „Kurs holen" liefern frische Werte).
/// Verwendungszweck: „nur schwierige Linien"-Filter, Fehlzug-Markierungen im Solver/Review.
/// </summary>
public class ChessableProblemMove
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

    /// <summary>Chessables eigener „schwierige Züge"-Zähler der Linie (aus getList, nHard).
    /// Null = noch nie aus einer Kapitel-Liste gesehen.</summary>
    public int? NHard { get; set; }

    /// <summary>Roh-JSON von <c>game.problemMoves.thisUser</c>: je Ply die falsch gespielten Züge
    /// mit Zähler (inkl. Sondercodes „timeU"/„giveU"). "{}" = zuletzt ohne Fehlzüge gesehen;
    /// null = noch nie ein getGame dieser Linie gesehen. Für den Server OPAK (nur Größe geprüft) —
    /// Struktur gehört Chessable, Auswertung dem Frontend.</summary>
    public string? ProblemMovesJson { get; set; }

    /// <summary>Chessables „lastReviewed" der Linie (UTC); null = nie bzw. unbekannt.</summary>
    public DateTime? LastReviewedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
