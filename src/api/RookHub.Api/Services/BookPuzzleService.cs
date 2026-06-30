using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Geschäftslogik rund um Buch-/Tagespuzzles (vormals inline im BookPuzzleController).
/// Wirft <see cref="KeyNotFoundException"/> (→ 404) bzw. <see cref="InvalidOperationException"/>
/// (→ 400); die HTTP-Abbildung übernimmt der Controller.
/// </summary>
public class BookPuzzleService
{
    private readonly AppDbContext _db;
    private readonly ILogger<BookPuzzleService> _logger;
    private readonly IWebhookTaskQueue _bgQueue;

    public BookPuzzleService(AppDbContext db, ILogger<BookPuzzleService> logger, IWebhookTaskQueue bgQueue)
    {
        _db = db;
        _logger = logger;
        _bgQueue = bgQueue;
    }

    private static readonly Regex SessionIdPattern =
        new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    public async Task<BookPuzzleDto?> GetByIdAsync(int id)
    {
        var puzzle = await _db.BookPuzzles
            .Include(bp => bp.Book)
            .FirstOrDefaultAsync(bp => bp.Id == id);
        return puzzle == null ? null : MapToDto(puzzle);
    }

    /// <summary>Nächstes Puzzle im selben Buch (Id-Reihenfolge = Buchreihenfolge); am Ende wieder das erste.</summary>
    public async Task<BookPuzzleDto> GetNextInBookAsync(int id)
    {
        var current = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == id)
            ?? throw new KeyNotFoundException("Book puzzle not found.");

