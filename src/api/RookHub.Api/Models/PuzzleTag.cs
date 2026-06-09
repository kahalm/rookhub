namespace RookHub.Api.Models;

/// <summary>
/// n:m-Verknüpfung Puzzle ↔ Tag. Rating ist aus dem Puzzle denormalisiert, damit der Index
/// (TagId, Rating) „Puzzles mit Thema X im Rating-Fenster" rein über den Index beantwortet —
/// das macht den Themen-Filter blitzschnell (statt LIKE-Full-Scan).
/// </summary>
public class PuzzleTag
{
    public int PuzzleId { get; set; }
    public Puzzle Puzzle { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public int Rating { get; set; }
}
