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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RepertoireFile> Files { get; set; } = new List<RepertoireFile>();
}
