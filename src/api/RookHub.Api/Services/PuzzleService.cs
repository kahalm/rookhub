using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class PuzzleService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PuzzleService> _logger;

    // Obergrenze anonymer Versuche pro Session — verhindert unbegrenztes
    // Anwachsen der PuzzleAttempts-Tabelle durch eine einzelne (anonyme) Session.
    private const int MaxAnonymousAttemptsPerSession = 200;

    public PuzzleService(AppDbContext db, IMemoryCache cache, ILogger<PuzzleService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
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
        var anyFilter = minRating.HasValue || maxRating.HasValue
            || !string.IsNullOrEmpty(themes) || (excludeSolved && userId.HasValue);

        int? minId, maxId;
        if (anyFilter)
        {
            // ID-Range ueber die GEFILTERTE Menge bestimmen (eine Aggregat-Abfrage).
            // Die globale Range wuerde randomId fast immer ausserhalb der gefilterten
            // Treffer platzieren -> alle Versuche scheitern -> degenerierter Fallback,
            // der stets dasselbe (erste) Puzzle liefert.
            var range = await query
                .GroupBy(_ => 1)
                .Select(g => new { Min = g.Min(p => p.Id), Max = g.Max(p => p.Id) })
                .FirstOrDefaultAsync();
            if (range == null) return null; // leere gefilterte Menge
            minId = range.Min;
            maxId = range.Max;
        }
        else
        {
            (minId, maxId) = await GetCachedIdRangeAsync();
        }
        if (minId == null || maxId == null) return null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var randomId = Random.Shared.Next(minId.Value, maxId.Value + 1);
            // Vorwaerts suchen; wenn nichts mehr kommt (randomId nahe Max), rueckwaerts –
            // so liefert jeder randomId in [min,max] einen Treffer (kein Always-First-Bias).
            var puzzle = await query
                .Where(p => p.Id >= randomId)
                .OrderBy(p => p.Id)
                .FirstOrDefaultAsync()
                ?? await query
                .Where(p => p.Id <= randomId)
                .OrderByDescending(p => p.Id)
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

        var user = await _db.AppUsers.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var vizLevel = Math.Clamp(dto.VisualizationLevel, 0, 4);
        var currentElo = GetEloForLevel(user, vizLevel);
        var attemptCount = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.VisualizationLevel == vizLevel);
        var kFactor = attemptCount < 30 ? 40 : 20;
        var (newRating, change) = CalculateElo(currentElo, puzzle.Rating, dto.Solved, kFactor);

        SetEloForLevel(user, vizLevel, newRating);

        var attempt = new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzleId,
            Solved = dto.Solved,
            TimeSpentSeconds = dto.TimeSpentSeconds,
            MoveLog = dto.MoveLog,
            EloAfter = newRating,
            EloChange = change,
            VisualizationLevel = vizLevel
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "PuzzleAttempt: User {UserId} {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight} VizLevel={VizLevel} Elo={EloAfter} ({EloChange:+#;-#;0})",
            userId, dto.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, dto.TimeSpentSeconds, dto.ScreenWidth, dto.ScreenHeight, vizLevel, newRating, change);

        return new PuzzleAttemptDto
        {
            Id = attempt.Id,
            PuzzleId = attempt.PuzzleId,
            LichessId = puzzle.LichessId,
            PuzzleRating = puzzle.Rating,
            Solved = attempt.Solved,
            TimeSpentSeconds = attempt.TimeSpentSeconds,
            AttemptedAt = attempt.AttemptedAt,
            MoveLog = attempt.MoveLog,
            EloAfter = attempt.EloAfter,
            EloChange = attempt.EloChange,
            VisualizationLevel = attempt.VisualizationLevel
        };
    }

    private async Task TrimAnonymousAttemptsAsync(string sessionId)
    {
        var count = await _db.PuzzleAttempts.CountAsync(a => a.AnonymousSessionId == sessionId);
        if (count <= MaxAnonymousAttemptsPerSession) return;
        var stale = await _db.PuzzleAttempts
            .Where(a => a.AnonymousSessionId == sessionId)
            .OrderBy(a => a.AttemptedAt)
            .Take(count - MaxAnonymousAttemptsPerSession)
            .ToListAsync();
        _db.PuzzleAttempts.RemoveRange(stale);
        await _db.SaveChangesAsync();
    }

    public async Task<PuzzleAttemptDto> RecordAnonymousAttemptAsync(string sessionId, int puzzleId, RecordPuzzleAttemptDto dto)
    {
        var puzzle = await _db.Puzzles.FindAsync(puzzleId)
            ?? throw new KeyNotFoundException("Puzzle not found.");

        var vizLevel = Math.Clamp(dto.VisualizationLevel, 0, 4);
        var attempt = new PuzzleAttempt
        {
            UserId = null,
            AnonymousSessionId = sessionId,
            PuzzleId = puzzleId,
            Solved = dto.Solved,
            TimeSpentSeconds = dto.TimeSpentSeconds,
            MoveLog = dto.MoveLog,
            VisualizationLevel = vizLevel
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        await TrimAnonymousAttemptsAsync(sessionId);

        _logger.LogInformation(
            "PuzzleAttempt: Anonymous {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight}",
            dto.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, dto.TimeSpentSeconds, dto.ScreenWidth, dto.ScreenHeight);

        return new PuzzleAttemptDto
        {
            Id = attempt.Id,
            PuzzleId = attempt.PuzzleId,
            LichessId = puzzle.LichessId,
            PuzzleRating = puzzle.Rating,
            Solved = attempt.Solved,
            TimeSpentSeconds = attempt.TimeSpentSeconds,
            AttemptedAt = attempt.AttemptedAt,
            MoveLog = attempt.MoveLog,
            EloAfter = null,
            EloChange = null,
            VisualizationLevel = attempt.VisualizationLevel
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

    public async Task<PuzzleStatsDto> GetStatsAsync(int userId, int vizLevel = 0)
    {
        var user = await _db.AppUsers.FindAsync(userId);

        var totalAttempts = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId);
        if (totalAttempts == 0)
            return new PuzzleStatsDto
            {
                PuzzleElo = user != null ? GetEloForLevel(user, vizLevel) : GetDefaultElo(vizLevel),
                PuzzleEloPerLevel = user != null ? BuildEloDict(user) : null
            };

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
            BestStreak = bestStreak,
            PuzzleElo = user != null ? GetEloForLevel(user, vizLevel) : GetDefaultElo(vizLevel),
            PuzzleEloPerLevel = user != null ? BuildEloDict(user) : null
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
                AttemptedAt = a.AttemptedAt,
                MoveLog = a.MoveLog,
                EloAfter = a.EloAfter,
                EloChange = a.EloChange,
                VisualizationLevel = a.VisualizationLevel
            })
            .ToListAsync();
    }

    /// <summary>Puzzle-Elo-Verlauf (letzte <paramref name="limit"/> bewertete Versuche, chronologisch aufsteigend).</summary>
    public async Task<List<EloHistoryPointDto>> GetEloHistoryAsync(int userId, int limit = 500)
    {
        if (limit < 1) limit = 1;
        if (limit > 2000) limit = 2000;

        var points = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId && a.EloAfter != null)
            .OrderByDescending(a => a.AttemptedAt)
            .Take(limit)
            .Select(a => new EloHistoryPointDto
            {
                AttemptedAt = a.AttemptedAt,
                Elo = a.EloAfter!.Value,
                VizLevel = a.VisualizationLevel,
                Solved = a.Solved,
            })
            .ToListAsync();
        points.Reverse();   // ältester zuerst
        return points;
    }

    /// <summary>Aufschlüsselung der Versuche nach Thema, Rating-Band und Tag (für die Statistikseite).</summary>
    public async Task<PuzzleBreakdownDto> GetBreakdownAsync(int userId)
    {
        var rows = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Solved, a.AttemptedAt, Rating = a.Puzzle.Rating, a.Puzzle.Themes })
            .ToListAsync();

        // Themen (Lichess-Themes sind leerzeichengetrennt im Themes-String).
        var themeAgg = new Dictionary<string, (int attempts, int solved)>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Themes)) continue;
            foreach (var theme in r.Themes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (att, sol) = themeAgg.TryGetValue(theme, out var v) ? v : (0, 0);
                themeAgg[theme] = (att + 1, sol + (r.Solved ? 1 : 0));
            }
        }
        var themes = themeAgg
            .Select(kv => new ThemeStatDto { Theme = kv.Key, Attempts = kv.Value.attempts, Solved = kv.Value.solved })
            .OrderByDescending(t => t.Attempts).ThenBy(t => t.Theme)
            .Take(20).ToList();

        // Rating-Bänder (200er-Schritte).
        var bandAgg = new Dictionary<int, (int attempts, int solved)>();
        foreach (var r in rows)
        {
            var bucket = (r.Rating / 200) * 200;
            var (att, sol) = bandAgg.TryGetValue(bucket, out var v) ? v : (0, 0);
            bandAgg[bucket] = (att + 1, sol + (r.Solved ? 1 : 0));
        }
        var ratingBands = bandAgg
            .OrderBy(kv => kv.Key)
            .Select(kv => new RatingBandStatDto { From = kv.Key, To = kv.Key + 199, Attempts = kv.Value.attempts, Solved = kv.Value.solved })
            .ToList();

        // Aktivität pro Tag (letzte 365 Tage).
        var since = DateTime.UtcNow.Date.AddDays(-364);
        var activity = rows
            .Where(r => r.AttemptedAt.Date >= since)
            .GroupBy(r => r.AttemptedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ActivityDayDto { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
            .ToList();

        return new PuzzleBreakdownDto { Themes = themes, RatingBands = ratingBands, Activity = activity };
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

    public static int GetDefaultElo(int level) => Math.Max(100, 1500 - 100 * level);

    internal static int GetEloForLevel(AppUser user, int level) => level switch
    {
        0 => user.PuzzleElo,
        1 => user.PuzzleEloViz1 ?? GetDefaultElo(1),
        2 => user.PuzzleEloViz2 ?? GetDefaultElo(2),
        3 => user.PuzzleEloViz3 ?? GetDefaultElo(3),
        4 => user.PuzzleEloViz4 ?? GetDefaultElo(4),
        _ => user.PuzzleElo
    };

    internal static void SetEloForLevel(AppUser user, int level, int elo)
    {
        switch (level)
        {
            case 0: user.PuzzleElo = elo; break;
            case 1: user.PuzzleEloViz1 = elo; break;
            case 2: user.PuzzleEloViz2 = elo; break;
            case 3: user.PuzzleEloViz3 = elo; break;
            case 4: user.PuzzleEloViz4 = elo; break;
        }
    }

    private static Dictionary<int, int> BuildEloDict(AppUser user) => new()
    {
        [0] = user.PuzzleElo,
        [1] = user.PuzzleEloViz1 ?? GetDefaultElo(1),
        [2] = user.PuzzleEloViz2 ?? GetDefaultElo(2),
        [3] = user.PuzzleEloViz3 ?? GetDefaultElo(3),
        [4] = user.PuzzleEloViz4 ?? GetDefaultElo(4),
    };

    internal static (int newRating, int change) CalculateElo(int userRating, int puzzleRating, bool solved, int kFactor)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10.0, (puzzleRating - userRating) / 400.0));
        double actual = solved ? 1.0 : 0.0;
        int change = (int)Math.Round(kFactor * (actual - expected));
        int newRating = Math.Max(100, userRating + change);
        return (newRating, newRating - userRating);
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
