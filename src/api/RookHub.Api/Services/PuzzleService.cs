using System.Linq.Expressions;
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
    // Obergrenze auth. Versuche pro (User, Puzzle, VizLevel) — kein unbegrenztes Tabellenwachstum.
    private const int MaxAttemptsPerUserPuzzleVizLevel = 20;

    public PuzzleService(AppDbContext db, IMemoryCache cache, ILogger<PuzzleService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <param name="themes">Leerzeichengetrennt; Puzzle muss ALLE enthalten (UND-Verknüpfung).</param>
    /// <param name="themesAny">Leerzeichengetrennt; Puzzle muss MINDESTENS EINS enthalten (ODER-Verknüpfung).
    /// Für „5 schwächste Themen trainieren" — ein Puzzle trägt selten alle Schwächen-Themen gleichzeitig.</param>
    public async Task<PuzzleDto?> GetRandomAsync(int? userId, int? minRating, int? maxRating, string? themes, bool excludeSolved,
        string? themesAny = null, IReadOnlyCollection<int>? excludeIds = null)
    {
        var query = _db.Puzzles.AsQueryable();

        if (minRating.HasValue)
            query = query.Where(p => p.Rating >= minRating.Value);
        if (maxRating.HasValue)
            query = query.Where(p => p.Rating <= maxRating.Value);
        var hasThemeFilter = !string.IsNullOrEmpty(themes) || !string.IsNullOrEmpty(themesAny);
        query = ApplyThemeFilters(query, themes, themesAny);
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts
                .Where(a => a.UserId == uid && a.Solved)
                .Select(a => a.PuzzleId);
            query = query.Where(p => !solvedIds.Contains(p.Id));
        }
        // Bereits im selben Batch vergebene Puzzles ausschliessen (Offline-Vorab-Laden).
        if (excludeIds is { Count: > 0 })
            query = query.Where(p => !excludeIds.Contains(p.Id));

        // Theme-Filter (LIKE) kann KEINEN Index nutzen → die ID-Range-Methode (Min/Max + Seek)
        // würde mehrere Full-Scans pro Aufruf auslösen (im Endless-Batch ×Fensteranzahl = sehr lahm).
        // Stattdessen die (durch Rating bereits eingegrenzte) Treffer-ID-Liste EINMAL laden und
        // zufällig wählen: 1 Scan + 1 PK-Lookup.
        if (hasThemeFilter)
        {
            var ids = await query.Select(p => p.Id).ToListAsync();
            if (ids.Count == 0) return null;
            var pickId = ids[Random.Shared.Next(ids.Count)];
            var picked = await _db.Puzzles.FirstOrDefaultAsync(p => p.Id == pickId);
            return picked == null ? null : MapToDto(picked);
        }

        // Fast random selection via ID-range instead of COUNT(*)+SKIP(N).
        // COUNT+SKIP is O(N) and takes 10+ seconds on millions of rows.
        // ID-range picks a random point in the PK space and seeks forward - O(1).
        // Theme-Filter ist oben schon behandelt (early return) → hier nur Rating/Solved/ExcludeIds.
        var anyFilter = minRating.HasValue || maxRating.HasValue
            || (excludeSolved && userId.HasValue)
            || excludeIds is { Count: > 0 };

        int? minId, maxId;
        if (anyFilter)
        {
            // ID-Range ueber die GEFILTERTE Menge bestimmen. Min/Max-Aggregate statt
            // GroupBy(_=>1)+FirstOrDefault — so entsteht KEIN "FirstOrDefault ohne OrderBy"
            // (das Aggregat ist deterministisch). Die globale Range wuerde randomId fast immer
            // ausserhalb der gefilterten Treffer platzieren -> degenerierter Always-First-Fallback.
            if (!await query.AnyAsync()) return null; // leere gefilterte Menge
            minId = await query.MinAsync(p => p.Id);
            maxId = await query.MaxAsync(p => p.Id);
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

    /// <summary>
    /// Liefert je Rating-Fenster ein zufälliges, im Batch eindeutiges Puzzle (für das
    /// Offline-Vorab-Laden eines ganzen Endless-Runs). Fenster ohne Treffer entfallen;
    /// die Reihenfolge folgt den übergebenen Fenstern.
    /// </summary>
    public async Task<List<PuzzleDto>> GetRandomBatchAsync(int? userId,
        IEnumerable<(int Min, int Max)> windows, string? themes, bool excludeSolved, string? themesAny = null)
    {
        var winList = windows.ToList();
        // Theme-Filter (LIKE, kein Index): NICHT pro Fenster scannen (×40 = sehr lahm), sondern
        // EINMAL alle Treffer der Gesamt-Rating-Spanne laden und in-memory auf die Fenster verteilen.
        if ((!string.IsNullOrEmpty(themes) || !string.IsNullOrEmpty(themesAny)) && winList.Count > 0)
            return await GetThemedBatchAsync(userId, winList, themes, themesAny, excludeSolved);

        var used = new HashSet<int>();
        var result = new List<PuzzleDto>();
        foreach (var (min, max) in winList)
        {
            var dto = await GetRandomAsync(userId, min, max, themes, excludeSolved, themesAny, used);
            if (dto != null && used.Add(dto.Id))
                result.Add(dto);
        }
        return result;
    }

    /// <summary>
    /// Effizienter Theme-Batch: EIN Scan über die gesamte Rating-Spanne aller Fenster, dann
    /// in-memory je Fenster ein zufälliges, eindeutiges Treffer-Puzzle wählen und EINMAL nachladen.
    /// </summary>
    private async Task<List<PuzzleDto>> GetThemedBatchAsync(int? userId,
        List<(int Min, int Max)> windows, string? themes, string? themesAny, bool excludeSolved)
    {
        var spanMin = windows.Min(w => w.Min);
        var spanMax = windows.Max(w => w.Max);
        var q = _db.Puzzles.Where(p => p.Rating >= spanMin && p.Rating <= spanMax);
        q = ApplyThemeFilters(q, themes, themesAny);
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts.Where(a => a.UserId == uid && a.Solved).Select(a => a.PuzzleId);
            q = q.Where(p => !solvedIds.Contains(p.Id));
        }

        var candidates = await q.Select(p => new { p.Id, p.Rating }).ToListAsync();   // 1 Scan
        if (candidates.Count == 0) return new List<PuzzleDto>();

        var used = new HashSet<int>();
        var chosenIds = new List<int>();
        foreach (var (min, max) in windows)
        {
            var pool = candidates.Where(c => c.Rating >= min && c.Rating <= max && !used.Contains(c.Id)).ToList();
            if (pool.Count == 0) continue;
            var pick = pool[Random.Shared.Next(pool.Count)].Id;
            used.Add(pick);
            chosenIds.Add(pick);
        }
        if (chosenIds.Count == 0) return new List<PuzzleDto>();

        var puzzles = await _db.Puzzles.Where(p => chosenIds.Contains(p.Id)).ToListAsync();   // 1 Nachladen
        var byId = puzzles.ToDictionary(p => p.Id);
        return chosenIds.Where(byId.ContainsKey).Select(id => MapToDto(byId[id])).ToList();   // Fensterreihenfolge
    }

    /// <summary>Wendet UND-Themen (<paramref name="themes"/>) und ODER-Themen (<paramref name="themesAny"/>) auf die Query an.</summary>
    private static IQueryable<Puzzle> ApplyThemeFilters(IQueryable<Puzzle> query, string? themes, string? themesAny)
    {
        if (!string.IsNullOrEmpty(themes))
        {
            foreach (var theme in themes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var sanitized = SanitizeLikeInput(theme);
                query = query.Where(p => p.Themes != null && EF.Functions.Like(p.Themes, $"%{sanitized}%"));
            }
        }
        if (!string.IsNullOrEmpty(themesAny))
            query = WhereAnyThemeLike(query, themesAny);
        return query;
    }

    /// <summary>
    /// Filtert auf Puzzles, deren Themes-String mindestens EINES der übergebenen Themen enthält
    /// (ODER-Verknüpfung). Baut ein EF-übersetzbares OrElse-Prädikat über LIKE-Vergleiche.
    /// </summary>
    private static IQueryable<Puzzle> WhereAnyThemeLike(IQueryable<Puzzle> query, string themesAny)
    {
        var list = themesAny
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeLikeInput)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (list.Count == 0) return query;

        var p = Expression.Parameter(typeof(Puzzle), "p");
        var themesProp = Expression.Property(p, nameof(Puzzle.Themes));
        // EF.Functions als STATISCHER Property-Zugriff (nicht als Konstante!) — nur so erkennt
        // der Pomelo/MySQL-Übersetzer das Like-Muster und erzeugt SQL statt zu scheitern.
        var efFns = Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!);
        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            new[] { typeof(Microsoft.EntityFrameworkCore.DbFunctions), typeof(string), typeof(string) })!;

        Expression? anyLike = null;
        foreach (var t in list)
        {
            var pattern = Expression.Constant($"%{t}%", typeof(string));
            Expression like = Expression.Call(likeMethod, efFns, themesProp, pattern);
            anyLike = anyLike == null ? like : Expression.OrElse(anyLike, like);
        }
        // p.Themes != null && (LIKE %t1% || LIKE %t2% || ...)
        var notNull = Expression.NotEqual(themesProp, Expression.Constant(null, typeof(string)));
        var body = Expression.AndAlso(notNull, anyLike!);
        return query.Where(Expression.Lambda<Func<Puzzle, bool>>(body, p));
    }

    /// <summary>
    /// Die schwächsten Themen des Users: nach Lösungsquote (solved/attempts) aufsteigend,
    /// nur Themen mit ≥ <paramref name="minAttempts"/> Versuchen, max. <paramref name="count"/>.
    /// Basis für „5 schwächste Themen trainieren" in Puzzle- und Endless-Modus.
    /// </summary>
    public async Task<List<ThemeStatDto>> GetWorstThemesAsync(int userId, int count = 5, int minAttempts = 3)
    {
        var themeStrings = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId && a.Puzzle.Themes != null)
            .Select(a => new { a.Solved, a.Puzzle.Themes })
            .ToListAsync();

        var agg = new Dictionary<string, (int attempts, int solved)>();
        foreach (var r in themeStrings)
        {
            if (string.IsNullOrWhiteSpace(r.Themes)) continue;
            foreach (var theme in r.Themes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (att, sol) = agg.TryGetValue(theme, out var v) ? v : (0, 0);
                agg[theme] = (att + 1, sol + (r.Solved ? 1 : 0));
            }
        }

        return agg
            .Where(kv => kv.Value.attempts >= minAttempts)
            .Select(kv => new ThemeStatDto { Theme = kv.Key, Attempts = kv.Value.attempts, Solved = kv.Value.solved })
            .OrderBy(t => (double)t.Solved / t.Attempts)   // niedrigste Lösungsquote zuerst
            .ThenByDescending(t => t.Attempts)             // bei Gleichstand: mehr Daten zuerst
            .ThenBy(t => t.Theme)
            .Take(Math.Max(1, count))
            .ToList();
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

        // Idempotenz: Doppel-Submit innerhalb von 30 Sekunden → bestehenden Versuch zurückgeben.
        var idempotencyCutoff = DateTime.UtcNow.AddSeconds(-30);
        var duplicate = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId && a.PuzzleId == puzzleId
                     && a.VisualizationLevel == vizLevel && a.AttemptedAt >= idempotencyCutoff)
            .OrderByDescending(a => a.AttemptedAt)
            .FirstOrDefaultAsync();
        if (duplicate != null)
            return MapAttemptToDto(duplicate, puzzle);

        var currentElo = GetEloForLevel(user, vizLevel);
        var attemptCount = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.VisualizationLevel == vizLevel);
        var kFactor = attemptCount < 30 ? 40 : 20;

        // Elo nur beim ersten Versuch für dieses Puzzle aktualisieren — verhindert Elo-Inflation.
        var isFirstAttempt = !await _db.PuzzleAttempts.AnyAsync(
            a => a.UserId == userId && a.PuzzleId == puzzleId && a.VisualizationLevel == vizLevel);
        int newRating, change;
        if (isFirstAttempt)
        {
            (newRating, change) = CalculateElo(currentElo, puzzle.Rating, dto.Solved, kFactor);
            SetEloForLevel(user, vizLevel, newRating);
        }
        else
        {
            newRating = currentElo;
            change = 0;
        }

        var attempt = new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzleId,
            Solved = dto.Solved,
            TimeSpentSeconds = dto.TimeSpentSeconds,
            MoveLog = dto.MoveLog,
            EloAfter = newRating,
            EloChange = change,
            VisualizationLevel = vizLevel,
            EvalShown = dto.EvalShown,
            VizShowCount = Math.Clamp(dto.VizShowCount, 0, 100)
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        // Tabellenwachstum begrenzen: älteste Versuche entfernen wenn Limit überschritten.
        await TrimUserPuzzleAttemptsAsync(userId, puzzleId, vizLevel);

        var solvedAt = attempt.AttemptedAt;
        var startedAt = solvedAt.AddSeconds(-Math.Clamp(dto.TimeSpentSeconds, 0, 86400));
        _logger.LogInformation(
            "PuzzleAttempt: User {UserId} {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight} VizLevel={VizLevel} Elo={EloAfter} ({EloChange:+#;-#;0}) EvalShown={EvalShown} VizShowCount={VizShowCount}",
            userId, dto.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, startedAt, solvedAt, dto.TimeSpentSeconds, dto.ScreenWidth, dto.ScreenHeight, vizLevel, newRating, change, dto.EvalShown, dto.VizShowCount);

        return MapAttemptToDto(attempt, puzzle);
    }

    private static PuzzleAttemptDto MapAttemptToDto(PuzzleAttempt attempt, Puzzle puzzle) => new()
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

    private async Task TrimUserPuzzleAttemptsAsync(int userId, int puzzleId, int vizLevel)
    {
        var count = await _db.PuzzleAttempts.CountAsync(
            a => a.UserId == userId && a.PuzzleId == puzzleId && a.VisualizationLevel == vizLevel);
        if (count <= MaxAttemptsPerUserPuzzleVizLevel) return;
        var stale = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId && a.PuzzleId == puzzleId && a.VisualizationLevel == vizLevel)
            .OrderBy(a => a.AttemptedAt)
            .Take(count - MaxAttemptsPerUserPuzzleVizLevel)
            .ToListAsync();
        _db.PuzzleAttempts.RemoveRange(stale);
        await _db.SaveChangesAsync();
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
            VisualizationLevel = vizLevel,
            EvalShown = dto.EvalShown,
            VizShowCount = Math.Clamp(dto.VizShowCount, 0, 100)
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        await TrimAnonymousAttemptsAsync(sessionId);

        var solvedAt = attempt.AttemptedAt;
        var startedAt = solvedAt.AddSeconds(-Math.Clamp(dto.TimeSpentSeconds, 0, 86400));
        _logger.LogInformation(
            "PuzzleAttempt: Anonymous {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight}",
            dto.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, startedAt, solvedAt, dto.TimeSpentSeconds, dto.ScreenWidth, dto.ScreenHeight);

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

    public async Task<PuzzleStatsDto> GetStatsAsync(int userId, int? vizLevel = null)
    {
        var user = await _db.AppUsers.FindAsync(userId);

        // Ohne explizit angefragtes Level (Dashboard/Übersicht): das vom User am meisten
        // gespielte Level — sonst zeigt das Dashboard stur das Level-0-Elo (Default 1500),
        // obwohl der User z.B. im Blindfold-Level bei 1800 steht.
        var level = vizLevel ?? await GetPrimaryLevelAsync(userId);

        var totalAttempts = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId);
        if (totalAttempts == 0)
            return new PuzzleStatsDto
            {
                PuzzleElo = user != null ? GetEloForLevel(user, level) : GetDefaultElo(level),
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
            PuzzleElo = user != null ? GetEloForLevel(user, level) : GetDefaultElo(level),
            PuzzleEloPerLevel = user != null ? BuildEloDict(user) : null
        };
    }

    /// <summary>Das vom User am meisten gespielte Visualisierungs-Level (0–4); 0 falls keine Versuche.
    /// Liefert das „Haupt-Elo" für Dashboard/Übersicht, statt stur Level 0 (Default 1500) zu zeigen.</summary>
    private async Task<int> GetPrimaryLevelAsync(int userId)
    {
        var top = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.VisualizationLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Level)
            .FirstOrDefaultAsync();
        return top?.Level ?? 0;
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
