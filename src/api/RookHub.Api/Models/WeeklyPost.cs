using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

/// <summary>
/// Ein „Wochenpost": ein hochgeladenes PGN mit geplantem Termin (Datum + Uhrzeit).
/// Bildet die wöchentlichen Posts des schach-bots auf RookHub ab. Öffentlich sichtbar,
/// Verwaltung (Upload/Bearbeiten/Löschen) nur durch Admins.
/// </summary>
public class WeeklyPost
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Column(TypeName = "LONGTEXT")]
    public string PgnContent { get; set; } = string.Empty;

    public long FileSize { get; set; }

    /// <summary>Geplanter Termin (lokale Wall-Clock-Zeit, wie vom Admin gewählt; Standard-Uhrzeit 19:00).</summary>
    public DateTime ScheduledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
