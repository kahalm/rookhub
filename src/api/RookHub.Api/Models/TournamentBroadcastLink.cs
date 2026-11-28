namespace RookHub.Api.Models;

/// <summary>
/// Live-Bretter (1.014.0): welche Lichess-Übertragung zu einem geholten chess-results-Turnier gehört. Automatisch
/// gefunden (<c>Manual = false</c>, <c>Tournaments/Live/BroadcastFinder</c>) oder von Hand mit einem Konto verknüpft. Nur
/// Runden einer hier verknüpften Übertragung werden gestreamt. Bewusst ohne Nutzer-Bezug (wer verknüpft hat, steht im Log).
/// </summary>
public class TournamentBroadcastLink
{
    public int Id { get; set; }
    /// <summary>chess-results-Turniernummer (wie <c>TournamentSubscription.CrawlerTournamentId</c>).</summary>
    public string CrawlerTournamentId { get; set; } = string.Empty;
    /// <summary>Kennung des Übertragungs-Turniers: Lichess (8 Zeichen) oder chess.com <c>cc~{event-slug}</c> (1.040.0).</summary>
    public string TourId { get; set; } = string.Empty;
    public string TourName { get; set; } = string.Empty;
    public bool Manual { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
