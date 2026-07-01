namespace RookHub.Api.Models;

/// <summary>
/// Ein vom User fürs Dashboard „angepinnter" Kurs (Buch). Rein persönlich (per User), damit
/// er den Kurs direkt vom Dashboard aus starten kann. Analog zum Turnier-Pinning
/// (<see cref="TournamentUserSetting"/>), aber als eigene schlanke Pin-Tabelle.
/// </summary>
public class CoursePin
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    /// <summary>Zeitpunkt des Anpinnens (für optionale Sortierung nach Anpin-Reihenfolge).</summary>
    public DateTime PinnedAt { get; set; } = DateTime.UtcNow;
}
