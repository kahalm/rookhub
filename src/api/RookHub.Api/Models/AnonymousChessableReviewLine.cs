using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Roh abgelegte Chessable-<c>getReview</c>-Antwort EINER Linie eines Users, der (noch) KEINEN
/// RookHub-Token hinterlegt hat — die Extension sendet token-los an einen anonymen Endpoint und
/// identifiziert die Linie über die Chessable-<c>uid</c> (aus dem Chessable-JWT decodiert), NICHT über
/// einen RookHub-Account. Gegenstück zu <see cref="ChessableReviewLine"/> (dort mit <c>UserId</c>).
///
/// <para>Absichtlich eine eigene Tabelle statt <c>UserId</c> nullbar zu machen: der authentifizierte
/// Pfad bleibt sauber (Cascade-FK + Unique je User), und das „Claimen" ist eine klare Move-Operation.
/// Verknüpft ein User später seinen Chessable-Bearer mit RookHub, decodiert der Server dieselbe
/// <c>uid</c> aus dem Bearer und übernimmt die passenden Anon-Zeilen in seinen Account
/// (<see cref="Services.ChessableReviewLineService.ClaimAnonForUidAsync"/>). Kein FK (uid ist kein
/// RookHub-User). Ungeclaimte Zeilen werden per Retention (siehe Aufräum-Job) irgendwann entsorgt.</para>
/// </summary>
public class AnonymousChessableReviewLine
{
    public int Id { get; set; }

    /// <summary>Chessable-User-ID (uid, numerisch als String; aus dem Chessable-JWT <c>user.uid</c>).</summary>
    [Required, MaxLength(32)]
    public string ChessableUid { get; set; } = string.Empty;

    /// <summary>Chessable-Kurs-ID (bid, numerisch als String).</summary>
    [Required, MaxLength(12)]
    public string Bid { get; set; } = string.Empty;

    /// <summary>Chessable-Varianten-ID (oid, numerisch als String).</summary>
    [Required, MaxLength(32)]
    public string Oid { get; set; } = string.Empty;

    /// <summary>Roh-JSON der getReview-Antwort dieser Linie (opak, erst beim Claim/Kurs-Aufbau geparst). LONGTEXT.</summary>
    [Required]
    public string Json { get; set; } = string.Empty;

    /// <summary>Kapiteltitel der Linie (best-effort; nur informativ).</summary>
    [MaxLength(300)]
    public string? ChapterTitle { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
