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

    /// <summary>Einzelnes Puzzle per Id — bewusst OHNE Buch-Gate (Teilen-Links, Tagespuzzle,
    /// OG-Vorschau, Bot-Lookup; siehe <see cref="BookAccess"/>).
    /// <para>ABER: gehört das Puzzle zu einem Kalkulationsbuch, wird die LÖSUNG zurückgehalten
    /// (Moves/MoveComments/MoveShapes/AltMoves). Sonst wäre dies der fünfte, ungegatete Weg an
    /// der Kalkulations-Invariante vorbei: die anonyme Slug-/Public-Ansicht liefert je Stellung
    /// die BookPuzzle-Id, mit der man hier sonst die vollen Züge zöge (Review-Fund 2026-08-09).
    /// Anders als die Solver-Geschwister (die 404 werfen) bleibt der Endpunkt hier NUTZBAR —
    /// OG-Vorschau/Teilen einer Stellung brauchen FEN + Metadaten, nur eben nicht die Lösung.</para></summary>
    public async Task<BookPuzzleDto?> GetByIdAsync(int id)
    {
        var puzzle = await _db.BookPuzzles
            .Include(bp => bp.Book)
            .FirstOrDefaultAsync(bp => bp.Id == id);
        if (puzzle == null) return null;
        var dto = MapToDto(puzzle);
        if (puzzle.Book?.IsCalculation == true)
        {
            dto.Moves = "";
            dto.MoveComments = null;
            dto.MoveShapes = null;
            dto.AltMoves = null;
        }
        return dto;
    }

    /// <summary>Nächstes Puzzle im selben Buch in Lesereihenfolge (Round = Chessable-Zeilennummer,
    /// dann Id; NICHT die DB-Id, da re-gefetchte Linien höhere Ids haben); am Ende wieder das erste.
    /// <para>Gegatet über <see cref="BookAccess"/>: das Durchlaufen eines Buchs ab einem geteilten
    /// Einzel-Puzzle darf keinen Zugriff auf fremde/gruppen-gegatete Bücher öffnen.</para></summary>
    public async Task<BookPuzzleDto> GetNextInBookAsync(int id, int? userId = null, bool isAdmin = false)
    {
        var current = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == id)
            ?? throw new KeyNotFoundException("Book puzzle not found.");
        await EnsureBookReadableAsync(current, userId, isAdmin);
        await EnsureNotCalculationBookAsync(current);

        // Nur Schlüssel (Id, Round) in Round-Reihenfolge laden; der Cursor-Vergleich passiert
        // in-memory (provider-unabhängig; SQL sortiert nur nach der Round-Spalte).
        var keys = await BookSiblings(current).OrderBy(bp => bp.Round.Length).ThenBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new { bp.Id, bp.Round })
            .ToListAsync();
        // Cursor-Vergleich über DIESELBE Reihenfolge wie die Sortierung (inkl. Round.Length) —
        // siehe ChapterOrder.Compare: ordinal allein sprang bei Runden „9"/„10" zurück bzw. gar nicht.
        var nextId = keys.FirstOrDefault(k => ChapterOrder.Compare(k.Round, k.Id, current.Round, current.Id) > 0)?.Id
                     ?? keys.Select(k => (int?)k.Id).FirstOrDefault();   // am Ende → erstes (Loop)
        if (nextId == null) throw new KeyNotFoundException("No puzzles in book.");
        var next = await BookSiblings(current).Include(bp => bp.Book).FirstAsync(bp => bp.Id == nextId.Value);
        return MapToDto(next);
    }

    /// <summary>Zufälliges Puzzle aus demselben Buch (möglichst nicht das aktuelle). Gegatet wie
    /// <see cref="GetNextInBookAsync"/>.</summary>
    public async Task<BookPuzzleDto> GetRandomInBookAsync(int id, int? userId = null, bool isAdmin = false)
    {
        var current = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == id)
            ?? throw new KeyNotFoundException("Book puzzle not found.");
        await EnsureBookReadableAsync(current, userId, isAdmin);
        await EnsureNotCalculationBookAsync(current);

        // Info-/Erklärlinien sind kein Quiz → nicht zufällig ziehen.
        var others = BookSiblings(current).Where(bp => bp.Id != current.Id && !bp.IsInfoOnly);
        var count = await others.CountAsync();
        if (count == 0)
            return MapToDto(await BookSiblings(current).Include(bp => bp.Book).FirstAsync(bp => bp.Id == current.Id));
        var pick = await others.Include(bp => bp.Book).OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstAsync();
        return MapToDto(pick);
    }

    /// <summary>Wirft <see cref="KeyNotFoundException"/> (→404, kein Existenz-Orakel), wenn der Aufrufer
    /// das Buch des Puzzles nicht lesen darf.</summary>
    private async Task EnsureBookReadableAsync(BookPuzzle puzzle, int? userId, bool isAdmin)
    {
        if (!await BookAccess.CanReadPuzzleAsync(_db, puzzle, userId, isAdmin))
            throw new KeyNotFoundException("Book puzzle not found.");
    }

    /// <summary>
    /// Wirft <see cref="KeyNotFoundException"/> (→404), wenn das Puzzle zu einem KALKULATIONSBUCH
    /// gehört. Das Buch-Durchlaufen (<see cref="GetNextInBookAsync"/>/<see cref="GetRandomInBookAsync"/>)
    /// ist ein SOLVER-Weg und liefert <see cref="BookPuzzle.Moves"/> mit — in einem Kalkulationsbuch
    /// ist das die Lösung, die den Server nicht verlassen soll (siehe
    /// <see cref="CourseAccess.IsCalculationBookAsync"/>). Beide Endpoints sind anonym und hängen am
    /// selben <see cref="BookAccess"/>-Tor wie der öffentliche Kurs-Endpoint; ohne diese Sperre
    /// bliebe dort dieselbe Lücke offen.
    /// <para>Altbestand ohne <see cref="BookPuzzle.BookId"/> wird wie in
    /// <see cref="BookAccess.CanReadPuzzleAsync"/> über den Dateinamen aufgelöst.</para>
    /// </summary>
    private async Task EnsureNotCalculationBookAsync(BookPuzzle puzzle)
    {
        var isCalculation = puzzle.BookId is int bookId
            ? await CourseAccess.IsCalculationBookAsync(_db, bookId)
            : await _db.Books.AnyAsync(b => b.FileName == puzzle.BookFileName && b.IsCalculation);
        if (isCalculation) throw new KeyNotFoundException("Book puzzle not found.");
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
            // Spielweise je Versuch; unbekannt/fehlend → "training" (Altbestand-Verhalten).
            Mode = SolveMode.Normalize(dto.Mode),
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
                    Mode = SolveMode.Normalize(dto.Mode),
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

        // Eingeloggte: je User aggregieren. Regel (seit v0.309.0): Löser ist, wer das Puzzle
        // IRGENDWANN gelöst hat — als bloß „versucht" gilt nur, wer nie gelöst hat. Fehlversuche
        // vor dem ersten Solve werden gezählt (WrongAttempts → rotes ✗ je Fehlversuch, analog 💡);
        // die Zeit ist die SUMME aller Versuche bis einschließlich des ersten Solves, Tipps das
        // Maximum bis dahin. Versuche NACH dem ersten Solve (Nachspielen) ändern nichts mehr.
        // In-Memory-Aggregation: Versuche je Puzzle sind wenige hundert Zeilen (49 Prod-User).
        var attempts = await q.Where(a => a.UserId != null)
            .Select(a => new { UserId = a.UserId!.Value, a.Solved, a.TimeSeconds, a.HintsUsed, a.AttemptedAt, a.Mode })
            .ToListAsync();
        var perUser = attempts
            .GroupBy(a => a.UserId)
            .Select(g =>
            {
                var ordered = g.OrderBy(a => a.AttemptedAt).ToList();
                var firstSolve = ordered.FirstOrDefault(a => a.Solved);
                var counted = firstSolve == null ? ordered
                    : ordered.Where(a => a.AttemptedAt <= firstSolve.AttemptedAt).ToList();
                return new
                {
                    UserId = g.Key,
                    Solved = firstSolve != null,
                    TimeSeconds = counted.Sum(a => a.TimeSeconds),
                    HintsUsed = counted.Count == 0 ? 0 : counted.Max(a => a.HintsUsed),
                    WrongAttempts = counted.Count(a => !a.Solved),
                    // Spielweise = die des ersten Solves (sonst des ersten Versuchs); alles, was
                    // nicht ausdrücklich „easy" ist, gilt als „training" (auch Altbestand).
                    Mode = SolveMode.Normalize((firstSolve ?? ordered.FirstOrDefault())?.Mode),
                };
            })
            .ToList();

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
            .Where(u => u.Solved)
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
                    HintsUsed = u.HintsUsed,
                    WrongAttempts = u.WrongAttempts,
                    Mode = u.Mode
                };
            })
            .OrderBy(s => s.Name)
            .ToList();

        // Löser je Spielweise (Summe = SolvedCount) — dieselbe Zählregel wie beim Wochenpost.
        var (trainingCount, easyCount) = SolveMode.Split(
            solvers.Count, solvers.Count(sv => sv.Mode == SolveMode.Easy));

        return new BookPuzzleResultsDto
        {
            SolvedCount = solvers.Count,
            AnonymousSolvedCount = anonymousSolvedCount,
            AttemptCount = perUser.Count + anonymousAttempts,
            TrainingCount = trainingCount,
            EasyCount = easyCount,
            Solvers = solvers
        };
    }

    /// <summary>Zieht ein zufälliges Puzzle aus dem Pool (Count → Random-Skip → FirstOrDefault).
    /// FirstOrDefault statt First ist Teil des Kontrakts: schrumpft der Pool zwischen CountAsync
    /// und dem Skip (paralleler Import/Delete), zeigt Skip(index) sonst ins Leere und FirstAsync
    /// würfe einen unbehandelten 500 statt eines sauberen 404. Diese Race-Absicherung lag vorher
    /// als Copy-Paste in drei Methoden (GetRandom/GetOrAssignDaily/RegenerateDaily).</summary>
    private static async Task<BookPuzzle> PickRandomAsync(IQueryable<BookPuzzle> pool, string poolName)
    {
        var count = await pool.CountAsync();
        if (count == 0)
            throw new KeyNotFoundException($"No book puzzle available for pool '{poolName}'.");
        return await pool.OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"No book puzzle available for pool '{poolName}'.");
    }

    /// <summary>
    /// Zufälliges Buch-Puzzle aus dem gewünschten Pool. pool=random|blind → echtes Zufallspuzzle;
    /// pool=daily → deterministisch pro UTC-Tag. exclude=id,id schließt IDs aus; bookId überschreibt den Pool.
    /// </summary>
    public async Task<BookPuzzleDto> GetRandomAsync(string pool, string? exclude, int? bookId,
        int? userId = null, bool isAdmin = false)
    {
        pool = (pool ?? "random").Trim().ToLowerInvariant();
        if (pool != "random" && pool != "daily" && pool != "blind")
            throw new InvalidOperationException("pool must be one of: random, daily, blind.");

        // Info-/Erklärlinien (IsInfoOnly) sind keine Quizaufgaben → in KEINEM Zufalls-/Tagespuzzle-Topf.
        var query = _db.BookPuzzles.Include(bp => bp.Book).Where(bp => bp.Book != null && !bp.IsInfoOnly);
        if (bookId.HasValue)
        {
            // Explizite Buchwahl überschreibt den Pool-Filter: irgendein Puzzle aus diesem Buch —
            // aber NUR aus einem lesbaren Buch (sonst wäre der Endpoint ein anonymer Voll-Export
            // beliebiger Bücher, inkl. persönlicher Importe fremder Nutzer; siehe BookAccess).
            if (!await BookAccess.CanReadAsync(_db, bookId.Value, userId, isAdmin))
                throw new KeyNotFoundException($"No book puzzle available for pool '{pool}'.");
            query = query.Where(bp => bp.BookId == bookId.Value);
        }
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

        var puzzle = await PickRandomAsync(query, pool);
        return MapToDto(puzzle);
    }

    /// <summary>
    /// Liefert das Tagespuzzle fuer ein bestimmtes UTC-Datum.
    ///
    /// - Datum > heute → <see cref="InvalidOperationException"/> (400)
    /// - bereits zugeordnet → gespeicherter Eintrag
    /// - noch nicht zugeordnet und Datum ist HEUTE oder GESTERN → JETZT ausloesen, speichern,
    ///   liefern (Race-safe: Unique-Constraint auf Date macht parallele Inserts idempotent)
    /// - aelteres Datum ohne Zuordnung → <see cref="KeyNotFoundException"/> (404). Der Endpoint
    ///   (und das OG-Vorschaubild) ist AllowAnonymous — on-demand-Anlage fuer BELIEBIGE
    ///   Vergangenheit erlaubte anonyme DB-Write-Amplification per Datums-Enumeration und
    ///   „verbrauchte" dabei Puzzles aus dem forDaily-Pool. Der Scheduler ordnet ohnehin
    ///   taeglich um 00:00 UTC zu; Gestern bleibt als Zeitzonen-/Ausfall-Kulanz on-demand.
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

        if (date < today.AddDays(-1))
            throw new KeyNotFoundException("No daily puzzle was assigned for this date.");

        // 2) Zufaelliges Puzzle aus dem forDaily-Pool (ausgemusterte + Info-/Erklaerlinien ausgenommen).
        var pool = _db.BookPuzzles.Include(bp => bp.Book)
            .Where(bp => bp.Book != null && bp.Book.ForDaily && !bp.Retired && !bp.IsInfoOnly);
        var picked = await PickRandomAsync(pool, "daily");

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

        // Neues Puzzle aus dem forDaily-Pool ziehen (ausgemusterte – inkl. des gerade ausgemusterten –
        // sowie Info-/Erklaerlinien ausgenommen).
        var pool = _db.BookPuzzles.Include(bp => bp.Book)
            .Where(bp => bp.Book != null && bp.Book.ForDaily && !bp.Retired && !bp.IsInfoOnly
                         && (retiredId == null || bp.Id != retiredId.Value));
        var picked = await PickRandomAsync(pool, "daily");

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

    /// <summary>Buch-Liste mit Counts (gruppiert über BookFileName) — nur die für den (ggf. anonymen)
    /// Aufrufer lesbaren Bücher (<see cref="BookAccess"/>). Vorher listete der offene Endpoint ALLE
    /// Bücher inkl. persönlicher Importe fremder Nutzer und lieferte damit die Ids für den
    /// <c>?bookId=</c>-Voll-Export.</summary>
    public async Task<List<BookInfoDto>> GetBooksAsync(int? userId = null, bool isAdmin = false)
    {
        var readableFileNames = BookAccess.ReadableBy(_db, userId, isAdmin).Select(b => b.FileName);
        // Altbestand ohne Book-Zeile bleibt sichtbar (an so einem „Buch" kann keine Freigabe hängen —
        // dieselbe Regel wie in BookAccess.CanReadPuzzleAsync).
        var gatedFileNames = _db.Books.Select(b => b.FileName);
        return await _db.BookPuzzles
            .Where(bp => readableFileNames.Contains(bp.BookFileName)
                         || !gatedFileNames.Contains(bp.BookFileName))
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
    }

    /// <summary>Bulk-Import aus JSON; legt fehlende Bücher an, dedupliziert über LineId.</summary>
    public async Task<(int imported, int skipped)> ImportAsync(List<BookPuzzleImportDto> puzzles)
    {
        if (puzzles == null || puzzles.Count == 0)
            throw new InvalidOperationException("No puzzles provided.");

        if (puzzles.Count > 10_000)
            throw new InvalidOperationException("Maximum 10000 puzzles per import.");

        // NUR die LineIds abfragen, um die es in diesem Import geht. Vorher lud die Zeile die
        // LineId JEDES Puzzles der gesamten Datenbank in den Speicher, obwohl höchstens 10.000
        // davon geprüft werden — die Kosten wuchsen also mit dem Gesamtbestand statt mit dem Import.
        // In Blöcken, damit die IN-Liste nicht ausufert.
        var incoming = puzzles
            .Select(p => p.LineId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();
        var existingLineIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in incoming.Chunk(500))
        {
            var found = await _db.BookPuzzles
                .Where(bp => chunk.Contains(bp.LineId))
                .Select(bp => bp.LineId)
                .ToListAsync();
            foreach (var id in found) existingLineIds.Add(id);
        }

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
        MoveShapes = bp.MoveShapes,   // roher JSON-String; das Frontend parst ihn selbst
        AltMoves = bp.AltMoves,       // roher JSON-String {ply:[uci]}; Solver akzeptiert die Alternativen
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
