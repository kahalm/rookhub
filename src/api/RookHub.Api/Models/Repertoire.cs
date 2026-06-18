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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RepertoireFile> Files { get; set; } = new List<RepertoireFile>();
}
