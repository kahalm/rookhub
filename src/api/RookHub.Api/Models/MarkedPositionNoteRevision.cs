namespace RookHub.Api.Models;

/// <summary>
/// Eine frühere Fassung der Schlagworte + Bemerkung einer „+"-Stellung (0.862.1, Wunsch 2026-10-27: „merk dir beim Bearbeiten
/// auch die alten, damit ich ggf. Gelöschtes wiederherstellen kann"). Geschrieben, bevor <see cref="MarkedPosition.Tags"/>/
/// <see cref="MarkedPosition.Comment"/> überschrieben werden oder die Markierung entfernt wird — darum am Aufrufer und am
/// <see cref="PositionKey"/> statt an der Markierung: der Verlauf überlebt das Entfernen und kommt beim erneuten Markieren wieder.
/// Je Aufrufer und Stellung höchstens <see cref="Services.Games.MarkedPositionService.MaxRevisions"/> (älteste fallen weg).
/// </summary>
public class MarkedPositionNoteRevision
{
    public int Id { get; set; }

    /// <summary>Konto — oder <c>null</c>, dann <see cref="AnonSessionId"/> (wie <see cref="MarkedPosition"/>).</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }
    public string? AnonSessionId { get; set; }

    public string PositionKey { get; set; } = string.Empty;

    /// <summary>Die alten Schlagworte (<c>;</c>-getrennt) und die alte Bemerkung.</summary>
    public string Tags { get; set; } = string.Empty;
    public string? Comment { get; set; }

    /// <summary>Wann diese Fassung abgelöst wurde.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
