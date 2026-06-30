using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Persistierter Chessable-Bearer eines RookHub-Users. RookHub speichert den
/// Token verschlüsselt und reicht ihn pro Request an die piratechess-API durch,
/// die die Chessable-Calls (curl-impersonate) tatsächlich ausführt.
/// </summary>
public class ChessableCredential
{
    public int Id { get; set; }

    /// <summary>Besitzer; Cascade-Delete mit dem User. 1:1.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>AES-verschlüsselter Bearer (Base64). Plaintext nie persistiert.</summary>
    [Required]
    public string EncryptedBearer { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gecachte Kursliste (JSON: [{bid,name}]) — damit der User nicht jedes Mal „Kurse laden" muss.</summary>
    public string? CachedCoursesJson { get; set; }
    /// <summary>Zeitpunkt des letzten Kurslisten-Abrufs (für die „Stand"-Anzeige).</summary>
    public DateTime? CoursesCachedAt { get; set; }

    /// <summary>
    /// Circuit-Breaker: gesetzt, sobald Chessable diesen Bearer als endgültig unbrauchbar abgewiesen
    /// hat (Account gesperrt/gelöscht oder Token abgelaufen/ungültig — siehe
    /// <see cref="Services.ChessableBearerBreaker.IsBearerFatal"/>). Solange ≠ null wird der Bearer
    /// für KEINE weitere Chessable-Anfrage verwendet (Importe pausieren, Lese-Endpoints verweigern) —
    /// bis der User/Admin per „Testen" die Gültigkeit bestätigt (erfolgreicher Test ⇒ Feld wird
    /// geleert und pausierte Importe nehmen wieder auf). NICHT bei reinem IP-/Cloudflare-Block gesetzt
    /// (das ist die VPN-Ausgangs-IP, nicht der Bearer).
    /// </summary>
    public DateTime? BlockedAt { get; set; }

    /// <summary>Die Fehlermeldung, die den Circuit-Breaker ausgelöst hat (für die UI-Anzeige).</summary>
    public string? BlockedReason { get; set; }
}
