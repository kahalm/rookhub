using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Accounts;

public class GroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Konten der Gruppe — direkte UND über enthaltene Gruppen geerbte, jedes einmal.</summary>
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Direkt enthaltene Gruppen: deren Mitglieder zählen hier mit (2026-10-14).</summary>
    public List<int> IncludedGroupIds { get; set; } = new();
    /// <summary>System-Gruppe „Everyone" (jeder implizit Mitglied; nicht löschbar/mitgliedsänderbar).</summary>
    public bool IsEveryone { get; set; }
}

public class CreateGroupDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? Description { get; set; }
}

public class UpdateGroupDto
{
    [MaxLength(100)]
    public string? Name { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
}

public class GroupMemberDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    /// <summary>Direkt eingetragen (entfernbar). <c>false</c> = nur über <see cref="Via"/> geerbt.</summary>
    public bool Direct { get; set; } = true;
    /// <summary>Die enthaltenen Gruppen, über die das Konto (auch) dazugehört.</summary>
    public List<string> Via { get; set; } = new();
}

/// <summary><c>GET /api/admin/groups/options</c> — eine Gruppe als Auswahl (F5-005-Nachlauf): Kennung, Name, ob es
/// „Everyone" ist, und die Mitgliederzahl (direkt + geerbt). Keine Mitglieder, keine Beschreibung, keine Verschachtelung —
/// die bleiben bei groups.manage.</summary>
public sealed record GroupOptionDto(int Id, string Name, bool IsEveryone, int MemberCount);
