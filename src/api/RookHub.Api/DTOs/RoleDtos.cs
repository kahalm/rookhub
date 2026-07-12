using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Eine Rolle inkl. ihrer Permissions + Mitgliederzahl (Admin-Rollenübersicht).</summary>
public class RoleDto
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = new();
    public int MemberCount { get; set; }
}

/// <summary>Anlegen einer neuen Rolle.</summary>
public class CreateRoleDto
{
    [Required, MaxLength(50), RegularExpression(@"^[a-z][a-z0-9._-]{1,49}$",
        ErrorMessage = "Key: Kleinbuchstaben/Ziffern/._- , beginnt mit Buchstabe.")]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();
}

/// <summary>Bearbeiten von Name + Permission-Menge einer Rolle (Key ist unveränderlich).</summary>
public class UpdateRoleDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();
}

/// <summary>Setzt die (Nicht-Admin-)Rollen eines Users auf genau diese Menge.</summary>
public class SetUserRolesDto
{
    public List<int> RoleIds { get; set; } = new();
}

/// <summary>Rollen eines Users für die Zuweisungs-UI.</summary>
public class UserRolesDto
{
    public int UserId { get; set; }
    public List<int> RoleIds { get; set; } = new();
}
