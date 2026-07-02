using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class Repertoire
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsPublic { get; set; }

    /// <summary>Kategorie (z. B. Eroeffnung) — Default <see cref="RepertoireKind.None"/>.</summary>
    public RepertoireKind Kind { get; set; } = RepertoireKind.None;

    /// <summary>
    /// Soll dieses Repertoire von der Browser-Extension/dem Userscript genutzt werden
    /// (Listing + Abweichungsanalyse im Analysemodus)? Default true (bestehendes Verhalten);
    /// per Bearbeiten-Dialog abwaehlbar.
    /// </summary>
    public bool UseForExtension { get; set; } = true;

    /// <summary>
    /// Version der Import-Pipeline (<see cref="Services.ImportPipeline"/>), mit der der Inhalt
    /// dieses Repertoires zuletzt aufbereitet wurde. <c>&lt; CurrentVersion</c> ⇒ „veraltet".
    /// Default 0 = Altbestand. (Repertoires speichern ihr Roh-PGN selbst in
    /// <see cref="RepertoireFile.PgnContent"/> und werten live aus — heute hat das
    /// Neu-Aufbereiten meist keine abgeleiteten Daten zu erneuern; Feld ist zukunftssicher.)
    /// </summary>
    public int ImportVersion { get; set; }

    /// <summary>
    /// Optionale Chessable-Kurs-ID (numerisch als String, z. B. "12345"), die dieses Repertoire
    /// mit einem Chessable-Kurs verknüpft. Ermöglicht der Browser-Extension, beim Training auf
    /// chessable.com automatisch den richtigen <see cref="Kind"/> (Opening/Endgame/…) zu ermitteln.
    /// </summary>
    [MaxLength(32)]
    public string? ChessableCourseId { get; set; }

    /// <summary>Optionaler Override der 9-Stufen-SR-Intervalle NUR für dieses Repertoire — JSON-Array
    /// mit 9 `{ value, unit }`-Einträgen. Null = globale Nutzer-Defaults verwenden.</summary>
    public string? SrIntervalsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RepertoireFile> Files { get; set; } = new List<RepertoireFile>();
}
