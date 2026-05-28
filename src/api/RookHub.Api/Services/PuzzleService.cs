using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class PuzzleService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public PuzzleService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

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
            {
                var sanitized = SanitizeLikeInput(theme);
                query = query.Where(p => p.Themes != null && EF.Functions.Like(p.Themes, $"%{sanitized}%"));
            }
        }
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts
                .Where(a => a.UserId == uid && a.Solved)
                .Select(a => a.PuzzleId);
            query = query.Where(p => !solvedIds.Contains(p.Id));
        }

        // Fast random selection via ID-range instead of COUNT(*)+SKIP(N).
        // COUNT+SKIP is O(N) and takes 10+ seconds on millions of rows.
        // ID-range picks a random point in the PK space and seeks forward - O(1).
        var (minId, maxId) = await GetCachedIdRangeAsync();
        if (minId == null || maxId == null) return null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var randomId = Random.Shared.Next(minId.Value, maxId.Value + 1);
            var puzzle = await query
                .Where(p => p.Id >= randomId)
                .OrderBy(p => p.Id)
                .FirstOrDefaultAsync();
            if (puzzle != null) return MapToDto(puzzle);
        }

        // Fallback: get any matching puzzle
        var fallback = await query.OrderBy(p => p.Id).FirstOrDefaultAsync();
        return fallback == null ? null : MapToDto(fallback);
    }

    public async Task<(int Min, int Max)?> GetRatingRangeAsync()
    {
        var min = await _db.Puzzles.MinAsync(p => (int?)p.Rating);
        var max = await _db.Puzzles.MaxAsync(p => (int?)p.Rating);
        if (min == null || max == null) return null;
        return (min.Value, max.Value);
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

    public async Task<PuzzleAttemptDto> RecordAnonymousAttemptAsync(string sessionId, int puzzleId, RecordPuzzleAttemptDto dto)
    {
        var puzzle = await _db.Puzzles.FindAsync(puzzleId)
            ?? throw new KeyNotFoundException("Puzzle not found.");

        var attempt = new PuzzleAttempt
        {
            UserId = null,
            AnonymousSessionId = sessionId,
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

    public async Task<PuzzleStatsDto> GetAnonymousStatsAsync(string sessionId)
    {
        var totalAttempts = await _db.PuzzleAttempts.CountAsync(a => a.AnonymousSessionId == sessionId);
        if (totalAttempts == 0)
            return new PuzzleStatsDto();

        var solved = await _db.PuzzleAttempts.CountAsync(a => a.AnonymousSessionId == sessionId && a.Solved);
        var accuracy = (double)solved / totalAttempts * 100;

        var recentResults = await _db.PuzzleAttempts
            .Where(a => a.AnonymousSessionId == sessionId)
            .OrderByDescending(a => a.AttemptedAt)
            .Take(1000)
            .Select(a => a.Solved)
            .ToListAsync();

        var currentStreak = 0;
        foreach (var s in recentResults)
        {
            if (s) currentStreak++;
            else break;
        }

        var bestStreak = 0;
        var streak = 0;
        foreach (var s in recentResults)
        {
            if (s) { streak++; bestStreak = Math.Max(bestStreak, streak); }
            else streak = 0;
        }

        return new PuzzleStatsDto
        {
            TotalAttempts = totalAttempts,
            Solved = solved,
            Accuracy = Math.Round(accuracy, 1),
            CurrentStreak = currentStreak,
            BestStreak = bestStreak
        };
    }

    public async Task<int> ClaimSessionAsync(int userId, string sessionId)
    {
        var attempts = await _db.PuzzleAttempts
            .Where(a => a.AnonymousSessionId == sessionId && a.UserId == null)
            .ToListAsync();

        foreach (var attempt in attempts)
        {
            attempt.UserId = userId;
            attempt.AnonymousSessionId = null;
        }

        await _db.SaveChangesAsync();
        return attempts.Count;
    }

    public async Task<PuzzleStatsDto> GetStatsAsync(int userId)
    {
        var totalAttempts = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId);
        if (totalAttempts == 0)
            return new PuzzleStatsDto();

        var solved = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.Solved);
        var accuracy = (double)solved / totalAttempts * 100;

        // Calculate streaks from most recent 1000 attempts
        var recentResults = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AttemptedAt)
            .Take(1000)
            .Select(a => a.Solved)
            .ToListAsync();

        var currentStreak = 0;
        foreach (var s in recentResults)
        {
            if (s) currentStreak++;
            else break;
        }

        var bestStreak = 0;
        var streak = 0;
        foreach (var s in recentResults)
        {
            if (s) { streak++; bestStreak = Math.Max(bestStreak, streak); }
            else streak = 0;
        }

        return new PuzzleStatsDto
        {
            TotalAttempts = totalAttempts,
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

    public async Task<int> ImportFromCsvAsync(Stream csvStream, int? minRating, int? maxRating, int? maxCount, CancellationToken ct = default)
    {
        var existingIds = await _db.Puzzles.Select(p => p.LichessId).ToHashSetAsync(ct);
        var imported = 0;
        var batch = new List<Puzzle>();

        using var reader = new StreamReader(csvStream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();

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
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.Puzzles.AddRange(batch);
            await _db.SaveChangesAsync(ct);
        }

        return imported;
    }

    private async Task<(int? Min, int? Max)> GetCachedIdRangeAsync()
    {
        const string cacheKey = "PuzzleIdRange";
        if (_cache.TryGetValue<(int?, int?)>(cacheKey, out var cached))
            return cached;

        var min = await _db.Puzzles.MinAsync(p => (int?)p.Id);
        var max = await _db.Puzzles.MaxAsync(p => (int?)p.Id);
        var result = (min, max);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    private static string SanitizeLikeInput(string input)
        => input.Replace("%", "\\%").Replace("_", "\\_");

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
