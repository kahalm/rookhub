using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Per-User-Fortschritt für Wochenposts: zeichnet gespielte Puzzles (idempotent je (Post, User, Index))
/// auf und berechnet den Stand. „Erledigt" = alle Puzzles gespielt (Solved egal). Muster analog zu
/// <see cref="CourseService.RecordResultAsync"/> (idempotent + <see cref="DbUpdateException"/>-Race-Handling).
/// </summary>
public class WeeklyPostService
{
    private readonly AppDbContext _db;
    private readonly ILogger<WeeklyPostService> _logger;
    private readonly IWebhookTaskQueue? _bgQueue;

    public WeeklyPostService(AppDbContext db, ILogger<WeeklyPostService> logger, IWebhookTaskQueue? bgQueue = null)
    {
        _db = db;
        _logger = logger;
        _bgQueue = bgQueue;
    }

    /// <summary>
    /// Liefert die gecachte Puzzle-Anzahl eines Posts; ist sie 0 (Alt-Datensatz vor Einführung der Spalte),
    /// wird sie aus dem PGN nachberechnet und persistiert (einmaliger Lazy-Backfill, danach parse-frei).
    /// </summary>
    private async Task<int> GetTotalAsync(WeeklyPost post)
    {
        // Buch-Kapitel-Quelle: Inhalt ist live an das Buch gebunden → Anzahl frisch aus den (Quiz-)Puzzles
        // des Kapitels zählen, damit „erledigt"/Total mit der tatsächlichen Spielsequenz übereinstimmt.
        if (post.SourceBookId is int bookId)
            return await ChapterQuizPuzzles(bookId, post.SourceChapter).CountAsync();

        if (post.PuzzleCount > 0) return post.PuzzleCount;
        var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
        if (total > 0)
        {
            post.PuzzleCount = total;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
        }
        return total;
    }

    // ---- Buch-Kapitel-Quelle -------------------------------------------------
    // Ein Wochenpost kann statt eines hochgeladenen PGN EIN Kapitel eines Buchs spiegeln. Die Puzzles
    // werden dann live aus den BookPuzzles gebaut — dieselbe Reihenfolge/Filterung wie im Kurs
    // (Round dann Id, Info-/Erklärlinien raus). Kapitel-Grouping analog CourseService.

    /// <summary>Quiz-Puzzles (ohne Info-/Erklärlinien) eines Buch-Kapitels in Lesereihenfolge (Round, dann Id).
    /// <paramref name="chapterName"/> = null ⇒ Sammel-„ohne Kapitel".</summary>
    private IQueryable<BookPuzzle> ChapterQuizPuzzles(int bookId, string? chapterName)
    {
        var q = _db.BookPuzzles.Where(bp => bp.BookId == bookId && !bp.IsInfoOnly);
        q = chapterName == null
            ? q.Where(bp => bp.Chapter == null || bp.Chapter == "")
            : q.Where(bp => bp.Chapter == chapterName);
        return q.OrderBy(bp => bp.Round).ThenBy(bp => bp.Id);
    }

    /// <summary>Die Kapitelnamen eines Buchs in Lesereihenfolge — geteilte Logik in
    /// <see cref="ChapterOrder"/> (deckungsgleich mit <c>CourseService</c>), damit der von der
    /// Kapitel-Liste gelieferte Index hier denselben Namen auflöst.</summary>
    private Task<List<string?>> GetOrderedChapterNamesAsync(int bookId)
        => ChapterOrder.GetOrderedChapterNamesAsync(_db, bookId);

    /// <summary>
    /// Puzzle-Sequenz eines Wochenposts zum Durchspielen. Buch-Kapitel-Quelle → live aus den BookPuzzles
    /// (voller <see cref="BookPuzzleDto"/> inkl. Tipps/Shapes/Alt-Zügen); sonst das gespeicherte PGN
    /// on-the-fly geparst (Alt-Verhalten). In beiden Fällen ist <c>Id</c> der 0-basierte Sequenz-Index
    /// (kein DB-Datensatz) — der Wochenpost-Fortschritt ist index-basiert.
    /// </summary>
    public async Task<List<BookPuzzleDto>> GetPlayPuzzlesAsync(WeeklyPost post)
    {
        if (post.SourceBookId is int bookId)
        {
            var puzzles = await ChapterQuizPuzzles(bookId, post.SourceChapter).Include(bp => bp.Book).ToListAsync();
            return puzzles.Select((bp, i) =>
            {
                var dto = BookPuzzleService.MapToDto(bp);
                dto.Id = i;   // Sequenz-Index (Fortschritt ist index-basiert), nicht die DB-Id
                return dto;
            }).ToList();
        }

        var parsed = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles;
        return parsed.Select((p, i) => new BookPuzzleDto
        {
            Id = i,
            LineId = p.LineId,
            BookFileName = post.FileName,
            Round = p.Round,
            Fen = p.Fen,
            Moves = p.Moves,
            StartPly = p.StartPly,
            Title = p.Title,
            Chapter = p.Chapter,
            Comment = p.Comment,
            MoveComments = p.MoveComments,
        }).ToList();
    }

