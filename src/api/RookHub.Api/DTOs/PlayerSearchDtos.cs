namespace RookHub.Api.DTOs;

public class PlayerSearchResultDto
{
    public List<PlayerSearchItemDto> ChessResultsResults { get; set; } = [];
    public List<PlayerSearchItemDto> FideResults { get; set; } = [];
}

public class PlayerSearchItemDto
{
    public string Name { get; set; } = "";
    public string? FideId { get; set; }
    public string? ChessResultsId { get; set; }
    public int? Elo { get; set; }
    public string? Country { get; set; }
    public string? Title { get; set; }
}
