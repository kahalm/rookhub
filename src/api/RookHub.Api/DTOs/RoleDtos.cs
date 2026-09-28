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
    /// <summary>Gruppen, deren Mitglieder diese Rolle haben (0.589.0).</summary>
    public List<string> Groups { get; set; } = new();
}

/// <summary>Rollen einer Gruppe — alle Mitglieder haben sie (0.589.0).</summary>
public class GroupRolesDto
{
    public int GroupId { get; set; }
    public List<int> RoleIds { get; set; } = new();
}

/// <summary><c>GET /api/auth/permissions</c> — was JETZT gilt (live, nicht der Stand beim Anmelden).</summary>
public class AuthPermissionsDto
{
    public bool IsAdmin { get; set; }
    public List<string> Permissions { get; set; } = new();
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
