namespace RookHub.Api.DTOs;

public class FriendDto
{
    public int FriendshipId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class FriendRequestDto
{
    public int FriendshipId { get; set; }
    public int RequesterId { get; set; }
    public string RequesterUsername { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class UserSearchResultDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
