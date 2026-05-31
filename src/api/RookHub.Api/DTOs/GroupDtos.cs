using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class GroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
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
}
