using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Eine einer <see cref="Role"/> zugeordnete Berechtigung (Schlüssel = Konstante aus
/// <see cref="Permissions"/>). Composite-PK (RoleId, Permission).</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    [Required, MaxLength(64)]
    public string Permission { get; set; } = string.Empty;
}
