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
    private readonly PuzzleTaggingService _tagging;
    private bool _puzzleTagsReady;   // pro Request gecacht: ist die PuzzleTags-Tabelle befüllt?

    // Obergrenze anonymer Versuche pro Session — verhindert unbegrenztes
    // Anwachsen der PuzzleAttempts-Tabelle durch eine einzelne (anonyme) Session.
    private const int MaxAnonymousAttemptsPerSession = 200;
    // Obergrenze auth. Versuche pro (User, Puzzle, VizLevel) — kein unbegrenztes Tabellenwachstum.
    private const int MaxAttemptsPerUserPuzzleVizLevel = 20;

    public PuzzleService(AppDbContext db, IMemoryCache cache, ILogger<PuzzleService> logger, PuzzleTaggingService tagging)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _tagging = tagging;
    }

    /// <param name="themes">Leerzeichengetrennt; Puzzle muss ALLE enthalten (UND-Verknüpfung).</param>
    /// <param name="themesAny">Leerzeichengetrennt; Puzzle muss MINDESTENS EINS enthalten (ODER-Verknüpfung).
    /// Für „5 schwächste Themen trainieren" — ein Puzzle trägt selten alle Schwächen-Themen gleichzeitig.</param>
    public async Task<PuzzleDto?> GetRandomAsync(int? userId, int? minRating, int? maxRating, string? themes, bool excludeSolved,
        string? themesAny = null, IReadOnlyCollection<int>? excludeIds = null)
    {
        // Schnellpfad: reiner ODER-Themenfilter über die normalisierte Tag-Tabelle (Index (TagId,Rating))
        // statt LIKE-Full-Scan. Nur wenn PuzzleTags befüllt ist (sonst Fallback auf LIKE unten).
        if (!string.IsNullOrEmpty(themesAny) && string.IsNullOrEmpty(themes) && await HasPuzzleTagsAsync())
        {
            var cands = await TagCandidatesAsync(themesAny, minRating, maxRating, excludeSolved, userId, excludeIds);
            if (cands.Count == 0) return null;
            var pickId = cands[Random.Shared.Next(cands.Count)].Id;
            var picked = await _db.Puzzles.FirstOrDefaultAsync(p => p.Id == pickId);
            return picked == null ? null : MapToDto(picked);
        }

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
    /// Theme-Batch: je Rating-Fenster GENAU EIN zufälliges, im Batch eindeutiges Treffer-Puzzle,
    /// danach die gewählten Puzzles EINMAL nachladen (Reihenfolge = Fensterreihenfolge).
    /// </summary>
    private async Task<List<PuzzleDto>> GetThemedBatchAsync(int? userId,
        List<(int Min, int Max)> windows, string? themes, string? themesAny, bool excludeSolved)
    {
        List<int> chosenIds;
        if (!string.IsNullOrEmpty(themesAny) && string.IsNullOrEmpty(themes) && await HasPuzzleTagsAsync())
        {
            // Schneller Index-Pfad: pro Fenster ein gezielter Random-Seek über (TagId, Rating) mit
            // LIMIT 1 — NICHT die gesamte Spanne materialisieren (bei häufigen Tags Millionen Zeilen).
            chosenIds = await PickTaggedPerWindowAsync(themesAny, windows, excludeSolved, userId);
        }
        else
        {
            // Fallback (manuelle UND-Themen oder PuzzleTags noch nicht befüllt): LIKE-Scan über die
            // Gesamtspanne (1 Scan), dann in-memory je Fenster wählen. Kein Index → kein Per-Fenster-Seek.
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
            var candidates = (await q.Select(p => new { p.Id, p.Rating }).ToListAsync())
                .Select(r => (r.Id, r.Rating)).ToList();
            var used = new HashSet<int>();
            chosenIds = new List<int>();
            foreach (var (min, max) in windows)
            {
                var pool = candidates.Where(c => c.Rating >= min && c.Rating <= max && !used.Contains(c.Id)).ToList();
                if (pool.Count == 0) continue;
                var pick = pool[Random.Shared.Next(pool.Count)].Id;
                used.Add(pick);
                chosenIds.Add(pick);
            }
        }
        if (chosenIds.Count == 0) return new List<PuzzleDto>();

        var puzzles = await _db.Puzzles.Where(p => chosenIds.Contains(p.Id)).ToListAsync();   // 1 Nachladen
        var byId = puzzles.ToDictionary(p => p.Id);
        return chosenIds.Where(byId.ContainsKey).Select(id => MapToDto(byId[id])).ToList();   // Fensterreihenfolge
    }

    /// <summary>
    /// Wählt je Rating-Fenster EIN zufälliges, im Batch eindeutiges Tag-Puzzle über den Index
    /// (TagId, Rating) per Random-Seek + LIMIT 1 — statt die gesamte Kandidatenmenge zu laden.
    /// Reihenfolge der Rückgabe folgt den Fenstern; Fenster ohne Treffer entfallen.
    /// </summary>
    private async Task<List<int>> PickTaggedPerWindowAsync(string themesAny,
        List<(int Min, int Max)> windows, bool excludeSolved, int? userId)
    {
        var names = themesAny.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
        if (names.Count == 0) return new List<int>();
        var tagIds = await _db.Tags.Where(t => names.Contains(t.Name)).Select(t => t.Id).ToListAsync();
        if (tagIds.Count == 0) return new List<int>();

        var used = new HashSet<int>();
        var chosen = new List<int>();
        foreach (var (min, max) in windows)
        {
            var id = await SeekTaggedIdAsync(tagIds, min, max, excludeSolved, userId, used);
            if (id.HasValue && used.Add(id.Value))
                chosen.Add(id.Value);
        }
        return chosen;
    }

    /// <summary>
    /// Ein zufälliges Tag-Puzzle im Fenster [min,max]: Zufalls-Rating r ziehen, dann die Tags in
    /// zufälliger Reihenfolge durchgehen und je Tag per Index-Seek (TagId, Rating) das nächste Puzzle
    /// ab r holen. Pro Tag ist die Abfrage ein reiner Index-Range-Read (rating-sortiert) + LIMIT 1 —
    /// KEIN Filesort über eine IN-Liste (das machte häufige Tags langsam).
    /// </summary>
    private async Task<int?> SeekTaggedIdAsync(List<int> tagIds, int min, int max,
        bool excludeSolved, int? userId, ISet<int> used)
    {
        var r = Random.Shared.Next(min, max + 1);
        foreach (var tagId in tagIds.OrderBy(_ => Random.Shared.Next()))
        {
            var id = await SeekOneTagAsync(tagId, r, min, max, excludeSolved, userId, used);
            if (id.HasValue) return id;
        }
        return null;
    }

    /// <summary>Nächstes Puzzle EINES Tags im Fenster: vorwärts ab r (sonst rückwärts), Index-Seek + LIMIT 1.</summary>
    private async Task<int?> SeekOneTagAsync(int tagId, int r, int min, int max,
        bool excludeSolved, int? userId, ISet<int> used)
    {
        var q = _db.PuzzleTags.Where(pt => pt.TagId == tagId);
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts.Where(a => a.UserId == uid && a.Solved).Select(a => a.PuzzleId);
            q = q.Where(pt => !solvedIds.Contains(pt.PuzzleId));
        }
        if (used.Count > 0)
            q = q.Where(pt => !used.Contains(pt.PuzzleId));

        var fwd = await q.Where(pt => pt.Rating >= r && pt.Rating <= max)
            .OrderBy(pt => pt.Rating).Select(pt => (int?)pt.PuzzleId).FirstOrDefaultAsync();
        if (fwd.HasValue) return fwd;
        return await q.Where(pt => pt.Rating >= min && pt.Rating < r)
            .OrderByDescending(pt => pt.Rating).Select(pt => (int?)pt.PuzzleId).FirstOrDefaultAsync();
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

    /// <summary>Ist die normalisierte PuzzleTags-Tabelle befüllt? (pro Request gecacht)</summary>
    private async Task<bool> HasPuzzleTagsAsync()
    {
        if (_puzzleTagsReady) return true;
        _puzzleTagsReady = await _db.PuzzleTags.AnyAsync();
        return _puzzleTagsReady;
    }

    /// <summary>
    /// Kandidaten (PuzzleId + Rating) für einen ODER-Themenfilter über die normalisierte Tag-Tabelle.
    /// Nutzt den Index (TagId, Rating) → reiner Index-Range-Scan statt LIKE-Full-Scan.
    /// </summary>
    private async Task<List<(int Id, int Rating)>> TagCandidatesAsync(string themesAny,
        int? minRating, int? maxRating, bool excludeSolved, int? userId, IReadOnlyCollection<int>? excludeIds)
    {
        var names = themesAny.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
        if (names.Count == 0) return new List<(int, int)>();
        var tagIds = await _db.Tags.Where(t => names.Contains(t.Name)).Select(t => t.Id).ToListAsync();
        if (tagIds.Count == 0) return new List<(int, int)>();

        var q = _db.PuzzleTags.Where(pt => tagIds.Contains(pt.TagId));
        if (minRating.HasValue) q = q.Where(pt => pt.Rating >= minRating.Value);
        if (maxRating.HasValue) q = q.Where(pt => pt.Rating <= maxRating.Value);
        if (excludeIds is { Count: > 0 }) q = q.Where(pt => !excludeIds.Contains(pt.PuzzleId));
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts.Where(a => a.UserId == uid && a.Solved).Select(a => a.PuzzleId);
            q = q.Where(pt => !solvedIds.Contains(pt.PuzzleId));
        }
        // Ein Puzzle kann mehrere der gesuchten Tags tragen → DISTINCT auf (PuzzleId, Rating).
        var rows = await q.Select(pt => new { pt.PuzzleId, pt.Rating }).Distinct().ToListAsync();
        return rows.Select(r => (r.PuzzleId, r.Rating)).ToList();
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
        // Nur bei GLEICHEM Ergebnis (Solved): ein echter Zweitversuch mit anderem Ausgang
        // (Fail → sofort neu laden → Solve) ist kein Doppel-Submit und muss gespeichert werden,
        // sonst bleibt das Puzzle trotz Lösung „ungelöst" (excludeSolved/Leaderboards/Streaks).
        var idempotencyCutoff = DateTime.UtcNow.AddSeconds(-30);
        var duplicate = await _db.PuzzleAttempts
            .Where(a => a.UserId == userId && a.PuzzleId == puzzleId
                     && a.VisualizationLevel == vizLevel && a.Solved == dto.Solved
                     && a.AttemptedAt >= idempotencyCutoff)
            .OrderByDescending(a => a.AttemptedAt)
            .FirstOrDefaultAsync();
        if (duplicate != null)
            return MapAttemptToDto(duplicate, puzzle);

        var currentElo = PuzzleElo.GetEloForLevel(user, vizLevel);
        // Provisorische Elo-Kalibrierung: am Konto-/Level-Anfang größere Schritte (in BEIDE Richtungen
        // — der K-Faktor skaliert Gewinn wie Verlust), damit man schnell beim passenden Niveau landet.
        // Gegated auf gelöste UND gescheiterte Versuche je vizLevel (siehe ProvisionalKFactor).
        var solvedCount = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.VisualizationLevel == vizLevel && a.Solved);
        var failedCount = await _db.PuzzleAttempts.CountAsync(a => a.UserId == userId && a.VisualizationLevel == vizLevel && !a.Solved);
        var kFactor = PuzzleElo.ProvisionalKFactor(solvedCount, failedCount);

        // Elo nur beim ersten Versuch für dieses Puzzle aktualisieren — verhindert Elo-Inflation.
        var isFirstAttempt = !await _db.PuzzleAttempts.AnyAsync(
            a => a.UserId == userId && a.PuzzleId == puzzleId && a.VisualizationLevel == vizLevel);
        int newRating, change;
        if (isFirstAttempt)
        {
            (newRating, change) = PuzzleElo.CalculateElo(currentElo, puzzle.Rating, dto.Solved, kFactor);
            PuzzleElo.SetEloForLevel(user, vizLevel, newRating);
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
            VizShowCount = Math.Clamp(dto.VizShowCount, 0, 100),
            HintsUsed = Math.Clamp(dto.HintsUsed, 0, 3)
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
            VizShowCount = Math.Clamp(dto.VizShowCount, 0, 100),
            HintsUsed = Math.Clamp(dto.HintsUsed, 0, 3)
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        await TrimAnonymousAttemptsAsync(sessionId);

        var solvedAt = attempt.AttemptedAt;
        var startedAt = solvedAt.AddSeconds(-Math.Clamp(dto.TimeSpentSeconds, 0, 86400));
        _logger.LogInformation(
            "PuzzleAttempt: Anonymous {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight}",
            dto.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, startedAt, solvedAt, dto.TimeSpentSeconds, dto.ScreenWidth, dto.ScreenHeight);

        // Gleicher Mapper wie die eingeloggten Pfade (EloAfter/EloChange sind beim anonymen
        // Versuch ohnehin nie gesetzt → null) — sonst fehlt jedes künftig ergänzte DTO-Feld
        // still nur in der anonymen Antwort.
        return MapAttemptToDto(attempt, puzzle);
    }


    public async Task<int> ImportFromCsvAsync(Stream csvStream, int? minRating, int? maxRating, int? maxCount, CancellationToken ct = default)
    {
        var existingIds = await _db.Puzzles.Select(p => p.LichessId).ToHashSetAsync(ct);
        var tagCache = await _db.Tags.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
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
                await _tagging.SyncPuzzleTagsAsync(batch, tagCache, ct);   // Tag/PuzzleTag mitpflegen
                _db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.Puzzles.AddRange(batch);
            await _db.SaveChangesAsync(ct);
            await _tagging.SyncPuzzleTagsAsync(batch, tagCache, ct);
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
        GameUrl = p.GameUrl,
        HintsFlagged = p.HintsFlagged
    };

    /// <summary>Markiert/entmarkiert die (on-the-fly-)Tipps eines Standard-Puzzles als „dumm/schlecht".
    /// Liefert false, wenn das Puzzle nicht existiert.</summary>
    public async Task<bool> FlagHintsAsync(int puzzleId, bool flagged)
    {
        var p = await _db.Puzzles.FindAsync(puzzleId);
        if (p == null) return false;
        p.HintsFlagged = flagged;
        await _db.SaveChangesAsync();
        return true;
    }
}
