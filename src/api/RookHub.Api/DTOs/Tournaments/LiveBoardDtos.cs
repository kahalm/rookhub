using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Tournaments;

/// <summary><c>PUT /api/live-boards/{id}/link</c>: Lichess-Link der Übertragung (Turnier, Runde oder Partie).</summary>
public sealed class LiveBoardLinkRequest
{
    [MaxLength(500)]
    public string? Url { get; set; }
}
