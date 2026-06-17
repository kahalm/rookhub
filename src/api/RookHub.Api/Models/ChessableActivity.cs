namespace RookHub.Api.Models;

/// <summary>
/// Append-only Zeit-Log für AKTIVE Chessable-Trainingszeit, gemeldet von der RepCheck-Browser-
/// Extension (bzw. dem Userscript) über <c>POST /api/extension/training-activity</c>. Die Extension
/// misst auf chessable.com nur tatsächlich aktive Zeit (Brett vorhanden, Tab sichtbar, kürzliche
/// Zug-/Klick-/XP-Aktivität — kein bloßes Offenlassen) und flusht sie in kleinen Häppchen.
/// Grundlage für die eigene Kategorie „Chessable" im Trainingsziele-Tracker
/// (<see cref="Services.TrainingGoalService"/>); Einzel-Häppchen werden beim Aggregieren gedeckelt.
/// </summary>
public class ChessableActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Aktiv trainierte Zeit dieses Häppchens in Sekunden (gegen Inflation gedeckelt).</summary>
    public int TimeSeconds { get; set; }

    /// <summary>Anzahl in diesem Häppchen abgeschlossener (gewerteter) Züge — informativ.</summary>
    public int MovesTrained { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
