using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Liefert dem Schach-Bot den Trainings-/Puzzle-Fortschritt eines über die Discord-ID verknüpften
/// Spielers — Grundlage für den personalisierten Motivations-DM. Bündelt ausschließlich bestehende
/// Service-Logik (<see cref="TrainingGoalService"/>, <see cref="PuzzleService"/>); keine eigene
/// Aggregation, damit Bot- und Web-Ansicht denselben Fortschritt zeigen.
/// </summary>
public class BotStatsService
{
    private readonly AppDbContext _db;
    private readonly TrainingGoalService _goals;
    private readonly PuzzleService _puzzles;

    public BotStatsService(AppDbContext db, TrainingGoalService goals, PuzzleService puzzles)
    {
        _db = db;
        _goals = goals;
        _puzzles = puzzles;
    }

    /// <summary>
    /// Fortschritt für die gegebene Discord-ID — oder <c>null</c>, wenn kein RookHub-Konto damit
    /// verknüpft ist (der Bot zeigt dann den Verknüpfungs-Hinweis statt einer Motivation).
    /// </summary>
    public async Task<BotPlayerProgressDto?> GetProgressByDiscordIdAsync(string discordId)
    {
        var user = await _db.AppUsers
            .Where(u => u.Profile != null && u.Profile.DiscordId == discordId)
            .Select(u => new { u.Id, u.Username, DisplayName = u.Profile!.DisplayName })
            .FirstOrDefaultAsync();
        if (user == null)
            return null;

        return new BotPlayerProgressDto
        {
            Username = user.Username,
            DisplayName = user.DisplayName,
            // vizLevel = null → Elo des meistgespielten Levels (wie Dashboard), nicht stur Level 0.
            Today = await _goals.GetTodayAsync(user.Id),
            Puzzles = await _puzzles.GetStatsAsync(user.Id, null),
        };
    }
}
