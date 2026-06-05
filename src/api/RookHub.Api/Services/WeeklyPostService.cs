using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Per-User-Fortschritt für Wochenposts: zeichnet gespielte Puzzles (idempotent je (Post, User, Index))
/// auf und berechnet den Stand. „Erledigt" = alle Puzzles gespielt (Solved egal). Muster analog zu
/// <see cref="CourseService.RecordResultAsync"/> (idempotent + <see cref="DbUpdateException"/>-Race-Handling).
/// </summary>
public class WeeklyPostService
{
    private readonly AppDbContext _db;
    private readonly ILogger<WeeklyPostService> _logger;

    public WeeklyPostService(AppDbContext db, ILogger<WeeklyPostService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Zeichnet einen gespielten Puzzle-Versuch auf (erster Versuch je Index zählt) und liefert den Stand.</summary>
    public async Task<WeeklyPostProgressDto> RecordAttemptAsync(int weeklyPostId, int userId, RecordWeeklyAttemptDto dto)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");

        var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
        if (dto.PuzzleIndex < 0 || dto.PuzzleIndex >= total)
            throw new KeyNotFoundException("Puzzle index out of range.");

        var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);

        var already = await _db.WeeklyPostAttempts
            .AnyAsync(a => a.WeeklyPostId == weeklyPostId && a.UserId == userId && a.PuzzleIndex == dto.PuzzleIndex);
        if (!already)
        {
            _db.WeeklyPostAttempts.Add(new WeeklyPostAttempt
            {
                WeeklyPostId = weeklyPostId,
                UserId = userId,
                PuzzleIndex = dto.PuzzleIndex,
                Solved = dto.Solved,
                TimeSeconds = timeSeconds,
                AttemptedAt = DateTime.UtcNow,
            });
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race: paralleles Aufzeichnen desselben Puzzles → Unique (WeeklyPostId, UserId, PuzzleIndex). Idempotent.
                _db.ChangeTracker.Clear();
            }
        }

        return await BuildProgressAsync(weeklyPostId, userId, total);
    }

    /// <summary>Aktueller Fortschritt des Users für einen Wochenpost.</summary>
    public async Task<WeeklyPostProgressDto> GetProgressAsync(int weeklyPostId, int userId)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");
        var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
        return await BuildProgressAsync(weeklyPostId, userId, total);
    }

    private async Task<WeeklyPostProgressDto> BuildProgressAsync(int weeklyPostId, int userId, int total)
    {
        var played = await _db.WeeklyPostAttempts
            .Where(a => a.WeeklyPostId == weeklyPostId && a.UserId == userId)
            .Select(a => a.Solved)
            .ToListAsync();

        return new WeeklyPostProgressDto
        {
            WeeklyPostId = weeklyPostId,
            Total = total,
            PlayedCount = played.Count,
            SolvedCount = played.Count(s => s),
            Completed = total > 0 && played.Count >= total,
        };
    }
}
