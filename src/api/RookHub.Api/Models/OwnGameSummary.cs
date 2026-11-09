namespace RookHub.Api.Models;

/// <summary>
/// Zusammenfassung einer EIGENEN gespeicherten Partie durch Claude (0.923.0, <see cref="Services.Games.OwnGameSummaries"/>):
/// der Besitzer fordert sie an, nur er sieht sie. Eine Zeile je Partie; dieselben Zustände wie <see cref="ClubGameSummary"/>.
/// </summary>
public class OwnGameSummary
{
    public int Id { get; set; }
    public int SavedGameId { get; set; }
    public int UserId { get; set; }
    public ClubGameSummaryStatus Status { get; set; }
    public string? Markdown { get; set; }
    /// <summary>Zug-Schlüssel beim Abliefern — weicht er später ab, wurde die Partie seither korrigiert.</summary>
    public string? MovesHash { get; set; }
    public string? Model { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? DoneAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>Eine angenommene Anforderung (Kontingent je Konto und UTC-Tag). Eigene Zeile, weil ein neues Anfordern derselben
/// Partie die <see cref="OwnGameSummary"/> überschreibt, aber trotzdem zählt.</summary>
public class OwnGameSummaryRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SavedGameId { get; set; }
    public DateTime At { get; set; }
}
