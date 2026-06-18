namespace RookHub.Api.Models;

/// <summary>
/// Eine vom User auf chess.com oder lichess.org gespeicherte Partie (Button „Partie speichern"
/// in der RepCheck-Extension). Die Extension schickt die SAN-Zugliste der aktuellen Review-/
/// Analyse-Seite plus Best-Effort-Metadaten; der Server baut daraus ein PGN. Pro Spiel wird ein
/// eindeutiges <see cref="ShareToken"/> erzeugt, über das die Partie öffentlich geteilt werden kann.
/// Gespeichert über <c>POST /api/extension/games</c>.
/// </summary>
public class SavedGame
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Herkunfts-Plattform: <c>chess.com</c> oder <c>lichess</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Partie-ID auf der Plattform (aus der URL), falls erkannt — für Dedup.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Vollständiges PGN (serverseitig aus Zugliste + Metadaten gebaut).</summary>
    public string Pgn { get; set; } = string.Empty;

    public string? White { get; set; }
    public string? Black { get; set; }

    /// <summary>Ergebnis (<c>1-0</c>/<c>0-1</c>/<c>1/2-1/2</c>/<c>*</c>).</summary>
    public string? Result { get; set; }

    /// <summary>Wann die Partie gespielt wurde, falls bekannt.</summary>
    public DateTime? PlayedAt { get; set; }

    /// <summary>Original-URL der Partie (Link zurück zur Plattform).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Eindeutiges URL-sicheres Token für den öffentlichen Teilen-Link (<c>/g/{token}</c>).</summary>
    public string ShareToken { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
