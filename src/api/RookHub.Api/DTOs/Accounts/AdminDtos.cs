namespace RookHub.Api.DTOs.Accounts;

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

    /// <summary>Optionaler Grund (P-4): steht nur im Protokoll des Servers (Zeile „AdminLock“), nicht in der Datenbank
    /// und nicht beim Nutzer; gekürzt auf 200 Zeichen (`AdminService.LockReasonMaxLength`).</summary>
    public string? Reason { get; set; }
}

/// <summary>Antwort von <c>POST /api/admin/users/{id}/password-reset-link</c>: einmaliger Reset-Link und sein Ablauf (UTC).</summary>
public class PasswordResetLinkDto
{
    public string Link { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Reihenfolge der Admin-Nutzerliste (<c>GET /api/admin/users?sort=</c>): Vorgabe nach Id, <c>createdDesc</c> die
/// neuesten Konten zuerst (Mitglieder-Vorschläge im Gruppen-Tab, F5-010).</summary>
public enum AdminUserSort
{
    Id,
    CreatedDesc,
}
