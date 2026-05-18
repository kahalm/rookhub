namespace RookHub.Api.DTOs;

public class ProfileDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? FideId { get; set; }
    public string? ChessResultsId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }
}

public class UpdateProfileDto
{
    public string? DisplayName { get; set; }
    public string? FideId { get; set; }
    public string? ChessResultsId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }
}
