namespace RookHub.Api.Models;

/// <summary>Join-Entität für die n:m-Beziehung zwischen <see cref="AppUser"/> und <see cref="Role"/>.</summary>
public class UserRole
{
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }
}