    /// <summary>
    /// Legt einen Wochenpost aus EINEM Kapitel eines Buchs an. <paramref name="chapterIndex"/> ist die
    /// 0-basierte Position aus der Kapitel-Liste. Wirft <see cref="ArgumentException"/> (→ 400), wenn das
    /// Buch fehlt, der Index ungültig ist oder das Kapitel keine (Quiz-)Puzzles enthält.
    /// </summary>
    public async Task<WeeklyPost> CreateFromChapterAsync(int bookId, int chapterIndex, DateTime scheduledAt, string? title, string? description)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId)
            ?? throw new ArgumentException("Book not found.");

        var names = await GetOrderedChapterNamesAsync(bookId);
        if (chapterIndex < 0 || chapterIndex >= names.Count)
            throw new ArgumentException("Chapter index out of range.");
        var chapterName = names[chapterIndex];

        var count = await ChapterQuizPuzzles(bookId, chapterName).CountAsync();
        if (count == 0)
            throw new ArgumentException("Chapter has no puzzles.");

        var bookName = string.IsNullOrWhiteSpace(book.DisplayName) ? book.FileName : book.DisplayName;
        var finalTitle = string.IsNullOrWhiteSpace(title)
            ? (chapterName == null ? bookName : $"{bookName}: {chapterName}")
            : title.Trim();
        if (finalTitle.Length == 0) finalTitle = bookName;
        if (finalTitle.Length > 300) finalTitle = finalTitle[..300];

        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (desc != null && desc.Length > 500) desc = desc[..500];

        var now = DateTime.UtcNow;
        var post = new WeeklyPost
        {
            Title = finalTitle,
            Description = desc,
            FileName = book.FileName,
            PgnContent = string.Empty,      // Buch-Quelle: kein gespeichertes PGN, Puzzles kommen live aus den BookPuzzles
            FileSize = 0,
            PuzzleCount = count,            // GetTotalAsync zählt für Buch-Quellen ohnehin live; hier fürs Übersichts-DTO
            SourceBookId = bookId,
            SourceChapter = chapterName,
            ScheduledAt = scheduledAt == default ? now : scheduledAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();
        return post;
    }

    /// <summary>Zeichnet einen gespielten Puzzle-Versuch auf (erster Versuch je Index zählt) und liefert den Stand.</summary>
    public async Task<WeeklyPostProgressDto> RecordAttemptAsync(int weeklyPostId, int userId, RecordWeeklyAttemptDto dto)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");

        var total = await GetTotalAsync(post);
        if (dto.PuzzleIndex < 0 || dto.PuzzleIndex >= total)
            throw new KeyNotFoundException("Puzzle index out of range.");

        var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);

        var already = await _db.WeeklyPostAttempts
            .AnyAsync(a => a.WeeklyPostId == weeklyPostId && a.UserId == userId && a.PuzzleIndex == dto.PuzzleIndex);
        if (!already)
        {
            // Strukturiertes Log fuer Kibana (Marker „WeeklyPostAttempt") — nur beim ERSTEN Versuch je
            // (Post, User, Index), damit die Event-Anzahl je User = Zahl der gespielten Wochenpost-Puzzle.
            // UserName/UserId reichert die Request-Middleware via LogContext an. Muster wie CoursePuzzleAttempt.
            _logger.LogInformation(
                "WeeklyPostAttempt: User {UserId} {Result} weekly-post {WeeklyPostId} puzzle {PuzzleIndex} in {TimeSeconds}s",
                userId, dto.Solved ? "solved" : "failed", weeklyPostId, dto.PuzzleIndex, timeSeconds);

            _db.WeeklyPostAttempts.Add(new WeeklyPostAttempt
            {
                WeeklyPostId = weeklyPostId,
                UserId = userId,
                PuzzleIndex = dto.PuzzleIndex,
                Solved = dto.Solved,
                TimeSeconds = timeSeconds,
                HintsUsed = Math.Clamp(dto.HintsUsed, 0, 3),
                WrongAttempts = Math.Clamp(dto.WrongAttempts, 0, 10000),
                Mouseslips = Math.Clamp(dto.Mouseslips, 0, 1000),
                AttemptedAt = DateTime.UtcNow,
            });
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race: paralleles Aufzeichnen desselben Puzzles → Unique (WeeklyPostId, UserId, PuzzleIndex). Idempotent.
                _db.ChangeTracker.Clear();
            }
            // Nur bei einem NEUEN Versuch den Bot-Webhook anstoßen (kein Spam bei idempotenten Wiederholungen).
            await NotifySchachBotAsync(weeklyPostId);
        }

        return await BuildProgressAsync(weeklyPostId, userId, total);
    }

    /// <summary>
    /// Aggregierte Ergebnisse eines Wochenposts (für die Discord-Anzeige): je User gespielt/gelöst,
    /// Gesamtzeit, „erledigt" (alle gespielt) — mit Discord-Verknüpfung sofern vorhanden.
    /// </summary>
    public async Task<WeeklyPostResultsDto> GetResultsAsync(int weeklyPostId)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");
        var total = await GetTotalAsync(post);

        var perUser = await _db.WeeklyPostAttempts
            .Where(a => a.WeeklyPostId == weeklyPostId)
            .GroupBy(a => a.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Played = g.Count(),
                Solved = g.Count(a => a.Solved),
                TotalSeconds = g.Sum(a => a.TimeSeconds),
                // Über alle Puzzles des Posts: höchste genutzte Tipp-Stufe → > 0 ⇒ „mit Tipps gelöst" (💡).
                HintsUsed = g.Max(a => a.HintsUsed),
            })
            .ToListAsync();

        var userIds = perUser.Select(u => u.UserId).ToList();
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username);
        var profiles = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var players = perUser
            .Select(u =>
            {
                profiles.TryGetValue(u.UserId, out var prof);
                names.TryGetValue(u.UserId, out var uname);
                return new WeeklyPlayerResultDto
                {
                    UserId = u.UserId,
                    Name = prof?.DisplayName ?? uname ?? $"#{u.UserId}",
                    DiscordId = prof?.DiscordId,
                    DiscordUsername = prof?.DiscordUsername,
                    PlayedCount = u.Played,
                    SolvedCount = u.Solved,
                    TotalSeconds = u.TotalSeconds,
                    HintsUsed = u.HintsUsed,
                    Completed = total > 0 && u.Played >= total,
                };
            })
            .OrderByDescending(p => p.Completed)
            .ThenByDescending(p => p.SolvedCount)
            .ThenBy(p => p.Name)
            .ToList();

        return new WeeklyPostResultsDto
        {
            WeeklyPostId = weeklyPostId,
            Total = total,
            CompletedCount = players.Count(p => p.Completed),
            Players = players,
        };
    }

    /// <summary>
    /// Admin-Detailaufschlüsselung eines Spielers bei einem Wochenpost: eine Zeile je gespieltem Puzzle
    /// (Zeit, Tipps, Fehlzüge, Mausrutscher). Puzzle-Titel werden EINMAL aus dem PGN aufgelöst.
    /// </summary>
    public async Task<WeeklyPlayerBreakdownDto> GetPlayerBreakdownAsync(int weeklyPostId, int userId)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");
        var total = await GetTotalAsync(post);

        var attempts = await _db.WeeklyPostAttempts
            .Where(a => a.WeeklyPostId == weeklyPostId && a.UserId == userId)
            .OrderBy(a => a.PuzzleIndex)
            .ToListAsync();

        // Titel je Puzzle-Index aus derselben Sequenz wie beim Durchspielen (PGN- oder Buch-Kapitel-Quelle).
        var titles = (await GetPlayPuzzlesAsync(post)).Select(p => p.Title).ToList();

        var name = await _db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.DisplayName).FirstOrDefaultAsync()
            ?? await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync()
            ?? $"#{userId}";

        return new WeeklyPlayerBreakdownDto
        {
            WeeklyPostId = weeklyPostId,
            UserId = userId,
            PlayerName = name,
            Total = total,
            Rows = attempts.Select(a => new WeeklyPuzzleBreakdownRowDto
            {
                PuzzleIndex = a.PuzzleIndex,
                Title = a.PuzzleIndex >= 0 && a.PuzzleIndex < titles.Count ? titles[a.PuzzleIndex] : null,
                Solved = a.Solved,
                TimeSeconds = a.TimeSeconds,
                HintsUsed = a.HintsUsed,
                WrongAttempts = a.WrongAttempts,
                Mouseslips = a.Mouseslips,
                AttemptedAt = a.AttemptedAt,
            }).ToList(),
        };
    }

    /// <summary>Stößt den schach-bot-Webhook für den Wochenpost an (fire-and-forget via BG-Queue, frische Results im Worker).</summary>
    private async ValueTask NotifySchachBotAsync(int weeklyPostId)
    {
        if (_bgQueue == null) return;
        await _bgQueue.EnqueueAsync(async (sp, ct) =>
        {
            var hookLogger = sp.GetService<ILoggerFactory>()?.CreateLogger("RookHub.SchachBotWeeklyNotify");
            var hook = sp.GetService<SchachBotWebhookService>();
            if (hook == null || !hook.IsEnabled) return;
            var svc = sp.GetService<WeeklyPostService>();   // scoped → eigener DbContext
            if (svc == null) { hookLogger?.LogWarning("WeeklyNotify: WeeklyPostService nicht im Scope."); return; }
            try
            {
                var results = await svc.GetResultsAsync(weeklyPostId);
                await hook.NotifyWeeklyAsync(weeklyPostId, results, ct);
            }
            catch (Exception ex)
            {
                hookLogger?.LogWarning(ex, "WeeklyNotify Worker fehlgeschlagen (weeklyPostId={Id})", weeklyPostId);
            }
        });
    }

    /// <summary>
    /// Fortschritt des Users über ALLE Wochenposts, an denen er Versuche hat (für die Übersicht).
    /// Posts ohne Versuche werden weggelassen (Frontend zeigt dort nichts). Lädt alle gespielten Posts
    /// in EINER Query (kein FindAsync je Post) und nutzt die gecachte Puzzle-Anzahl (kein PGN-Parse).
    /// </summary>
    public async Task<List<WeeklyPostProgressDto>> GetAllProgressAsync(int userId)
    {
        var attempts = await _db.WeeklyPostAttempts
            .Where(a => a.UserId == userId)
            .Select(a => new { a.WeeklyPostId, a.Solved, a.TimeSeconds })
            .ToListAsync();

        var postIds = attempts.Select(a => a.WeeklyPostId).Distinct().ToList();
        var posts = await _db.WeeklyPosts
            .Where(w => postIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id);

        var needsBackfill = false;
        var result = new List<WeeklyPostProgressDto>();
        foreach (var grp in attempts.GroupBy(a => a.WeeklyPostId))
        {
            if (!posts.TryGetValue(grp.Key, out var post)) continue;   // Post inzwischen gelöscht → ignorieren
            int total;
            if (post.SourceBookId != null)
            {
                // Kapitel-Quelle: Inhalt hängt live am Buch — Anzahl frisch zählen (wie GetTotalAsync/
                // RecordAttempt). Der beim Anlegen gecachte PuzzleCount veraltet, sobald ein Re-Fetch
                // das Kapitel wachsen/schrumpfen lässt → Übersicht meldete sonst „erledigt", während
                // die Post-Ansicht noch offene Puzzles zeigte (bzw. umgekehrt nie erreichbar wurde).
                total = await GetTotalAsync(post);
            }
            else
            {
                total = post.PuzzleCount;
                if (total <= 0)
                {
                    // Alt-Datensatz ohne gecachte Anzahl: einmal parsen und für künftige Aufrufe persistieren.
                    total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
                    if (total > 0) { post.PuzzleCount = total; needsBackfill = true; }
                }
            }
            var played = grp.Count();
            result.Add(new WeeklyPostProgressDto
            {
                WeeklyPostId = grp.Key,
                Total = total,
                PlayedCount = played,
                SolvedCount = grp.Count(a => a.Solved),
                Completed = total > 0 && played >= total,
                TotalSeconds = grp.Sum(a => a.TimeSeconds),
            });
        }
        if (needsBackfill)
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
        }
        return result;
    }

    /// <summary>Aktueller Fortschritt des Users für einen Wochenpost.</summary>
    public async Task<WeeklyPostProgressDto> GetProgressAsync(int weeklyPostId, int userId)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");
        var total = await GetTotalAsync(post);
        return await BuildProgressAsync(weeklyPostId, userId, total);
    }

    private async Task<WeeklyPostProgressDto> BuildProgressAsync(int weeklyPostId, int userId, int total)
    {
        var played = await _db.WeeklyPostAttempts
            .Where(a => a.WeeklyPostId == weeklyPostId && a.UserId == userId)
            .Select(a => new { a.PuzzleIndex, a.Solved, a.TimeSeconds })
            .ToListAsync();

        return new WeeklyPostProgressDto
        {
            WeeklyPostId = weeklyPostId,
            Total = total,
            PlayedCount = played.Count,
            SolvedCount = played.Count(a => a.Solved),
            Completed = total > 0 && played.Count >= total,
            TotalSeconds = played.Sum(a => a.TimeSeconds),
            PlayedIndices = played.Select(a => a.PuzzleIndex).OrderBy(i => i).ToList(),
        };
    }
}
