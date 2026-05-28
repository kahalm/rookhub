using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class EndlessProgressService
{
    private readonly AppDbContext _db;
    private const int MaxSessions = 50;

    public EndlessProgressService(AppDbContext db) => _db = db;

    // --- Authenticated Progress ---

    public async Task<EndlessSyncResponseDto> GetSyncDataAsync(int userId)
    {
        var progress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId);

        var sessions = await _db.EndlessSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Timestamp)
            .Take(MaxSessions)
            .Select(s => MapSessionDto(s))
            .ToListAsync();

        return new EndlessSyncResponseDto
        {
            Progress = progress != null ? MapProgressDto(progress) : null,
            Sessions = sessions
        };
    }

    public async Task<EndlessProgressDto> SaveProgressAsync(int userId, SaveEndlessProgressDto dto)
    {
        var progress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (progress == null)
        {
            progress = new EndlessProgress { UserId = userId };
            _db.EndlessProgresses.Add(progress);
        }

        ApplyProgressDto(progress, dto);
        await _db.SaveChangesAsync();
        return MapProgressDto(progress);
    }

    // --- Anonymous Progress ---

    public async Task<EndlessSyncResponseDto> GetAnonymousSyncDataAsync(string sessionId)
    {
        var progress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.AnonymousSessionId == sessionId);

        var sessions = await _db.EndlessSessions
            .Where(s => s.AnonymousSessionId == sessionId)
            .OrderByDescending(s => s.Timestamp)
            .Take(MaxSessions)
            .Select(s => MapSessionDto(s))
            .ToListAsync();

        return new EndlessSyncResponseDto
        {
            Progress = progress != null ? MapProgressDto(progress) : null,
            Sessions = sessions
        };
    }

    public async Task<EndlessProgressDto> SaveAnonymousProgressAsync(string sessionId, SaveEndlessProgressDto dto)
    {
        var progress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.AnonymousSessionId == sessionId);

        if (progress == null)
        {
            progress = new EndlessProgress { AnonymousSessionId = sessionId };
            _db.EndlessProgresses.Add(progress);
        }

        ApplyProgressDto(progress, dto);
        await _db.SaveChangesAsync();
        return MapProgressDto(progress);
    }

    // --- Sessions ---

    public async Task<EndlessSessionDto> RecordSessionAsync(int userId, RecordEndlessSessionDto dto)
    {
        var session = new EndlessSession
        {
            UserId = userId,
            Timestamp = dto.Timestamp,
            TotalSolved = dto.TotalSolved,
            MaxRating = dto.MaxRating,
            DurationSeconds = dto.DurationSeconds,
            ConfigJson = dto.ConfigJson,
            MistakeAtRatings = dto.MistakeAtRatings
        };
        _db.EndlessSessions.Add(session);
        await _db.SaveChangesAsync();

        await TrimSessionsAsync(userId: userId);
        return MapSessionDto(session);
    }

    public async Task<EndlessSessionDto> RecordAnonymousSessionAsync(string sessionId, RecordEndlessSessionDto dto)
    {
        var session = new EndlessSession
        {
            AnonymousSessionId = sessionId,
            Timestamp = dto.Timestamp,
            TotalSolved = dto.TotalSolved,
            MaxRating = dto.MaxRating,
            DurationSeconds = dto.DurationSeconds,
            ConfigJson = dto.ConfigJson,
            MistakeAtRatings = dto.MistakeAtRatings
        };
        _db.EndlessSessions.Add(session);
        await _db.SaveChangesAsync();

        await TrimSessionsAsync(anonymousSessionId: sessionId);
        return MapSessionDto(session);
    }

    // --- Bulk Import ---

    public async Task<int> BulkImportSessionsAsync(int userId, List<RecordEndlessSessionDto> dtos)
    {
        var count = 0;
        foreach (var dto in dtos)
        {
            _db.EndlessSessions.Add(new EndlessSession
            {
                UserId = userId,
                Timestamp = dto.Timestamp,
                TotalSolved = dto.TotalSolved,
                MaxRating = dto.MaxRating,
                DurationSeconds = dto.DurationSeconds,
                ConfigJson = dto.ConfigJson,
                MistakeAtRatings = dto.MistakeAtRatings
            });
            count++;
        }
        await _db.SaveChangesAsync();
        await TrimSessionsAsync(userId: userId);
        return count;
    }

    public async Task<int> BulkImportAnonymousSessionsAsync(string sessionId, List<RecordEndlessSessionDto> dtos)
    {
        var count = 0;
        foreach (var dto in dtos)
        {
            _db.EndlessSessions.Add(new EndlessSession
            {
                AnonymousSessionId = sessionId,
                Timestamp = dto.Timestamp,
                TotalSolved = dto.TotalSolved,
                MaxRating = dto.MaxRating,
                DurationSeconds = dto.DurationSeconds,
                ConfigJson = dto.ConfigJson,
                MistakeAtRatings = dto.MistakeAtRatings
            });
            count++;
        }
        await _db.SaveChangesAsync();
        await TrimSessionsAsync(anonymousSessionId: sessionId);
        return count;
    }

    // --- Claim (anonymous → user) ---

    public async Task<int> ClaimSessionAsync(int userId, string anonymousSessionId)
    {
        var anonProgress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.AnonymousSessionId == anonymousSessionId);

        var anonSessions = await _db.EndlessSessions
            .Where(s => s.AnonymousSessionId == anonymousSessionId)
            .ToListAsync();

        if (anonProgress == null && anonSessions.Count == 0)
            return 0;

        var userProgress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (anonProgress != null)
        {
            if (userProgress == null)
            {
                // Transfer config + highscore + active game
                userProgress = new EndlessProgress
                {
                    UserId = userId,
                    StartElo = anonProgress.StartElo,
                    Step = anonProgress.Step,
                    Themes = anonProgress.Themes,
                    Fasttrack = anonProgress.Fasttrack,
                    FasttrackThreshold1 = anonProgress.FasttrackThreshold1,
                    FasttrackThreshold2 = anonProgress.FasttrackThreshold2,
                    StockfishDepth = anonProgress.StockfishDepth,
                    Highscore = anonProgress.Highscore,
                    ActiveGameState = anonProgress.ActiveGameState,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.EndlessProgresses.Add(userProgress);
            }
            else
            {
                // Merge: highscore = max, active game only if user has none
                userProgress.Highscore = Math.Max(userProgress.Highscore, anonProgress.Highscore);
                if (userProgress.ActiveGameState == null && anonProgress.ActiveGameState != null)
                    userProgress.ActiveGameState = anonProgress.ActiveGameState;
                userProgress.UpdatedAt = DateTime.UtcNow;
            }

            _db.EndlessProgresses.Remove(anonProgress);
        }

        // Transfer sessions
        var transferred = 0;
        foreach (var session in anonSessions)
        {
            session.UserId = userId;
            session.AnonymousSessionId = null;
            transferred++;
        }

        await _db.SaveChangesAsync();
        await TrimSessionsAsync(userId: userId);
        return transferred;
    }

    // --- Helpers ---

    private async Task TrimSessionsAsync(int? userId = null, string? anonymousSessionId = null)
    {
        IQueryable<EndlessSession> query;
        if (userId.HasValue)
            query = _db.EndlessSessions.Where(s => s.UserId == userId.Value);
        else if (anonymousSessionId != null)
            query = _db.EndlessSessions.Where(s => s.AnonymousSessionId == anonymousSessionId);
        else
            return;

        var count = await query.CountAsync();
        if (count <= MaxSessions) return;

        var toRemove = await query
            .OrderBy(s => s.Timestamp)
            .Take(count - MaxSessions)
            .ToListAsync();

        _db.EndlessSessions.RemoveRange(toRemove);
        await _db.SaveChangesAsync();
    }

    private static void ApplyProgressDto(EndlessProgress progress, SaveEndlessProgressDto dto)
    {
        progress.StartElo = dto.StartElo;
        progress.Step = dto.Step;
        progress.Themes = dto.Themes;
        progress.Fasttrack = dto.Fasttrack;
        progress.FasttrackThreshold1 = dto.FasttrackThreshold1;
        progress.FasttrackThreshold2 = dto.FasttrackThreshold2;
        progress.StockfishDepth = dto.StockfishDepth;
        progress.Highscore = dto.Highscore;
        progress.ActiveGameState = dto.ActiveGameState;
        progress.UpdatedAt = DateTime.UtcNow;
    }

    private static EndlessProgressDto MapProgressDto(EndlessProgress p) => new()
    {
        StartElo = p.StartElo,
        Step = p.Step,
        Themes = p.Themes,
        Fasttrack = p.Fasttrack,
        FasttrackThreshold1 = p.FasttrackThreshold1,
        FasttrackThreshold2 = p.FasttrackThreshold2,
        StockfishDepth = p.StockfishDepth,
        Highscore = p.Highscore,
        ActiveGameState = p.ActiveGameState,
        UpdatedAt = p.UpdatedAt
    };

    private static EndlessSessionDto MapSessionDto(EndlessSession s) => new()
    {
        Id = s.Id,
        Timestamp = s.Timestamp,
        TotalSolved = s.TotalSolved,
        MaxRating = s.MaxRating,
        DurationSeconds = s.DurationSeconds,
        ConfigJson = s.ConfigJson,
        MistakeAtRatings = s.MistakeAtRatings
    };
}