        var siblings = BookSiblings(current).Include(bp => bp.Book);
        var next = await siblings.Where(bp => bp.Id > current.Id).OrderBy(bp => bp.Id).FirstOrDefaultAsync()
                   ?? await siblings.OrderBy(bp => bp.Id).FirstOrDefaultAsync()   // am Ende → erstes (Loop)
                   ?? throw new KeyNotFoundException("No puzzles in book.");
        return MapToDto(next);
    }

    /// <summary>Zufälliges Puzzle aus demselben Buch (möglichst nicht das aktuelle).</summary>
    public async Task<BookPuzzleDto> GetRandomInBookAsync(int id)
    {
        var current = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == id)
            ?? throw new KeyNotFoundException("Book puzzle not found.");

        // Info-/Erklärlinien sind kein Quiz → nicht zufällig ziehen.
        var others = BookSiblings(current).Where(bp => bp.Id != current.Id && !bp.IsInfoOnly);
        var count = await others.CountAsync();
        if (count == 0)
            return MapToDto(await BookSiblings(current).Include(bp => bp.Book).FirstAsync(bp => bp.Id == current.Id));
        var pick = await others.Include(bp => bp.Book).OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstAsync();
        return MapToDto(pick);
    }

    /// <summary>Puzzles desselben Buchs (per BookId; Fallback BookFileName für Altbestand ohne BookId).</summary>
    private IQueryable<BookPuzzle> BookSiblings(BookPuzzle current) =>
        current.BookId != null
            ? _db.BookPuzzles.Where(bp => bp.BookId == current.BookId)
            : _db.BookPuzzles.Where(bp => bp.BookFileName == current.BookFileName);

    /// <summary>Zeichnet einen Lösungsversuch des eingeloggten Users an einem Buch-Puzzle auf
    /// (für die Tagespuzzle-Visualisierung auf Discord).</summary>
    public async Task RecordAttemptAsync(int id, int userId, RecordBookAttemptDto dto)
    {
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.Id == id))
            throw new KeyNotFoundException("Book puzzle not found.");

        var solvedAt = DateTime.UtcNow;
        var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);
        var startedAt = solvedAt.AddSeconds(-timeSeconds);

        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = id,
            UserId = userId,
            Solved = dto.Solved,
            TimeSeconds = timeSeconds,
            AttemptedAt = solvedAt,
            HintsUsed = Math.Clamp(dto.HintsUsed, 0, 3),
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "BookPuzzleAttempt: User {UserId} {Result} book-puzzle {PuzzleId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
            userId, dto.Solved ? "solved" : "failed", id, startedAt, solvedAt, timeSeconds);

        await NotifySchachBotAsync(id);
    }

    /// <summary>Anonymer (nicht eingeloggter) Lösungsversuch — zählt fürs Tagespuzzle mit,
    /// erscheint aber namenlos. Nur Solves werden erfasst, je (Puzzle, Session) genau einmal
    /// (gegen Spam + saubere Zählung).</summary>
    public async Task RecordAnonymousAttemptAsync(int id, RecordAnonymousBookAttemptDto dto)
    {
        if (!SessionIdPattern.IsMatch(dto.SessionId ?? ""))
            throw new InvalidOperationException("Invalid sessionId.");
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.Id == id))
            throw new KeyNotFoundException("Book puzzle not found.");

        if (dto.Solved)
        {
            var exists = await _db.BookPuzzleAttempts.AnyAsync(
                a => a.BookPuzzleId == id && a.AnonymousSessionId == dto.SessionId && a.Solved);
            if (!exists)
            {
                var solvedAt = DateTime.UtcNow;
                var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);
                _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
                {
                    BookPuzzleId = id,
                    AnonymousSessionId = dto.SessionId,
                    Solved = true,
                    TimeSeconds = timeSeconds,
                    AttemptedAt = solvedAt,
                });
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Race: paralleler Erstversuch derselben Session hat den Unique-Index
                    // (BookPuzzleId, AnonymousSessionId) zuerst belegt → idempotent, kein Doppel-Webhook.
                    _db.ChangeTracker.Clear();
                    return;
                }
                _logger.LogInformation(
                    "BookPuzzleAttempt: Anonymous solved book-puzzle {PuzzleId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
                    id, solvedAt.AddSeconds(-timeSeconds), solvedAt, timeSeconds);
                await NotifySchachBotAsync(id);
            }
        }
    }

    /// <summary>„Track solves" eines per Link geteilten Puzzles: erfasst den ERSTEN Versuch je Besucher
    /// (<paramref name="identityKey"/>). Spätere Versuche desselben Besuchers werden ignoriert (Unique-Index;
    /// hier zusätzlich vorab geprüft, damit InMemory-Tests ohne DB-Constraint korrekt sind). Liefert die
    /// aktuellen Zähler zurück.</summary>
    public async Task<SharedPuzzleCountsDto> RecordSharedAttemptAsync(int id, string identityKey, bool solved, int hintsUsed = 0)
    {
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.Id == id))
            throw new KeyNotFoundException("Book puzzle not found.");

        var exists = await _db.SharedPuzzleAttempts
            .AnyAsync(a => a.BookPuzzleId == id && a.IdentityKey == identityKey);
        if (!exists)
        {
            _db.SharedPuzzleAttempts.Add(new SharedPuzzleAttempt
            {
                BookPuzzleId = id,
                IdentityKey = identityKey,
                Solved = solved,
                HintsUsed = Math.Clamp(hintsUsed, 0, 3),
                CreatedAt = DateTime.UtcNow,
            });
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Race: ein paralleler Erstversuch desselben Besuchers hat den Unique-Index zuerst belegt.
                _db.ChangeTracker.Clear();
            }
        }
        return await GetSharedCountsAsync(id);
    }

    /// <summary>Aggregierte „Track solves"-Zähler (Erstversuch je Besucher) eines geteilten Puzzles,
    /// inkl. Aufschlüsselung der gelösten Erstversuche nach genutzter Tipp-Stufe (0–3).</summary>
    public async Task<SharedPuzzleCountsDto> GetSharedCountsAsync(int id)
    {
        // Eine Aggregat-Query: solved/failed + gelöste je Tipp-Stufe (0–3). HintsUsed ist bereits 0–3 geklemmt.
        var byHint = await _db.SharedPuzzleAttempts
            .Where(a => a.BookPuzzleId == id)
            .GroupBy(a => new { a.Solved, a.HintsUsed })
            .Select(g => new { g.Key.Solved, g.Key.HintsUsed, Count = g.Count() })
            .ToListAsync();

        var solvedByHints = new List<int> { 0, 0, 0, 0 };
        var solved = 0;
        var failed = 0;
        foreach (var row in byHint)
        {
            if (row.Solved)
            {
                solved += row.Count;
                var lvl = Math.Clamp(row.HintsUsed, 0, 3);
                solvedByHints[lvl] += row.Count;
            }
            else
            {
                failed += row.Count;
            }
        }
        return new SharedPuzzleCountsDto { Solved = solved, Failed = failed, SolvedByHints = solvedByHints };
    }

    /// <summary>
    /// Überträgt anonyme BookPuzzleAttempts einer Session auf den eingeloggten User.
    /// Bereits vorhandene Attempts des Users für dasselbe Puzzle werden übersprungen (Unique-Constraint).
    /// Gibt die Anzahl übertragener Attempts zurück.
    /// </summary>
    public async Task<int> ClaimSessionAsync(int userId, string sessionId)
    {
        if (!SessionIdPattern.IsMatch(sessionId ?? ""))
            return 0;

        var anonAttempts = await _db.BookPuzzleAttempts
            .Where(a => a.AnonymousSessionId == sessionId)
            .ToListAsync();

        if (anonAttempts.Count == 0) return 0;

        var existingPuzzleIds = await _db.BookPuzzleAttempts
            .Where(a => a.UserId == userId)
            .Select(a => a.BookPuzzleId)
            .ToHashSetAsync();

        int transferred = 0;
        int deleted = 0;
        foreach (var attempt in anonAttempts)
        {
            if (existingPuzzleIds.Contains(attempt.BookPuzzleId))
            {
                // User hat das Puzzle bereits eingeloggt gelöst → anonymen Eintrag löschen
                _db.BookPuzzleAttempts.Remove(attempt);
                deleted++;
                continue;
            }
            attempt.UserId = userId;
            attempt.AnonymousSessionId = null;
            transferred++;
        }

        if (transferred > 0 || deleted > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("BookPuzzle.ClaimSession: {Transferred} Attempts übertragen, {Deleted} Duplikate gelöscht (Session {Session} → User {UserId}).", transferred, deleted, sessionId, userId);
        }
        return transferred;
    }

    /// <summary>
    /// Stoesst den schach-bot-Webhook fuer das Puzzle an (fire-and-forget via BG-Queue).
    /// Holt im Worker frische Solver-Daten + ruft <see cref="SchachBotWebhookService.NotifyAttemptAsync"/> auf.
    /// </summary>
    private async ValueTask NotifySchachBotAsync(int puzzleId)
    {
        await _bgQueue.EnqueueAsync(async (sp, ct) =>
        {
            var hookLogger = sp.GetService<ILoggerFactory>()?.CreateLogger("RookHub.SchachBotNotify");
            var hook = sp.GetService<SchachBotWebhookService>();
            if (hook == null || !hook.IsEnabled) return;
            // Service-Provider ist scoped → eigene Service-Instanz mit eigenem DbContext.
            var svc = sp.GetService<BookPuzzleService>();
            if (svc == null)
            {
                hookLogger?.LogWarning("SchachBot-Notify: BookPuzzleService nicht im Scope verfuegbar.");
                return;
            }
            try
            {
                var results = await svc.GetResultsAsync(puzzleId, null);
                await hook.NotifyAttemptAsync(puzzleId, results, ct);
            }
            catch (Exception ex)
            {
                hookLogger?.LogWarning(ex, "SchachBot-Notify Worker fehlgeschlagen (puzzleId={PuzzleId})", puzzleId);
            }
        });
    }

    /// <summary>
    /// Aggregierte Ergebnisse zu einem Buch-Puzzle (für die Tagespuzzle-Anzeige): wer hat gelöst
    /// (je User dedupliziert, mit Discord-Verknüpfung sofern vorhanden) + Versuchs-/Lösungszähler.
    /// <paramref name="since"/> (ISO-UTC) grenzt optional auf einen Zeitraum ein.
    /// </summary>
    public async Task<BookPuzzleResultsDto> GetResultsAsync(int id, string? since)
    {
        var q = _db.BookPuzzleAttempts.Where(a => a.BookPuzzleId == id);
        if (DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var sinceUtc))
            q = q.Where(a => a.AttemptedAt >= sinceUtc);

        // Eingeloggte: je User aggregieren. Fairness-Regel (insb. Tagespuzzle):
        // Ein User gilt nur dann als Löser, wenn sein ERSTER Versuch gelöst war —
        // ein späterer Solve nach einem fehlgeschlagenen ersten Versuch zählt nicht.
        // FirstSolved/AnyAttempt sind beides EF-übersetzbare skalare Aggregate.
        var perUser = await q.Where(a => a.UserId != null)
            .GroupBy(a => a.UserId)
            .Select(g => new
            {
                UserId = g.Key!.Value,
                FirstSolved = g.OrderBy(a => a.AttemptedAt).Select(a => a.Solved).FirstOrDefault(),
                TimeSeconds = g.OrderBy(a => a.AttemptedAt).Select(a => a.TimeSeconds).FirstOrDefault(),
                HintsUsed = g.OrderBy(a => a.AttemptedAt).Select(a => a.HintsUsed).FirstOrDefault()
            })
            .ToListAsync();

        // Anonyme: nur gelöste werden anonym erfasst → distinct Sessions = anonyme Löser.
        var anonymousSolvedCount = await q.Where(a => a.AnonymousSessionId != null && a.Solved)
            .Select(a => a.AnonymousSessionId).Distinct().CountAsync();
        var anonymousAttempts = await q.Where(a => a.AnonymousSessionId != null)
            .Select(a => a.AnonymousSessionId).Distinct().CountAsync();

        var userIds = perUser.Select(u => u.UserId).ToList();
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username);
        var profiles = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var solvers = perUser
            .Where(u => u.FirstSolved)
            .Select(u =>
            {
                profiles.TryGetValue(u.UserId, out var prof);
                names.TryGetValue(u.UserId, out var uname);
                return new BookSolverDto
                {
                    Name = prof?.DisplayName ?? uname ?? $"#{u.UserId}",
                    DiscordId = prof?.DiscordId,
                    DiscordUsername = prof?.DiscordUsername,
                    TimeSeconds = u.TimeSeconds,
                    HintsUsed = u.HintsUsed
                };
            })
            .OrderBy(s => s.Name)
            .ToList();

        return new BookPuzzleResultsDto
        {
            SolvedCount = solvers.Count,
            AnonymousSolvedCount = anonymousSolvedCount,
            AttemptCount = perUser.Count + anonymousAttempts,
            Solvers = solvers
        };
    }

    /// <summary>
    /// Zufälliges Buch-Puzzle aus dem gewünschten Pool. pool=random|blind → echtes Zufallspuzzle;
    /// pool=daily → deterministisch pro UTC-Tag. exclude=id,id schließt IDs aus; bookId überschreibt den Pool.
    /// </summary>
    public async Task<BookPuzzleDto> GetRandomAsync(string pool, string? exclude, int? bookId)
    {
        pool = (pool ?? "random").Trim().ToLowerInvariant();
        if (pool != "random" && pool != "daily" && pool != "blind")
            throw new InvalidOperationException("pool must be one of: random, daily, blind.");

        // Info-/Erklärlinien (IsInfoOnly) sind keine Quizaufgaben → in KEINEM Zufalls-/Tagespuzzle-Topf.
        var query = _db.BookPuzzles.Include(bp => bp.Book).Where(bp => bp.Book != null && !bp.IsInfoOnly);
        if (bookId.HasValue)
            // Explizite Buchwahl überschreibt den Pool-Filter: irgendein Puzzle aus diesem Buch.
            query = query.Where(bp => bp.BookId == bookId.Value);
        else
            // Ausgemusterte Puzzles (Retired) werden in keinem Zufalls-Pool mehr gezogen.
            query = pool switch
            {
                "daily" => query.Where(bp => bp.Book!.ForDaily && !bp.Retired),
                "blind" => query.Where(bp => bp.Book!.ForBlind && !bp.Retired),
                _ => query.Where(bp => bp.Book!.ForRandom && !bp.Retired),
            };

        if (!string.IsNullOrWhiteSpace(exclude))
        {
            var excludeIds = exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (excludeIds.Count > 0)
                query = query.Where(bp => !excludeIds.Contains(bp.Id));
        }

        // Daily ist jetzt persistiert: einmal pro UTC-Tag wird ein Puzzle aus den
        // forDaily-Buechern ausgewuerfelt und in DailyPuzzles gespeichert. Spaetere
        // Aufrufe (heute oder rueckblickend) liefern denselben Eintrag.
        if (pool == "daily" && !bookId.HasValue && string.IsNullOrWhiteSpace(exclude))
        {
            return await GetOrAssignDailyAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        }

        var count = await query.CountAsync();
        if (count == 0)
            throw new KeyNotFoundException($"No book puzzle available for pool '{pool}'.");

        var index = Random.Shared.Next(count);

        // FirstOrDefault statt First: schrumpft der Pool zwischen CountAsync und hier
        // (paralleler Import/Delete), zeigt Skip(index) sonst ins Leere -> FirstAsync
        // wuerfe einen unbehandelten 500 statt eines sauberen 404.
        var puzzle = await query.OrderBy(bp => bp.Id).Skip(index).FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"No book puzzle available for pool '{pool}'.");
        return MapToDto(puzzle);
    }

    /// <summary>
    /// Liefert das Tagespuzzle fuer ein bestimmtes UTC-Datum.
    ///
    /// - Datum > heute → <see cref="InvalidOperationException"/> (400)
    /// - Datum ≤ heute und bereits zugeordnet → gespeicherter Eintrag
    /// - Datum ≤ heute und noch nicht zugeordnet → JETZT ausloesen, speichern, liefern
    ///   (Race-safe: Unique-Constraint auf Date macht parallele Inserts idempotent)
    /// </summary>
    public async Task<BookPuzzleDto> GetOrAssignDailyAsync(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date > today)
            throw new InvalidOperationException("Date is in the future.");

        // 1) Vorhandene Zuordnung? Lade gleich das Puzzle mit Book mit.
        var existing = await _db.DailyPuzzles
            .Where(d => d.Date == date)
            .Include(d => d.BookPuzzle!).ThenInclude(bp => bp.Book)
            .FirstOrDefaultAsync();
        if (existing?.BookPuzzle != null)
            return MapToDto(existing.BookPuzzle);

        // 2) Zufaelliges Puzzle aus dem forDaily-Pool (ausgemusterte ausgenommen).
        var pool = _db.BookPuzzles.Include(bp => bp.Book)
            .Where(bp => bp.Book != null && bp.Book.ForDaily && !bp.Retired);
        var count = await pool.CountAsync();
        if (count == 0)
            throw new KeyNotFoundException("No book puzzle available for pool 'daily'.");

        var index = Random.Shared.Next(count);
        var picked = await pool.OrderBy(bp => bp.Id).Skip(index).FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("No book puzzle available for pool 'daily'.");

        _db.DailyPuzzles.Add(new Models.DailyPuzzle
        {
            Date = date,
            BookPuzzleId = picked.Id,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: parallel hat ein anderer Aufruf schon zugeordnet → die vorhandene
            // Zeile lesen und das damals gewaehlte Puzzle liefern.
            _db.ChangeTracker.Clear();
            var raced = await _db.DailyPuzzles
                .Where(d => d.Date == date)
                .Include(d => d.BookPuzzle!).ThenInclude(bp => bp.Book)
                .FirstOrDefaultAsync();
            if (raced?.BookPuzzle != null)
                return MapToDto(raced.BookPuzzle);
            throw;
        }

        _logger.LogInformation("DailyPuzzle assigned: Date={Date} BookPuzzleId={Id}", date, picked.Id);
        return MapToDto(picked);
    }

    /// <summary>
    /// Generiert das Tagespuzzle eines UTC-Datums neu (Admin). Der Link/das Datum bleiben gleich,
    /// nur das dahinterliegende Puzzle wechselt: das bisher zugeordnete Puzzle wird <c>Retired</c>
    /// gesetzt (nie wieder im Daily-/Random-/Blind-Pool) und ein neues aus dem forDaily-Pool
    /// (ausgemusterte ausgenommen) gezogen und der bestehenden Zuordnung untergeschoben.
    ///
    /// Gibt es für das Datum noch keine Zuordnung, wird einfach eine neue angelegt (nichts auszumustern).
    /// Zukuenftige Daten → <see cref="InvalidOperationException"/> (400).
    /// </summary>
    public async Task<BookPuzzleDto> RegenerateDailyAsync(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date > today)
            throw new InvalidOperationException("Date is in the future.");

        var existing = await _db.DailyPuzzles.FirstOrDefaultAsync(d => d.Date == date);

        // Bisheriges Puzzle ausmustern, damit es nicht erneut (auch nicht in Random/Blind) gezogen wird.
        int? retiredId = existing?.BookPuzzleId;
        if (retiredId.HasValue)
        {
            var old = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == retiredId.Value);
            if (old != null)
                old.Retired = true;
        }

        // Neues Puzzle aus dem forDaily-Pool ziehen (ausgemusterte – inkl. des gerade ausgemusterten – ausgenommen).
        var pool = _db.BookPuzzles.Include(bp => bp.Book)
            .Where(bp => bp.Book != null && bp.Book.ForDaily && !bp.Retired
                         && (retiredId == null || bp.Id != retiredId.Value));
        var count = await pool.CountAsync();
        if (count == 0)
            throw new KeyNotFoundException("No book puzzle available for pool 'daily'.");

        var index = Random.Shared.Next(count);
        var picked = await pool.OrderBy(bp => bp.Id).Skip(index).FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("No book puzzle available for pool 'daily'.");

        var now = DateTime.UtcNow;
        if (existing != null)
        {
            existing.BookPuzzleId = picked.Id;
            existing.CreatedAt = now;
        }
        else
        {
            _db.DailyPuzzles.Add(new Models.DailyPuzzle
            {
                Date = date,
                BookPuzzleId = picked.Id,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "DailyPuzzle regenerated: Date={Date} RetiredPuzzleId={Retired} NewPuzzleId={New}",
            date, retiredId, picked.Id);

        var regeneratedDate = date;
        var regeneratedId = picked.Id;
        await _bgQueue.EnqueueAsync(async (sp, ct) =>
        {
            var hook = sp.GetService<SchachBotWebhookService>();
            if (hook != null)
                await hook.NotifyDailyRegeneratedAsync(regeneratedDate, regeneratedId, ct);
        });

        return MapToDto(picked);
    }

    /// <summary>Puzzle-Id zu einer LineId (Lookup für den schach-bot).</summary>
    public async Task<int> GetIdByLineIdAsync(string lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId))
            throw new InvalidOperationException("lineId is required.");

        if (lineId.Length > 300)
            lineId = lineId[..300];

        var puzzle = await _db.BookPuzzles
            .Where(bp => bp.LineId == lineId)
            .Select(bp => new { bp.Id })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Book puzzle not found for given lineId.");
        return puzzle.Id;
    }

    /// <summary>Buch-Liste mit Counts (gruppiert über BookFileName).</summary>
    public async Task<List<BookInfoDto>> GetBooksAsync() =>
        await _db.BookPuzzles
            .GroupBy(bp => bp.BookFileName)
            .Select(g => new BookInfoDto
            {
                BookId = g.Max(bp => bp.BookId),
                BookFileName = g.Key,
                Difficulty = g.First().Difficulty,
                BookRating = g.First().BookRating,
                Tags = g.First().Tags,
                PuzzleCount = g.Count()
            })
            .OrderBy(b => b.BookFileName)
            .ToListAsync();

    /// <summary>Bulk-Import aus JSON; legt fehlende Bücher an, dedupliziert über LineId.</summary>
    public async Task<(int imported, int skipped)> ImportAsync(List<BookPuzzleImportDto> puzzles)
    {
        if (puzzles == null || puzzles.Count == 0)
            throw new InvalidOperationException("No puzzles provided.");

        if (puzzles.Count > 10_000)
            throw new InvalidOperationException("Maximum 10000 puzzles per import.");

        var existingLineIds = await _db.BookPuzzles
            .Select(bp => bp.LineId)
            .ToHashSetAsync();

        // Pro Dateiname ein Book sicherstellen (find-or-create) und BookId setzen, damit
        // auch via Legacy-JSON-Import angelegte Puzzles in den Pools (GetRandom) und in der
        // Admin-Bücher-Liste erscheinen.
        var now = DateTime.UtcNow;
        var bookIds = new Dictionary<string, int>();

        async Task<int> EnsureBookAsync(string fileName)
        {
            if (bookIds.TryGetValue(fileName, out var cached))
                return cached;
            var book = await _db.Books.FirstOrDefaultAsync(b => b.FileName == fileName);
            if (book == null)
            {
                book = new Book
                {
                    FileName = fileName,
                    DisplayName = PgnImportService.CleanDisplayName(fileName),
                    // JSON-Bulk-Import liefert die abgeleiteten Felder (inkl. MoveComments) direkt mit
                    // → gilt als aktuelle Pipeline-Version. Kein SourcePgn (kein PGN), daher nicht
                    // lokal neu aufbereitbar — bei künftigen Pipeline-Bumps ggf. erneuter JSON-Import.
                    ImportVersion = ImportPipeline.CurrentVersion,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.Books.Add(book);
                await _db.SaveChangesAsync();
            }
            bookIds[fileName] = book.Id;
            return book.Id;
        }

        var toAdd = new List<BookPuzzle>();
        var skipped = 0;

        foreach (var dto in puzzles)
        {
            if (existingLineIds.Contains(dto.LineId))
            {
                skipped++;
                continue;
            }

            var fileName = (dto.BookFileName ?? string.Empty).Trim();
            if (fileName.Length == 0) { skipped++; continue; }     // kein leerer BookFileName
            if (fileName.Length > 200) fileName = fileName[..200];  // FileName/BookFileName sind varchar(200)

            var bookId = await EnsureBookAsync(fileName);
            toAdd.Add(new BookPuzzle
            {
                LineId = dto.LineId,
                BookFileName = fileName,
                BookId = bookId,
                Round = dto.Round,
                Fen = dto.Fen,
                Moves = dto.Moves,
                Title = dto.Title,
                Chapter = dto.Chapter,
                Comment = dto.Comment,
                MoveComments = dto.MoveComments is { Count: > 0 } ? JsonSerializer.Serialize(dto.MoveComments) : null,
                Difficulty = dto.Difficulty,
                BookRating = dto.BookRating,
                Tags = dto.Tags
            });
            existingLineIds.Add(dto.LineId);
        }

        if (toAdd.Count > 0)
        {
            _db.BookPuzzles.AddRange(toAdd);
            await _db.SaveChangesAsync();
        }

        return (toAdd.Count, skipped);
    }

    // --- Tagespuzzle-Leaderboards (Monats-Ladder + Hall of Fame) --------------------------

    /// <summary>Eine gewertete Erstversuch-Lösung an einem Tagespuzzle (Rohzeile fürs Ranking).</summary>
    private sealed record DailyScoreRow(DateOnly Date, int UserId, int TimeSeconds, DateTime FirstAt);

    /// <summary>Tages-Rang-Bonus nach Erstversuch-Zeit: 🥇 +5 / 🥈 +3 / 🥉 +1, sonst 0.</summary>
    private static int RankBonus(int rank) => rank switch { 1 => 5, 2 => 3, 3 => 1, _ => 0 };

    /// <summary>
    /// Lädt für alle Tagespuzzles im optionalen Datumsfenster [<paramref name="from"/>,
    /// <paramref name="to"/>] je (Tag, eingeloggtem User) den ERSTEN Versuch und behält nur die
    /// gelösten — dieselbe Fairness-Regel wie <see cref="GetResultsAsync"/>. Ranking/Punkte
    /// berechnet der Aufrufer in-memory (kleine Datenmengen: ein Puzzle pro Tag).
    /// </summary>
    private async Task<List<DailyScoreRow>> LoadDailyFirstAttemptsAsync(DateOnly? from, DateOnly? to)
    {
        var dailyQ = _db.DailyPuzzles.AsQueryable();
        if (from.HasValue) dailyQ = dailyQ.Where(d => d.Date >= from.Value);
        if (to.HasValue) dailyQ = dailyQ.Where(d => d.Date <= to.Value);
        var dailies = await dailyQ.Select(d => new { d.Date, d.BookPuzzleId }).ToListAsync();
        if (dailies.Count == 0) return new();

        var puzzleIds = dailies.Select(d => d.BookPuzzleId).Distinct().ToList();

        // Pro (Puzzle, User) der erste Versuch — Solved/Zeit/Zeitpunkt. Gleiche EF-übersetzbare
        // Aggregat-Form wie in GetResultsAsync.
        var firstAttempts = await _db.BookPuzzleAttempts
            .Where(a => a.UserId != null && puzzleIds.Contains(a.BookPuzzleId))
            .GroupBy(a => new { a.BookPuzzleId, a.UserId })
            .Select(g => new
            {
                g.Key.BookPuzzleId,
                UserId = g.Key.UserId!.Value,
                FirstSolved = g.OrderBy(a => a.AttemptedAt).Select(a => a.Solved).FirstOrDefault(),
                TimeSeconds = g.OrderBy(a => a.AttemptedAt).Select(a => a.TimeSeconds).FirstOrDefault(),
                FirstAt = g.OrderBy(a => a.AttemptedAt).Select(a => a.AttemptedAt).FirstOrDefault()
            })
            .Where(x => x.FirstSolved)
            .ToListAsync();

        // Solver je Puzzle gruppieren, damit ein (selten) an zwei Tagen wiederholtes Puzzle
        // an beiden Tagen wertet.
        var byPuzzle = firstAttempts.GroupBy(x => x.BookPuzzleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<DailyScoreRow>();
        foreach (var d in dailies)
        {
            if (!byPuzzle.TryGetValue(d.BookPuzzleId, out var solvers)) continue;
            foreach (var s in solvers)
                rows.Add(new DailyScoreRow(d.Date, s.UserId, s.TimeSeconds, s.FirstAt));
        }
        return rows;
    }

    /// <summary>
    /// Aggregiert die Erstversuch-Zeilen je User: pro Tag werden die Löser nach Zeit gerankt
    /// (Gleichstand → früherer Versuch zuerst), daraus Punkte (10 + Rang-Bonus), Lösungs- und
    /// 🥇-Zähler summiert.
    /// </summary>
    private static Dictionary<int, (int points, int solved, int golds)> AggregateScores(List<DailyScoreRow> rows)
    {
        var acc = new Dictionary<int, (int points, int solved, int golds)>();
        foreach (var day in rows.GroupBy(r => r.Date))
        {
            var ranked = day.OrderBy(r => r.TimeSeconds).ThenBy(r => r.FirstAt).ToList();
            foreach (var r in ranked)
            {
                // Competition-Ranking: Rang = 1 + Anzahl strikt SCHNELLERER Löser. Zeitgleiche Löser
                // bekommen denselben Rang/Bonus — und alle mit der Bestzeit zählen als 🥇 (statt dass
                // Submit-Reihenfolge/Mikrosekunden über Gold vs. Silber entscheiden, insb. bei TimeSeconds==0).
                var rank = 1 + ranked.Count(o => o.TimeSeconds < r.TimeSeconds);
                acc.TryGetValue(r.UserId, out var cur);
                acc[r.UserId] = (cur.points + 10 + RankBonus(rank), cur.solved + 1, cur.golds + (rank == 1 ? 1 : 0));
            }
        }
        return acc;
    }

    /// <summary>Namen + Discord-Profile der gegebenen User laden (für die Leaderboard-Anzeige).</summary>
    private async Task<(Dictionary<int, string> names, Dictionary<int, UserProfile> profiles)> ResolveUsersAsync(List<int> userIds)
    {
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username);
        var profiles = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);
        return (names, profiles);
    }

    private static (string name, string? discordId, string? discordUsername) ResolveIdentity(
        int userId, Dictionary<int, string> names, Dictionary<int, UserProfile> profiles)
    {
        profiles.TryGetValue(userId, out var prof);
        names.TryGetValue(userId, out var uname);
        return (prof?.DisplayName ?? uname ?? $"#{userId}", prof?.DiscordId, prof?.DiscordUsername);
    }

    /// <summary>
    /// Monats-Wertung des Tagespuzzles für <paramref name="year"/>/<paramref name="month"/>
    /// (1–12). Absteigend nach Punkten, dann gelösten Puzzles, dann Name.
    /// </summary>
    public async Task<DailyLadderDto> GetDailyLadderAsync(int year, int month)
    {
        if (month < 1 || month > 12)
            throw new InvalidOperationException("month must be between 1 and 12.");

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = await LoadDailyFirstAttemptsAsync(from, to);
        var perUser = AggregateScores(rows);
        var (names, profiles) = await ResolveUsersAsync(perUser.Keys.ToList());

        var entries = perUser
            .Select(kv =>
            {
                var (name, did, duser) = ResolveIdentity(kv.Key, names, profiles);
                return new DailyLadderEntryDto
                {
                    Name = name,
                    DiscordId = did,
                    DiscordUsername = duser,
                    Points = kv.Value.points,
                    Solved = kv.Value.solved,
                    Golds = kv.Value.golds
                };
            })
            .OrderByDescending(e => e.Points).ThenByDescending(e => e.Solved).ThenBy(e => e.Name)
            .ToList();

        return new DailyLadderDto { Period = $"{year:D4}-{month:D2}", Entries = entries };
    }

    /// <summary>
    /// All-time Hall of Fame des Tagespuzzles: meiste gelöste Dailies, meiste 🥇 (Tage als
    /// schnellster Erstversuch-Löser) und die schnellste je gelöste Lösung. Jede Liste auf
    /// <paramref name="top"/> Einträge begrenzt.
    /// </summary>
    public async Task<DailyHallOfFameDto> GetDailyHallOfFameAsync(int top = 5)
    {
        var rows = await LoadDailyFirstAttemptsAsync(null, null);
        var perUser = AggregateScores(rows);
        var (names, profiles) = await ResolveUsersAsync(perUser.Keys.ToList());

        List<HallOfFameEntryDto> RankBy(Func<(int points, int solved, int golds), int> pick) => perUser
            .Select(kv =>
            {
                var (name, did, duser) = ResolveIdentity(kv.Key, names, profiles);
                return new HallOfFameEntryDto { Name = name, DiscordId = did, DiscordUsername = duser, Value = pick(kv.Value) };
            })
            .Where(e => e.Value > 0)
            .OrderByDescending(e => e.Value).ThenBy(e => e.Name)
            .Take(top)
            .ToList();

        FastestSolveDto? fastest = null;
        var best = rows.Where(r => r.TimeSeconds > 0)
            .OrderBy(r => r.TimeSeconds).ThenBy(r => r.FirstAt).FirstOrDefault();
        if (best != null)
        {
            var (name, did, duser) = ResolveIdentity(best.UserId, names, profiles);
            fastest = new FastestSolveDto
            {
                Name = name,
                DiscordId = did,
                DiscordUsername = duser,
                TimeSeconds = best.TimeSeconds,
                Date = best.Date.ToString("yyyy-MM-dd")
            };
        }

        return new DailyHallOfFameDto
        {
            MostSolved = RankBy(v => v.solved),
            MostGolds = RankBy(v => v.golds),
            Fastest = fastest
        };
    }

    public static BookPuzzleDto MapToDto(BookPuzzle bp) => new()
    {
        Id = bp.Id,
        LineId = bp.LineId,
        BookFileName = bp.BookFileName,
        BookTitle = string.IsNullOrWhiteSpace(bp.Book?.DisplayName) ? null : bp.Book!.DisplayName,
        Round = bp.Round,
        Fen = bp.Fen,
        Moves = bp.Moves,
        StartPly = bp.StartPly,
        Title = bp.Title,
        Chapter = bp.Chapter,
        Comment = bp.Comment,
        MoveComments = ParseMoveComments(bp.MoveComments),
        // Metadaten bevorzugt vom Buch (admin-gepflegt), sonst vom Puzzle.
        Difficulty = bp.Book?.Difficulty ?? bp.Difficulty,
        BookRating = bp.Book?.Rating ?? bp.BookRating,
        Tags = bp.Book?.Tags ?? bp.Tags,
        Hints = ParseHints(bp.HintsJson),
        HintsFlagged = bp.HintsFlagged,
        IsInfoOnly = bp.IsInfoOnly
    };

    /// <summary>Deserialisiert <see cref="BookPuzzle.HintsJson"/> (sprach-keyed Tipp-Listen).
    /// Defekte/leere Werte → <c>null</c> (nie werfen).</summary>
    public static Dictionary<string, List<string>>? ParseHints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            return map is { Count: > 0 } ? map : null;
        }
        catch { return null; }
    }

    /// <summary>Deserialisiert die in <see cref="BookPuzzle.MoveComments"/> gespeicherte JSON-Map
    /// (Halbzug-Index → Kommentar). Defekte/leere Werte → <c>null</c> (nie werfen).</summary>
    private static Dictionary<int, string>? ParseMoveComments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
            return map is { Count: > 0 } ? map : null;
        }
        catch { return null; }
    }
}
