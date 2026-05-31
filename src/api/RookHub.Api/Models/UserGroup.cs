namespace RookHub.Api.Models;

/// <summary>Join-Entität für die n:m-Beziehung zwischen <see cref="AppUser"/> und <see cref="Group"/>.</summary>
public class UserGroup
{
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }
}
