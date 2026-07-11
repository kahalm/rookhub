using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class EndlessProgressService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EndlessProgressService> _logger;
    private const int MaxSessions = 50;
    /// <summary>Obergrenze fürs Per-Puzzle-Logging einer Session (Schutz gegen überlange Payloads).</summary>
    private const int MaxLoggedSessionPuzzles = 2000;

    public EndlessProgressService(AppDbContext db, ILogger<EndlessProgressService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // --- Authenticated Progress ---

    public async Task<EndlessSyncResponseDto> GetSyncDataAsync(int userId)
    {
        var progress = await _db.EndlessProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId);

        var sessions = await _db.EndlessSessions
            .Where(s => s.UserId == userId && !s.IsArchived)
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

        var isNew = progress == null;
        if (isNew)
        {
            progress = new EndlessProgress { UserId = userId };
            _db.EndlessProgresses.Add(progress);
        }

        ApplyProgressDto(progress!, dto);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (isNew)
        {
            // Race: ein paralleler Request hat die Zeile zwischen Read und Insert
            // angelegt (Unique-Index auf UserId). Statt 500/Lost-Update die nun
            // vorhandene Zeile laden und das Update darauf anwenden.
            _db.ChangeTracker.Clear();
            progress = await _db.EndlessProgresses.FirstAsync(p => p.UserId == userId);
            ApplyProgressDto(progress, dto);
            await _db.SaveChangesAsync();
        }
        return MapProgressDto(progress!);
    }

    // --- Anonymous Progress ---

    public async Task<EndlessSyncResponseDto> GetAnonymousSyncDataAsync(string sessionId)
    {
        var progress = await _db.EndlessProgresses
            .Where(p => p.AnonymousSessionId == sessionId)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync();

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
            .Where(p => p.AnonymousSessionId == sessionId)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync();

        var isNew = progress == null;
        if (isNew)
        {
            progress = new EndlessProgress { AnonymousSessionId = sessionId };
            _db.EndlessProgresses.Add(progress);
        }

        ApplyProgressDto(progress!, dto);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (isNew)
        {
            // Race auf dem AnonymousSessionId-Insert: nun vorhandene Zeile laden
            // und das Update darauf anwenden (statt 500/Lost-Update).
            _db.ChangeTracker.Clear();
            progress = await _db.EndlessProgresses
                .Where(p => p.AnonymousSessionId == sessionId)
                .OrderBy(p => p.Id)
                .FirstAsync();
            ApplyProgressDto(progress, dto);
            await _db.SaveChangesAsync();
        }
        return MapProgressDto(progress!);
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
            MistakeAtRatings = dto.MistakeAtRatings,
            Seed = dto.Seed,
            ChainPuzzleIds = dto.ChainPuzzleIds,
            PuzzleAttemptsJson = SerializeAttempts(dto.Puzzles)
        };
        _db.EndlessSessions.Add(session);
        await _db.SaveChangesAsync();

        LogSessionPuzzles(userId, dto.Puzzles);
        // Strukturierter Event fuer Kibana: Runs/Tag, Ø/Max geloeste Puzzles, Max-Rating,
        // Leaderboard (Cardinality/Terms auf fields.UserId). Analog zum PuzzleAttempt-Log.
        _logger.LogInformation(
            "EndlessSessionCompleted: User {UserId} solved {TotalSolved} maxRating {MaxRating} in {DurationSeconds}s",
            userId, dto.TotalSolved, dto.MaxRating, dto.DurationSeconds);

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
            MistakeAtRatings = dto.MistakeAtRatings,
            Seed = dto.Seed,
            ChainPuzzleIds = dto.ChainPuzzleIds,
            PuzzleAttemptsJson = SerializeAttempts(dto.Puzzles)
        };
        _db.EndlessSessions.Add(session);
        await _db.SaveChangesAsync();

        LogSessionPuzzles(null, dto.Puzzles);
        _logger.LogInformation(
            "EndlessSessionCompleted: Anonymous solved {TotalSolved} maxRating {MaxRating} in {DurationSeconds}s",
            dto.TotalSolved, dto.MaxRating, dto.DurationSeconds);

        await TrimSessionsAsync(anonymousSessionId: sessionId);
        return MapSessionDto(session);
    }

    /// <summary>
    /// Loggt jedes Puzzle einer Endless-Session mit Start- und Lösungszeit (für ES/Kibana).
    /// userId == null = anonyme Session. Nicht persistiert — reines strukturiertes Logging.
    /// </summary>
    private void LogSessionPuzzles(int? userId, List<EndlessSessionPuzzleDto> puzzles)
    {
        if (puzzles == null || puzzles.Count == 0) return;
        foreach (var p in puzzles.Take(MaxLoggedSessionPuzzles))
        {
            // Client-Timestamps plausibilisieren: Dauer auf [0, 86400]s clampen und SolvedAt aus
            // StartedAt + Dauer ableiten (konsistent zu den anderen Modi; verhindert absurde Werte
            // wie 1970/9999 oder negative Dauer im Kibana-Log bei fehlerhaften Client-Daten).
            var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(p.StartedAt).UtcDateTime;
            var rawSolvedAt = DateTimeOffset.FromUnixTimeMilliseconds(p.EndedAt).UtcDateTime;
            var seconds = Math.Clamp((rawSolvedAt - startedAt).TotalSeconds, 0, 86400);
            var solvedAt = startedAt.AddSeconds(seconds);
            var result = p.Solved ? "solved" : "failed";
            if (userId.HasValue)
                _logger.LogInformation(
                    "EndlessPuzzleAttempt: User {UserId} {Result} endless-puzzle {PuzzleId} (LichessId={LichessId}, Rating={Rating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {DurationSeconds:F0}s",
                    userId.Value, result, p.PuzzleId, p.LichessId, p.Rating, startedAt, solvedAt, seconds);
            else
                _logger.LogInformation(
                    "EndlessPuzzleAttempt: Anonymous {Result} endless-puzzle {PuzzleId} (LichessId={LichessId}, Rating={Rating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {DurationSeconds:F0}s",
                    result, p.PuzzleId, p.LichessId, p.Rating, startedAt, solvedAt, seconds);
        }
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
                MistakeAtRatings = dto.MistakeAtRatings,
                Seed = dto.Seed,
                ChainPuzzleIds = dto.ChainPuzzleIds
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
                MistakeAtRatings = dto.MistakeAtRatings,
                Seed = dto.Seed,
                ChainPuzzleIds = dto.ChainPuzzleIds
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
                    Themes = anonProgress.Themes,
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

    // --- History ---

    public async Task<EndlessHistoryResponseDto> GetSessionHistoryAsync(int userId, int page, int pageSize, bool? archived = null)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var query = _db.EndlessSessions.Where(s => s.UserId == userId);
        if (archived.HasValue)
            query = query.Where(s => s.IsArchived == archived.Value);
        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => MapSessionDto(s))
            .ToListAsync();

        return new EndlessHistoryResponseDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>Einzelnen Lauf (mit den persistierten Puzzle-Versuchen) für die Detail-Ansicht laden.
    /// Liefert null, wenn der Lauf nicht existiert oder nicht dem Nutzer gehört.</summary>
    public async Task<EndlessSessionDetailDto?> GetSessionDetailAsync(int userId, int sessionId)
    {
        var session = await _db.EndlessSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session == null) return null;

        return new EndlessSessionDetailDto
        {
            Id = session.Id,
            Timestamp = session.Timestamp,
            TotalSolved = session.TotalSolved,
            MaxRating = session.MaxRating,
            DurationSeconds = session.DurationSeconds,
            ConfigJson = session.ConfigJson,
            MistakeAtRatings = session.MistakeAtRatings,
            Seed = session.Seed,
            ChainPuzzleIds = session.ChainPuzzleIds,
            IsArchived = session.IsArchived,
            Puzzles = DeserializeAttempts(session.PuzzleAttemptsJson)
        };
    }

    // --- Archive ---

    public async Task<int> ArchiveSessionsAsync(int userId, List<int> sessionIds, bool archive)
    {
        var sessions = await _db.EndlessSessions
            .Where(s => s.UserId == userId && sessionIds.Contains(s.Id))
            .ToListAsync();

        foreach (var session in sessions)
            session.IsArchived = archive;

        await _db.SaveChangesAsync();
        return sessions.Count;
    }

    // --- Helpers ---

    /// <summary>Serialisiert die Puzzle-Versuche kompakt (nur die für die Detail-Ansicht nötigen Felder)
    /// für die Persistierung in PuzzleAttemptsJson. Null bei leerer Liste.</summary>
    private static string? SerializeAttempts(List<EndlessSessionPuzzleDto> puzzles)
    {
        if (puzzles == null || puzzles.Count == 0) return null;
        var compact = puzzles
            .Take(MaxLoggedSessionPuzzles)
            .Select(p => new StoredAttempt(p.PuzzleId, p.LichessId, p.Rating, p.Solved))
            .ToList();
        return JsonSerializer.Serialize(compact);
    }

    private static List<EndlessSessionPuzzleDto> DeserializeAttempts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredAttempt>>(json) ?? new();
            return stored.Select(a => new EndlessSessionPuzzleDto
            {
                PuzzleId = a.PuzzleId,
                LichessId = a.LichessId,
                Rating = a.Rating,
                Solved = a.Solved
            }).ToList();
        }
        catch
        {
            return new();
        }
    }

    private record StoredAttempt(int PuzzleId, string? LichessId, int Rating, bool Solved);

    private async Task TrimSessionsAsync(int? userId = null, string? anonymousSessionId = null)
    {
        // Authenticated users have unlimited sessions — only trim anonymous
        if (userId.HasValue)
            return;

        if (anonymousSessionId == null)
            return;

        var query = _db.EndlessSessions.Where(s => s.AnonymousSessionId == anonymousSessionId);

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
        progress.Themes = dto.Themes;
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
        Themes = p.Themes,
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
        MistakeAtRatings = s.MistakeAtRatings,
        Seed = s.Seed,
        ChainPuzzleIds = s.ChainPuzzleIds,
        IsArchived = s.IsArchived
    };
}
