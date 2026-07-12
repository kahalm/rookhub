using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine Rolle = frei bestückbares DB-Datum, das eine Menge von <see cref="Permissions"/> bündelt.
/// <see cref="IsSystem"/>-Rollen („admin"/„member") werden geseedet und dürfen nicht gelöscht werden.
/// Nutzer sind über <see cref="UserRole"/> n:m mit Rollen verknüpft; Rollen tragen ihre Permissions
/// als <see cref="RolePermission"/>-Zeilen (Schlüssel = Konstanten aus <see cref="Permissions"/>).
/// </summary>
public class Role
{
    public int Id { get; set; }

    /// <summary>Stabiler, maschinenlesbarer Schlüssel (z. B. „admin", „member", „trainer"). Unique.</summary>
    [Required, MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Anzeigename für die Admin-UI.</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Geseedete System-Rolle → nicht löschbar/umbenennbar über die Admin-UI.</summary>
    public bool IsSystem { get; set; }

    public ICollection<UserRole> Users { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}
