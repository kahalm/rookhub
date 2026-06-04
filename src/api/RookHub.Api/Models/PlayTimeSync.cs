using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Sync-Cursor je User und Plattform für das externe Spielzeit-Tracking. Der
/// <c>PlayTimeSyncService</c> holt nur Partien neuer als <see cref="LastGameTimestamp"/>,
/// damit nichts doppelt gezählt wird. Unique (UserId, Platform).
/// </summary>
public class PlayTimeSync
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>"lichess" | "chesscom".</summary>
    [MaxLength(16)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>Cursor: Zeitstempel (Unix-Millisekunden) der zuletzt verarbeiteten Partie.</summary>
    public long LastGameTimestamp { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Letzter Fehlertext (null = ok) — für Diagnose ohne separates Logging.</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }
}
