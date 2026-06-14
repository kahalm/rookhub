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

/// <summary>Ein Puzzle, an dem ein Freund gescheitert ist und es bis heute nicht gelöst hat — „Revenge a Friend".</summary>
public class RevengePuzzleDto
{
    public int PuzzleId { get; set; }
    public string LichessId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Themes { get; set; }
    /// <summary>Wie oft der Freund an diesem Puzzle gescheitert ist.</summary>
    public int FailCount { get; set; }
    /// <summary>Letzter Fehlversuch des Freundes (für die Sortierung „zuletzt gescheitert zuerst").</summary>
    public DateTime LastFailedAt { get; set; }
}

/// <summary>Revenge-Liste eines Freundes: offene Niederlagen, die du nun selbst lösen kannst.</summary>
public class RevengeListDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public List<RevengePuzzleDto> Puzzles { get; set; } = new();
}
