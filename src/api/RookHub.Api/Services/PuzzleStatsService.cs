using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Lesende Puzzle-Statistik + verwandte Auswertungen, aus <see cref="PuzzleService"/> ausgegliedert:
/// Konto-/anonyme Stats (Elo/Genauigkeit/Serien), Elo-Verlauf, Versuchs-History, Themen-/Rating-/
/// Aktivitäts-Aufschlüsselung, schwächste Themen, Themen-Liste (gecacht) sowie die „Revenge a Friend"-
/// Abfragen (offene Niederlagen). Enthält zusätzlich das Übernehmen anonymer Versuche auf ein Konto
/// (<see cref="ClaimSessionAsync"/>). Reine <c>AppDbContext</c>-/Cache-Logik; Elo-Mathematik in
/// <see cref="PuzzleElo"/>.
/// </summary>
public class PuzzleStatsService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public PuzzleStatsService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
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

    /// <summary>
    /// Alle vorkommenden Puzzle-Themen, alphabetisch sortiert — Optionen für die Themen-Auswahl.
    /// Quelle ist die normalisierte <c>Tags</c>-Tabelle; ist die (noch) leer (kein Tag-Backfill),
    /// Fallback auf die distinkten Tokens aus <c>Puzzle.Themes</c>. 1 h gecacht (Themenmenge ändert sich kaum).
    /// </summary>
    public async Task<List<string>> GetAllThemesAsync()
    {
        const string cacheKey = "puzzle:all-themes";
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
            return cached;

        var themes = await _db.Tags
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync();

        if (themes.Count == 0)
        {
            // Fallback: Tag-Tabelle noch nicht befüllt → aus den (leerzeichengetrennten) Puzzle.Themes ableiten.
            var raw = await _db.Puzzles
                .Where(p => p.Themes != null && p.Themes != "")
                .Select(p => p.Themes!)
                .ToListAsync();
            themes = raw
                .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }

        _cache.Set(cacheKey, themes, TimeSpan.FromHours(1));
        return themes;
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

    /// <summary>Übernimmt die anonymen Versuche einer Session auf ein Konto (nach Login/Registrierung).</summary>
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
                PuzzleElo = user != null ? PuzzleElo.GetEloForLevel(user, level) : PuzzleElo.GetDefaultElo(level),
                PuzzleEloPerLevel = user != null ? PuzzleElo.BuildEloDict(user) : null
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
            PuzzleElo = user != null ? PuzzleElo.GetEloForLevel(user, level) : PuzzleElo.GetDefaultElo(level),
            PuzzleEloPerLevel = user != null ? PuzzleElo.BuildEloDict(user) : null
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

    /// <summary>Zählt je Freund die OFFENEN Revenge-Puzzle für die Freundesliste (rotes Icon): Puzzles,
    /// an denen der Freund gescheitert ist und die er nie gelöst hat UND die der Aufrufer (<paramref
    /// name="viewerUserId"/>) selbst noch nicht gelöst hat. Liefert nur Freunde mit Count &gt; 0.
    /// Bewusst provider-sicher: einfache Projektionen laden + Aggregation in-memory (Datenmenge ist durch
    /// die Versuche der wenigen Freunde beschränkt) — vermeidet riskante GroupBy/Distinct-SQL-Übersetzung.</summary>
    public async Task<Dictionary<int, int>> GetOpenRevengeCountsAsync(int viewerUserId, IReadOnlyList<int> friendIds)
    {
        var result = new Dictionary<int, int>();
        if (friendIds.Count == 0) return result;

        // Puzzles, die der Aufrufer selbst gelöst hat → keine offene Rechnung mehr.
        var viewerSolved = (await _db.PuzzleAttempts
            .Where(a => a.UserId == viewerUserId && a.Solved)
            .Select(a => a.PuzzleId)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        // Alle Versuche der Freunde (schlanke Projektion) — je Freund in-memory auswerten.
        var rows = await _db.PuzzleAttempts
            .Where(a => a.UserId != null && friendIds.Contains(a.UserId.Value))
            .Select(a => new { UserId = a.UserId!.Value, a.PuzzleId, a.Solved })
            .ToListAsync();

        foreach (var byFriend in rows.GroupBy(r => r.UserId))
        {
            var solvedByFriend = byFriend.Where(r => r.Solved).Select(r => r.PuzzleId).ToHashSet();
            var open = byFriend
                .Where(r => !r.Solved)
                .Select(r => r.PuzzleId)
                .Where(pid => !solvedByFriend.Contains(pid) && !viewerSolved.Contains(pid))
                .Distinct()
                .Count();
            if (open > 0) result[byFriend.Key] = open;
        }
        return result;
    }

    /// <summary>
    /// Standard-Puzzles, an denen <paramref name="targetUserId"/> mindestens einmal gescheitert ist und die
    /// er bis heute NICHT gelöst hat — die „offenen Niederlagen" für „Revenge a Friend". Sortiert nach
    /// jüngstem Fehlversuch. <paramref name="viewerUserId"/> ist der Rächer: pro Puzzle wird vermerkt, ob er
    /// es selbst schon gelöst hat (<see cref="RevengePuzzleDto.SolvedByViewer"/>), damit das Frontend
    /// erledigte von offenen Revanchen trennen kann.
    /// </summary>
    public async Task<List<RevengePuzzleDto>> GetUnsolvedFailuresAsync(int targetUserId, int viewerUserId, int limit = 200)
    {
        limit = Math.Clamp(limit, 1, 500);

        // Puzzle-Ids, die der Target irgendwann gelöst hat → keine offene Rechnung mehr.
        var solvedIds = _db.PuzzleAttempts
            .Where(a => a.UserId == targetUserId && a.Solved)
            .Select(a => a.PuzzleId);

        // Fehlversuche auf nie gelösten Puzzles, je Puzzle aggregiert.
        var failures = await _db.PuzzleAttempts
            .Where(a => a.UserId == targetUserId && !a.Solved && !solvedIds.Contains(a.PuzzleId))
            .GroupBy(a => a.PuzzleId)
            .Select(g => new
            {
                PuzzleId = g.Key,
                FailCount = g.Count(),
                LastFailedAt = g.Max(x => x.AttemptedAt)
            })
            .OrderByDescending(x => x.LastFailedAt)
            .Take(limit)
            .ToListAsync();

        if (failures.Count == 0)
            return new List<RevengePuzzleDto>();

        var ids = failures.Select(f => f.PuzzleId).ToList();
        var puzzles = await _db.Puzzles
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.LichessId, p.Rating, p.Themes })
            .ToDictionaryAsync(p => p.Id);

        // Welche dieser Puzzles hat der Rächer (viewer) selbst schon gelöst?
        var viewerSolved = (await _db.PuzzleAttempts
            .Where(a => a.UserId == viewerUserId && a.Solved && ids.Contains(a.PuzzleId))
            .Select(a => a.PuzzleId)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        return failures
            .Where(f => puzzles.ContainsKey(f.PuzzleId))
            .Select(f =>
            {
                var p = puzzles[f.PuzzleId];
                return new RevengePuzzleDto
                {
                    PuzzleId = p.Id,
                    LichessId = p.LichessId,
                    Rating = p.Rating,
                    Themes = p.Themes,
                    FailCount = f.FailCount,
                    LastFailedAt = f.LastFailedAt,
                    SolvedByViewer = viewerSolved.Contains(p.Id)
                };
            })
            .ToList();
    }
}
