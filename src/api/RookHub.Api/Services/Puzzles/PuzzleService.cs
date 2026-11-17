using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.DTOs.Puzzles;

namespace RookHub.Api.Services.Puzzles;

public class PuzzleService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PuzzleService> _logger;
    private readonly PuzzleTaggingService _tagging;
    /// <summary>Challenge/Revanche nach einem gespeicherten Versuch (N9-001); <c>null</c> (Tests) = aus.</summary>
    private readonly ChallengeService? _challenges;
    private readonly RevengeNotificationService? _revenge;
    private bool _puzzleTagsReady;   // pro Request gecacht: ist die PuzzleTags-Tabelle befüllt?

    // Obergrenze anonymer Versuche pro Session — verhindert unbegrenztes
    // Anwachsen der PuzzleAttempts-Tabelle durch eine einzelne (anonyme) Session.
    private const int MaxAnonymousAttemptsPerSession = 200;
    // Obergrenze auth. Versuche pro (User, Puzzle, VizLevel) — kein unbegrenztes Tabellenwachstum.
    private const int MaxAttemptsPerUserPuzzleVizLevel = 20;

    public PuzzleService(AppDbContext db, IMemoryCache cache, ILogger<PuzzleService> logger, PuzzleTaggingService tagging,
        ChallengeService? challenges = null, RevengeNotificationService? revenge = null)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _tagging = tagging;
        _challenges = challenges;
        _revenge = revenge;
    }

    /// <param name="themes">Leerzeichengetrennt; Puzzle muss ALLE enthalten (UND-Verknüpfung, Teilstring des Themes-Texts).</param>
    /// <param name="themesAny">Leerzeichengetrennt; Puzzle muss MINDESTENS EINS tragen (ODER-Verknüpfung, exakter Tag-Name).
    /// Für „5 schwächste Themen trainieren" — ein Puzzle trägt selten alle Schwächen-Themen gleichzeitig.</param>
    public async Task<PuzzleDto?> GetRandomAsync(int? userId, int? minRating, int? maxRating, string? themes, bool excludeSolved,
        string? themesAny = null, IReadOnlyCollection<int>? excludeIds = null)
    {
        var hasThemeFilter = !string.IsNullOrEmpty(themes) || !string.IsNullOrEmpty(themesAny);
        if (hasThemeFilter && !await HasPuzzleTagsAsync())
            return await GetRandomByLikeScanAsync(userId, minRating, maxRating, themes, themesAny, excludeSolved, excludeIds);

        // Index-Seeks mit LIMIT 1 statt MIN/MAX über den Rating-Bereich bzw. LIKE-Scan (siehe PuzzleRandomPicker).
        var picker = await PuzzleRandomPicker.CreateAsync(_db, _cache, themes, themesAny, userId, excludeSolved);
        if (picker == null) return null;
        var exclude = excludeIds is { Count: > 0 } ? new HashSet<int>(excludeIds) : new HashSet<int>();
        var pickId = await picker.PickAsync(minRating, maxRating, exclude);
        if (pickId == null) return null;
        var picked = await _db.Puzzles.FirstOrDefaultAsync(p => p.Id == pickId.Value);
        return picked == null ? null : MapToDto(picked);
    }

    /// <summary>
    /// Rückfall bei Themen-Filter OHNE befüllte <c>PuzzleTags</c>-Tabelle (frische Installation vor dem Backfill):
    /// die durch Rating eingegrenzte Treffer-Id-Liste einmal laden und zufällig wählen.
    /// </summary>
    private async Task<PuzzleDto?> GetRandomByLikeScanAsync(int? userId, int? minRating, int? maxRating,
        string? themes, string? themesAny, bool excludeSolved, IReadOnlyCollection<int>? excludeIds)
    {
        var query = _db.Puzzles.AsQueryable();
        if (minRating.HasValue)
            query = query.Where(p => p.Rating >= minRating.Value);
        if (maxRating.HasValue)
            query = query.Where(p => p.Rating <= maxRating.Value);
        query = ApplyThemeFilters(query, themes, themesAny);
        if (excludeSolved && userId.HasValue)
        {
            var uid = userId.Value;
            var solvedIds = _db.PuzzleAttempts
                .Where(a => a.UserId == uid && a.Solved)
                .Select(a => a.PuzzleId);
            query = query.Where(p => !solvedIds.Contains(p.Id));
        }
        if (excludeIds is { Count: > 0 })
            query = query.Where(p => !excludeIds.Contains(p.Id));

        var ids = await query.Select(p => p.Id).ToListAsync();
        if (ids.Count == 0) return null;
        var pickId = ids[Random.Shared.Next(ids.Count)];
        var picked = await _db.Puzzles.FirstOrDefaultAsync(p => p.Id == pickId);
        return picked == null ? null : MapToDto(picked);
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
        if (winList.Count == 0) return new List<PuzzleDto>();

        var hasThemeFilter = !string.IsNullOrEmpty(themes) || !string.IsNullOrEmpty(themesAny);
        List<int> chosenIds;
        if (hasThemeFilter && !await HasPuzzleTagsAsync())
        {
            chosenIds = await PickByLikeScanAsync(userId, winList, themes, themesAny, excludeSolved);
        }
        else
        {
            // Je Fenster Probe + Index-Seek (PuzzleRandomPicker): 40 Fenster ≈ 40–80 Abfragen à ~1 ms statt
            // MIN/MAX über den Rating-Bereich bzw. LIKE-Scan über die Gesamtspanne (gemessen bis 30 s).
            chosenIds = new List<int>();
            var picker = await PuzzleRandomPicker.CreateAsync(_db, _cache, themes, themesAny, userId, excludeSolved);
            if (picker != null)
            {
                var used = new HashSet<int>();
                foreach (var (min, max) in winList)
                {
                    // Defense in depth zur DTO-Validierung: unsinnige Fenster entfallen still.
                    if (max < min || max == int.MaxValue) continue;
                    var id = await picker.PickAsync(min, max, used);
                    if (id.HasValue && used.Add(id.Value))
                        chosenIds.Add(id.Value);
                }
            }
        }
        if (chosenIds.Count == 0) return new List<PuzzleDto>();

        var puzzles = await _db.Puzzles.Where(p => chosenIds.Contains(p.Id)).ToListAsync();   // 1 Nachladen
        var byId = puzzles.ToDictionary(p => p.Id);
        return chosenIds.Where(byId.ContainsKey).Select(id => MapToDto(byId[id])).ToList();   // Fensterreihenfolge
    }

    /// <summary>
    /// Rückfall für Themen ohne befüllte <c>PuzzleTags</c>: LIKE-Scan über die Gesamtspanne (1 Scan), dann in-memory
    /// je Fenster wählen. Nur für kleine, frisch importierte Bestände gedacht.
    /// </summary>
    private async Task<List<int>> PickByLikeScanAsync(int? userId, List<(int Min, int Max)> windows,
        string? themes, string? themesAny, bool excludeSolved)
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
        var candidates = (await q.Select(p => new { p.Id, p.Rating }).ToListAsync())
            .Select(r => (r.Id, r.Rating)).ToList();
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
        return chosenIds;
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
        // ODER-Themen als ganze Tokens — dieselbe Treffermenge wie der exakte Tag-Vergleich im Index-Pfad.
        var anyNames = (themesAny ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (anyNames.Count > 0)
            query = query.Where(PuzzleRandomPicker.AnyTokenLike(anyNames));
        return query;
    }

    /// <summary>Ist die normalisierte PuzzleTags-Tabelle befüllt? (pro Request gecacht)</summary>
    private async Task<bool> HasPuzzleTagsAsync()
    {
        if (_puzzleTagsReady) return true;
        _puzzleTagsReady = await _db.PuzzleTags.AnyAsync();
        return _puzzleTagsReady;
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

        // Zeit/Tipps/Startzeit: eine Normalisierung für alle Recorder (siehe AttemptRecording). Die
        // Spielweise wird hier NICHT gespeichert (sie ergibt sich aus VisualizationLevel, siehe
        // MapAttemptToDto) — sie steht trotzdem richtig im Kern statt als falscher Vorgabewert.
        var core = AttemptRecording.From(
            dto.Solved, dto.TimeSpentSeconds, dto.HintsUsed,
            vizLevel > 0 ? SolveMode.Training : SolveMode.Easy);

        var attempt = new PuzzleAttempt
        {
            UserId = userId,
            PuzzleId = puzzleId,
            Solved = core.Solved,
            TimeSpentSeconds = core.TimeSeconds,
            AttemptedAt = core.AttemptedAt,
            MoveLog = dto.MoveLog,
            EloAfter = newRating,
            EloChange = change,
            VisualizationLevel = vizLevel,
            EvalShown = dto.EvalShown,
            VizShowCount = Math.Clamp(dto.VizShowCount, 0, 100),
            HintsUsed = core.HintsUsed
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        // Tabellenwachstum begrenzen: älteste Versuche entfernen wenn Limit überschritten.
        await TrimUserPuzzleAttemptsAsync(userId, puzzleId, vizLevel);

        _logger.LogInformation(
            "PuzzleAttempt: User {UserId} {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight} VizLevel={VizLevel} Elo={EloAfter} ({EloChange:+#;-#;0}) EvalShown={EvalShown} VizShowCount={VizShowCount}",
            userId, core.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, core.StartedAt, core.AttemptedAt, core.TimeSeconds, dto.ScreenWidth, dto.ScreenHeight, vizLevel, newRating, change, dto.EvalShown, dto.VizShowCount);

        await AfterAttemptForFriendsAsync(userId, puzzleId, core.Solved, core.TimeSeconds, dto.RevengeUserId);

        return MapAttemptToDto(attempt, puzzle);
    }

    /// <summary>Folgen eines gespeicherten Versuchs für Freunde (Codereview N9-001): offene Challenges an den Nutzer für
    /// dieses Puzzle abschließen und — schickt der Client <c>revengeUserId</c> mit — die Revanche-Glocke anlegen. Vorher
    /// kamen beide Meldungen als eigene Anfragen gleichzeitig mit dem Versuch an und prüften, bevor er gespeichert war:
    /// echte Lösungen wurden als „nicht gelöst" gebucht, die Revanche-Glocke fiel aus. Best effort: der Versuch ist
    /// gespeichert, ein Fehler hier darf ihn nicht als gescheitert melden (der Client legte ihn sonst offline erneut ab).</summary>
    private async Task AfterAttemptForFriendsAsync(int userId, int puzzleId, bool solved, int timeSeconds, int? revengeUserId)
    {
        try
        {
            if (_challenges != null)
                await _challenges.ResolveFromAttemptAsync(userId, PuzzleSource.Standard, puzzleId, solved, timeSeconds);
            if (_revenge != null && revengeUserId is int targetId)
                await _revenge.RecordAsync(userId, targetId, puzzleId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ChallengeAfterAttempt: Challenge/Revanche zu Puzzle {PuzzleId} von User {UserId} nicht verbucht",
                puzzleId, userId);
        }
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
        VisualizationLevel = attempt.VisualizationLevel,
        // Spielweise ABGELEITET (nicht gespeichert): Stufe 0 = Figuren ziehbar → „easy".
        Mode = attempt.VisualizationLevel > 0 ? SolveMode.Training : SolveMode.Easy
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
        // Dieselbe Normalisierung wie im eingeloggten Pfad (siehe AttemptRecording).
        var core = AttemptRecording.From(
            dto.Solved, dto.TimeSpentSeconds, dto.HintsUsed,
            vizLevel > 0 ? SolveMode.Training : SolveMode.Easy);

        var attempt = new PuzzleAttempt
        {
            UserId = null,
            AnonymousSessionId = sessionId,
            PuzzleId = puzzleId,
            Solved = core.Solved,
            TimeSpentSeconds = core.TimeSeconds,
            AttemptedAt = core.AttemptedAt,
            MoveLog = dto.MoveLog,
            VisualizationLevel = vizLevel,
            EvalShown = dto.EvalShown,
            VizShowCount = Math.Clamp(dto.VizShowCount, 0, 100),
            HintsUsed = core.HintsUsed
        };

        _db.PuzzleAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        await TrimAnonymousAttemptsAsync(sessionId);

        _logger.LogInformation(
            "PuzzleAttempt: Anonymous {Result} puzzle {PuzzleId} (LichessId={LichessId}, Rating={PuzzleRating}) StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSpentSeconds}s Screen={ScreenWidth}x{ScreenHeight}",
            core.Solved ? "solved" : "failed", puzzleId, puzzle.LichessId, puzzle.Rating, core.StartedAt, core.AttemptedAt, core.TimeSeconds, dto.ScreenWidth, dto.ScreenHeight);

        // Gleicher Mapper wie die eingeloggten Pfade (EloAfter/EloChange sind beim anonymen
        // Versuch ohnehin nie gesetzt → null) — sonst fehlt jedes künftig ergänzte DTO-Feld
        // still nur in der anonymen Antwort.
        return MapAttemptToDto(attempt, puzzle);
    }


    /// <summary>
    /// CSV-Import im Lichess-Format (<c>PuzzleId,FEN,Moves,Rating,…</c>). Die Zugfolge jeder Zeile wird auf der FEN
    /// nachgespielt (<see cref="EngineBroker.WorkSanitizer.PositionError"/> — dieselbe strenge Prüfung wie vor der
    /// Engine); passt sie nicht, wird die Zeile verworfen und gezählt. Vorher kamen solche Puzzles still durch, und
    /// der Solver blieb beim Setup-Zug für immer auf „Watch the opponent's move…" stehen (TODO 2026-09-13).
    /// </summary>
    public async Task<PuzzleCsvImportResultDto> ImportFromCsvAsync(Stream csvStream, int? minRating, int? maxRating, int? maxCount, CancellationToken ct = default)
    {
        var existingIds = await _db.Puzzles.Select(p => p.LichessId).ToHashSetAsync(ct);
        var tagCache = await _db.Tags.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
        var imported = 0;
        var result = new PuzzleCsvImportResultDto();
        var lineNumber = 0;
        var batch = new List<Puzzle>();

        using var reader = new StreamReader(csvStream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            var lichessId = parts[0].Trim();
            if (existingIds.Contains(lichessId)) continue;

            if (!int.TryParse(parts[3].Trim(), out var rating)) continue;

            if (minRating.HasValue && rating < minRating.Value) continue;
            if (maxRating.HasValue && rating > maxRating.Value) continue;

            var fen = parts[1].Trim();
            var moves = parts[2].Trim();
            var moveList = moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var reason = moveList.Length == 0 ? "no moves" : EngineBroker.WorkSanitizer.PositionError(fen, moveList);
            if (reason is not null)
            {
                result.RejectedInvalidMoves++;
                if (result.RejectedLineNumbers.Count < PuzzleCsvImportResultDto.MaxRejectedLineNumbers)
                    result.RejectedLineNumbers.Add(lineNumber);
                _logger.LogDebug("Puzzle-Import: Zeile {Line} ({LichessId}) verworfen: {Reason}",
                    lineNumber, lichessId, reason);
                continue;
            }

            var puzzle = new Puzzle
            {
                LichessId = lichessId,
                Fen = fen,
                Moves = moves,
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

        if (result.RejectedInvalidMoves > 0)
            _logger.LogWarning("Puzzle-Import: {Rejected} Zeilen mit ungueltiger Zugfolge verworfen (erste: {Lines})",
                result.RejectedInvalidMoves, string.Join(", ", result.RejectedLineNumbers));
        result.Imported = imported;
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
