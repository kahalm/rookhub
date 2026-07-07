using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Benutzergruppe. User können einer oder mehreren Gruppen zugeordnet werden;
/// spätere Features können Anzeige/Logik von der Gruppenzugehörigkeit abhängig machen.
/// </summary>
public class Group
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// System-Gruppe „Everyone": JEDER Nutzer ist implizit (ohne <see cref="UserGroup"/>-Zeile)
    /// Mitglied. Genau eine solche Gruppe existiert (per <c>AdminSeeder</c> angelegt); sie kann nicht
    /// gelöscht werden und ihre Mitgliedschaft nicht manuell verändert werden. Buch-/Kurs-Freigaben an
    /// diese Gruppe (bzw. gruppen-gegatete Menüpunkte) gelten damit für alle. Die Zugriffs-Checks in
    /// <c>CourseService</c>/<c>MenuVisibilityService</c> behandeln diese GroupId als universell.
    /// </summary>
    public bool IsEveryone { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<UserGroup> Members { get; set; } = new();
}
