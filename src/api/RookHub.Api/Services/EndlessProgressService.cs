using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Wem gehört ein Endless-Fortschritt: einem KONTO oder einer anonymen Browser-Sitzung. Beide Fälle
/// beantworten dieselben vier Fragen (Stand lesen, Stand speichern, Lauf aufzeichnen, Läufe am Stück
/// einspielen) — vorher stand jede davon ZWEIMAL da und unterschied sich nur im Prädikat. Zwei
/// Unterschiede waren dabei schon eingeschlichen: die anonyme Abfrage sortierte nach <c>Id</c> (Rest
/// aus der Zeit vor dem eindeutigen Index auf <c>AnonymousSessionId</c>) und ließ den Archiv-Filter aus.
///
/// <para>Gegenstück zu <see cref="GuessOwner"/>, wo dieselbe Frage bereits so beantwortet ist.</para>
/// </summary>
public readonly record struct EndlessOwner(int? UserId, string? AnonymousSessionId)
{
    public static EndlessOwner ForUser(int userId) => new(userId, null);

    /// <summary>Anonymer Besitzer. Die Kennung ist ungeprüfte Client-Eingabe — sie MUSS vorher gegen
    /// <see cref="ValidationConstants.SessionIdPattern"/> laufen (Controller), sonst wäre ein kurzer,
    /// erratbarer Wert der Weg in fremde Fortschritte.</summary>
    public static EndlessOwner ForAnonymous(string sessionId) => new(null, sessionId);

    public bool IsAnonymous => UserId is null;
}

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

    // --- Fortschritt (Konto ODER anonyme Sitzung) ---
    //
    // EIN Rumpf je Frage; die acht alten Namen darunter sind Ein-Zeilen-Adapter, damit die Aufrufer
    // (Controller, Tests) unveraendert bleiben. Das Praedikat MUSS ueber die Art verzweigen: ein
    // `p.UserId == owner.UserId` traefe bei einem anonymen Besitzer `p.UserId == null` — also JEDE
    // anonyme Zeile statt genau einer.

    private static System.Linq.Expressions.Expression<Func<EndlessProgress, bool>> ProgressOf(EndlessOwner owner)
    {
        var uid = owner.UserId;
        var sid = owner.AnonymousSessionId;
        return owner.IsAnonymous ? p => p.AnonymousSessionId == sid : p => p.UserId == uid;
    }

    private static System.Linq.Expressions.Expression<Func<EndlessSession, bool>> SessionsOf(EndlessOwner owner)
    {
        var uid = owner.UserId;
        var sid = owner.AnonymousSessionId;
        return owner.IsAnonymous ? s => s.AnonymousSessionId == sid : s => s.UserId == uid;
    }

    public async Task<EndlessSyncResponseDto> GetSyncDataAsync(EndlessOwner owner)
    {
        var progress = await _db.EndlessProgresses.FirstOrDefaultAsync(ProgressOf(owner));

        // Der Archiv-Filter galt bisher nur fuer Konten. Er gilt jetzt fuer beide und ist fuer eine
        // anonyme Sitzung folgenlos: archivieren kann nur ein Konto (ArchiveSessionsAsync ist auf
        // s.UserId eingeschraenkt), eine anonyme Zeile traegt IsArchived also nie.
        var sessions = await _db.EndlessSessions
            .Where(SessionsOf(owner))
            .Where(s => !s.IsArchived)
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

    public async Task<EndlessProgressDto> SaveProgressAsync(EndlessOwner owner, SaveEndlessProgressDto dto)
    {
        var progress = await _db.EndlessProgresses.FirstOrDefaultAsync(ProgressOf(owner));

        var isNew = progress == null;
        if (isNew)
        {
            progress = owner.IsAnonymous
                ? new EndlessProgress { AnonymousSessionId = owner.AnonymousSessionId }
                : new EndlessProgress { UserId = owner.UserId };
            _db.EndlessProgresses.Add(progress);
        }

        ApplyProgressDto(progress!, dto);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (isNew && AuthService.IsUniqueViolation(ex))
        {
            // Race: ein paralleler Request hat die Zeile zwischen Read und Insert angelegt (eindeutiger
            // Index auf UserId bzw. AnonymousSessionId). Statt 500/Lost-Update die nun vorhandene Zeile
            // laden und das Update darauf anwenden.
            _db.ChangeTracker.Clear();
            progress = await _db.EndlessProgresses.FirstAsync(ProgressOf(owner));
            ApplyProgressDto(progress, dto);
            await _db.SaveChangesAsync();
        }
        return MapProgressDto(progress!);
    }

    // --- Sessions ---

    /// <summary>EINE Stelle, die aus dem DTO eine <see cref="EndlessSession"/> baut — vorher vier
    /// Copy-Paste-Initialisierer, von denen die Bulk-Import-Kopien still PuzzleAttemptsJson
    /// wegließen (importierte Läufe hatten keine Detail-Ansicht, falls der Client Puzzles mitschickt).</summary>
    private static EndlessSession BuildSession(RecordEndlessSessionDto dto, int? userId, string? anonymousSessionId) => new()
    {
        UserId = userId,
        AnonymousSessionId = anonymousSessionId,
        Timestamp = dto.Timestamp,
        TotalSolved = dto.TotalSolved,
        MaxRating = dto.MaxRating,
        DurationSeconds = dto.DurationSeconds,
        ConfigJson = dto.ConfigJson,
        MistakeAtRatings = dto.MistakeAtRatings,
        Seed = dto.Seed,
        ChainPuzzleIds = dto.ChainPuzzleIds,
        // Spielweise des Laufs; unbekannt/fehlend → "training" (Altbestand-Verhalten).
        Mode = SolveMode.Normalize(dto.Mode),
        PuzzleAttemptsJson = SerializeAttempts(dto.Puzzles)
    };

    public async Task<EndlessSessionDto> RecordSessionAsync(EndlessOwner owner, RecordEndlessSessionDto dto)
    {
        var session = BuildSession(dto, owner.UserId, owner.AnonymousSessionId);
        _db.EndlessSessions.Add(session);
        await _db.SaveChangesAsync();

        LogSessionPuzzles(owner.UserId, dto.Puzzles);
        // Strukturierter Event fuer Kibana: Runs/Tag, Ø/Max geloeste Puzzles, Max-Rating,
        // Leaderboard (Cardinality/Terms auf fields.UserId). Analog zum PuzzleAttempt-Log.
        if (owner.IsAnonymous)
            _logger.LogInformation(
                "EndlessSessionCompleted: Anonymous solved {TotalSolved} maxRating {MaxRating} in {DurationSeconds}s",
                dto.TotalSolved, dto.MaxRating, dto.DurationSeconds);
        else
            _logger.LogInformation(
                "EndlessSessionCompleted: User {UserId} solved {TotalSolved} maxRating {MaxRating} in {DurationSeconds}s",
                owner.UserId, dto.TotalSolved, dto.MaxRating, dto.DurationSeconds);

        await TrimIfAnonymousAsync(owner);
        return MapSessionDto(session);
    }

    public async Task<int> BulkImportSessionsAsync(EndlessOwner owner, List<RecordEndlessSessionDto> dtos)
    {
        var count = 0;
        foreach (var dto in dtos)
        {
            _db.EndlessSessions.Add(BuildSession(dto, owner.UserId, owner.AnonymousSessionId));
            count++;
        }
        await _db.SaveChangesAsync();
        await TrimIfAnonymousAsync(owner);
        return count;
    }

    /// <summary>Nur anonyme Laeufe werden gedeckelt — die eines Kontos bleiben unbegrenzt.</summary>
    private Task TrimIfAnonymousAsync(EndlessOwner owner)
        => owner.IsAnonymous ? TrimAnonymousSessionsAsync(owner.AnonymousSessionId!) : Task.CompletedTask;

    // --- Adapter auf die beiden Besitzer-Arten (ein Ausdruck, kein zweiter Weg) ---

    public Task<EndlessSyncResponseDto> GetSyncDataAsync(int userId)
        => GetSyncDataAsync(EndlessOwner.ForUser(userId));

    public Task<EndlessSyncResponseDto> GetAnonymousSyncDataAsync(string sessionId)
        => GetSyncDataAsync(EndlessOwner.ForAnonymous(sessionId));

    public Task<EndlessProgressDto> SaveProgressAsync(int userId, SaveEndlessProgressDto dto)
        => SaveProgressAsync(EndlessOwner.ForUser(userId), dto);

    public Task<EndlessProgressDto> SaveAnonymousProgressAsync(string sessionId, SaveEndlessProgressDto dto)
        => SaveProgressAsync(EndlessOwner.ForAnonymous(sessionId), dto);

    public Task<EndlessSessionDto> RecordAnonymousSessionAsync(string sessionId, RecordEndlessSessionDto dto)
        => RecordSessionAsync(EndlessOwner.ForAnonymous(sessionId), dto);

    public Task<int> BulkImportAnonymousSessionsAsync(string sessionId, List<RecordEndlessSessionDto> dtos)
        => BulkImportSessionsAsync(EndlessOwner.ForAnonymous(sessionId), dtos);

    public Task<EndlessSessionDto> RecordSessionAsync(int userId, RecordEndlessSessionDto dto)
        => RecordSessionAsync(EndlessOwner.ForUser(userId), dto);

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

    public Task<int> BulkImportSessionsAsync(int userId, List<RecordEndlessSessionDto> dtos)
        => BulkImportSessionsAsync(EndlessOwner.ForUser(userId), dtos);

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
        return transferred;   // eingeloggte Sessions werden nicht getrimmt (unbegrenzt)
    }

    // --- History ---

    public async Task<EndlessHistoryResponseDto> GetSessionHistoryAsync(int userId, int page, int pageSize, bool? archived = null)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var query = _db.EndlessSessions.Where(s => s.UserId == userId);
        if (archived.HasValue)
            query = query.Where(s => s.IsArchived == archived.Value);
        var totalCount = await query.CountAsync();
        // Läufe je Spielweise über den GANZEN gefilterten Bestand (nicht nur die Seite): nur die
        // ausdrücklich als „easy" markierten zählen, alles andere (inkl. Altbestand) ist „training".
        var easyCount = await query.CountAsync(s => s.Mode == SolveMode.Easy);
        var (trainingCount, easy) = SolveMode.Split(totalCount, easyCount);

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
            PageSize = pageSize,
            TrainingCount = trainingCount,
            EasyCount = easy
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
            Mode = session.Mode,
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

    /// <summary>Kappt anonyme Sessions auf <see cref="MaxSessions"/>. NUR anonyme — eingeloggte
    /// Nutzer haben unbegrenzte Sessions (die früheren TrimSessionsAsync(userId:)-Aufrufe waren
    /// No-ops und täuschten eine Kappung vor, die es nie gab).</summary>
    private async Task TrimAnonymousSessionsAsync(string anonymousSessionId)
    {
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
        IsArchived = s.IsArchived,
        Mode = s.Mode
    };
}
