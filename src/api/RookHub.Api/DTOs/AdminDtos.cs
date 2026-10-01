namespace RookHub.Api.DTOs;

public class AdminUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Namen der Gruppen, in denen der User Mitglied ist.</summary>
    public List<string> Groups { get; set; } = new();
    /// <summary>Gesperrt bis (UTC), nur solange die Sperre läuft; <c>null</c> = nicht gesperrt. Eine unbefristete
    /// Sperre steht als 9999-12-31 da (<see cref="Models.AppUser.LockedIndefinitely"/>).</summary>
    public DateTime? LockedUntil { get; set; }
}

/// <summary>Body von <c>PUT /api/admin/users/{id}/admin</c>: Soll-Wert des Admin-Rechts (F5-013). Nullable, damit ein
/// fehlendes Feld nicht still als <c>false</c> (Entzug) gelesen wird.</summary>
public class SetAdminDto
{
    public bool? IsAdmin { get; set; }
}

/// <summary>Body von <c>POST /api/admin/users/{id}/lock</c>: Sperrende (UTC); <c>null</c> = unbefristet.</summary>
public class LockUserDto
{
    public DateTime? Until { get; set; }
}
