namespace RookHub.Api.Models;

/// <summary>Eine Rolle für ALLE Mitglieder einer Gruppe (0.589.0, Wunsch 2026-09-28: „Verein Schwaz soll der Gruppe Schwaz
/// hinzugefügt werden") — wer in die Gruppe kommt, hat die Rechte sofort, wer geht, verliert sie. Die Rechte werden live
/// aufgelöst (<see cref="Services.PermissionResolver"/>), nicht beim Anmelden ins Token geschrieben.</summary>
public class GroupRole
{
    public int GroupId { get; set; }
    public Group? Group { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }
}
