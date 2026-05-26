using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class PuzzleService
{
    private readonly AppDbContext _db;

    public PuzzleService(AppDbContext db) => _db = db;

    public async Task<PuzzleDto?> GetRandomAsync(int? userId, int? minRating, int? maxRating, string? themes, bool excludeSolved)
    {
        var query = _db.Puzzles.AsQueryable();

        if (minRating.HasValue)
            query = query.Where(p => p.Rating >= minRating.Value);
        if (maxRating.HasValue)
            query = query.Where(p => p.Rating <= maxRating.Value);
        if (!string.IsNullOrEmpty(themes))
        {
            var themeList = themes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var theme in themeList)
                query = query.Where(p => p.Themes != null && p.Themes.Contains(theme));
        }
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts
                .Where(a => a.UserId == uid && a.Solved)
                .Select(a => a.PuzzleId);
            query = query.Where(p => !solvedIds.Contains(p.Id));
        }

        var count = await query.CountAsync();
        if (count == 0) return null;

        var offset = Random.Shared.Next(count);
        var puzzle = await query.Skip(offset).FirstAsync();

        return MapToDto(puzzle);
    }

    public async Task<PuzzleDto?> GetByIdAsync(int id)
    {
        var puzzle = await _db.Puzzles.FindAsync(id);
        return puzzle == null ? null : MapToDto(puzzle);
    }

    public async Task<PuzzleAttemptDto> RecordAttemptAsync(int userId, int puzzleId, RecordPuzzleAttemptDto dto)
    {
        var puzzle = await _db.Puzzles.FindAsync(puzzleId)
            ?? throw new KeyNotFoundException("Puzzle not found.");

        var attempt = new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzleId,
            Solved = dto.Solved,
            TimeSpentSeconds = dto.TimeSpentSeconds
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        return new PuzzleAttemptDto
        {
            Id = attempt.Id,
            PuzzleId = attempt.PuzzleId,
            LichessId = puzzle.LichessId,
            PuzzleRating = puzzle.Rating,
            Solved = attempt.Solved,
            TimeSpentSeconds = attempt.TimeSpentSeconds,
            AttemptedAt = attempt.AttemptedAt
        };
    }

    public async Task<PuzzleStatsDto> GetStatsAsync(int userId)
    {
        var attempts = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AttemptedAt)
            .Select(a => a.Solved)
            .ToListAsync();

        if (attempts.Count == 0)
            return new PuzzleStatsDto();

        var solved = attempts.Count(a => a);
        var accuracy = (double)solved / attempts.Count * 100;

        // Calculate streaks
        var currentStreak = 0;
        foreach (var s in attempts)
        {
            if (s) currentStreak++;
            else break;
        }

        var bestStreak = 0;
        var streak = 0;
        foreach (var s in attempts)
        {
            if (s) { streak++; bestStreak = Math.Max(bestStreak, streak); }
            else streak = 0;
        }

        return new PuzzleStatsDto
        {
            TotalAttempts = attempts.Count,
            Solved = solved,
            Accuracy = Math.Round(accuracy, 1),
            CurrentStreak = currentStreak,
            BestStreak = bestStreak
        };
    }

    public async Task<List<PuzzleAttemptDto>> GetHistoryAsync(int userId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        return await _db.PuzzleAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AttemptedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(a => a.Puzzle)
            .Select(a => new PuzzleAttemptDto
            {
                Id = a.Id,
                PuzzleId = a.PuzzleId,
                LichessId = a.Puzzle.LichessId,
                PuzzleRating = a.Puzzle.Rating,
                Solved = a.Solved,
                TimeSpentSeconds = a.TimeSpentSeconds,
                AttemptedAt = a.AttemptedAt
            })
            .ToListAsync();
    }

    public async Task<int> ImportFromCsvAsync(Stream csvStream, int? minRating, int? maxRating, int? maxCount)
    {
        var existingIds = await _db.Puzzles.Select(p => p.LichessId).ToHashSetAsync();
        var imported = 0;
        var batch = new List<Puzzle>();

        using var reader = new StreamReader(csvStream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            var lichessId = parts[0].Trim();
            if (existingIds.Contains(lichessId)) continue;

            if (!int.TryParse(parts[3].Trim(), out var rating)) continue;

            if (minRating.HasValue && rating < minRating.Value) continue;
            if (maxRating.HasValue && rating > maxRating.Value) continue;

            var puzzle = new Puzzle
            {
                LichessId = lichessId,
                Fen = parts[1].Trim(),
                Moves = parts[2].Trim(),
                Rating = rating,
                RatingDeviation = int.TryParse(parts[4].Trim(), out var rd) ? rd : 0,
                Popularity = int.TryParse(parts[5].Trim(), out var pop) ? pop : 0,
                NbPlays = int.TryParse(parts[6].Trim(), out var nb) ? nb : 0,
                Themes = parts.Length > 7 ? parts[7].Trim() : null,
                GameUrl = parts.Length > 8 ? parts[8].Trim() : null,
                OpeningTags = parts.Length > 9 ? parts[9].Trim() : null
            };

            batch.Add(puzzle);
            existingIds.Add(lichessId);
            imported++;

            if (maxCount.HasValue && imported >= maxCount.Value) break;

            if (batch.Count >= 1000)
            {
                _db.Puzzles.AddRange(batch);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.Puzzles.AddRange(batch);
            await _db.SaveChangesAsync();
        }

        return imported;
    }

    private static PuzzleDto MapToDto(Puzzle p) => new()
    {
        Id = p.Id,
        LichessId = p.LichessId,
        Fen = p.Fen,
        Moves = p.Moves,
        Rating = p.Rating,
        Themes = p.Themes,
        GameUrl = p.GameUrl
    };
}
