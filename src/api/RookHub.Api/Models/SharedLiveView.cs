namespace RookHub.Api.Models;

/// <summary>
/// Geteilte, LEBENDE Live-Ansicht der Turnierseite (1.029.0, Wunsch 2026-11-26: „nicht mit round=2&amp;team=…, sondern einfach
/// einen Hash — wenn ich Bretter ergänze oder entferne, sollen alle mit dem Link das auch sehen“). Der kurze Link
/// <c>/live/{Token}</c> zeigt immer den aktuellen Stand von <see cref="ViewJson"/>; ändert der Besitzer die Ansicht, folgen alle
/// offenen Zuschauer über ihren SSE-Strom. Besitzer = Konto (<see cref="OwnerUserId"/>) ODER wer den Bearbeitungsschlüssel kennt
/// (nur sein Hash steht hier; Gäste teilen so auch). Verfällt nach <c>Retention:LiveViewDays</c> ohne Aufruf.
/// </summary>
public class SharedLiveView
{
    public int Id { get; set; }
    /// <summary>Kurze, unratbare Kennung im Link (10 Zeichen base62, ~59 bit).</summary>
    public string Token { get; set; } = string.Empty;
    /// <summary>chess-results-Turniernummer (wie <c>TournamentBroadcastLink.CrawlerTournamentId</c>).</summary>
    public string CrawlerTournamentId { get; set; } = string.Empty;
    /// <summary>Die Ansicht als JSON (<c>LiveViewState</c>: round, team, player, add, ext, eval) — schon geprüft und gedeckelt.</summary>
    public string ViewJson { get; set; } = "{}";
    /// <summary>Angemeldeter Ersteller (darf ohne Schlüssel ändern); Gäste: <c>null</c>.</summary>
    public int? OwnerUserId { get; set; }
    /// <summary>SHA-256 (hex) des Bearbeitungsschlüssels, den nur der Browser des Erstellers kennt.</summary>
    public string EditKeyHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Zuletzt geöffnet (höchstens stündlich nachgeführt) — Grundlage der Aufbewahrungsfrist.</summary>
    public DateTime LastViewedAt { get; set; } = DateTime.UtcNow;
}
