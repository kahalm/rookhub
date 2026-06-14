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
    public string? ChessResultsId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }
    public string? FideId { get; set; }
}

/// <summary>Puzzle-Statistik eines Freundes — für den Vergleich „Du vs. Freund" auf der Freunde-Stats-Seite.</summary>
public class FriendStatsDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public PuzzleStatsDto Stats { get; set; } = new();
    /// <summary>Themen-Aufschlüsselung (Versuche/Gelöst je Thema) für den Themen-Vergleich.</summary>
    public List<ThemeStatDto> Themes { get; set; } = new();
}
