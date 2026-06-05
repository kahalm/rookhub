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
    private readonly IBackgroundTaskQueue? _bgQueue;

    public WeeklyPostService(AppDbContext db, ILogger<WeeklyPostService> logger, IBackgroundTaskQueue? bgQueue = null)
    {
        _db = db;
        _logger = logger;
        _bgQueue = bgQueue;
    }

    /// <summary>Zeichnet einen gespielten Puzzle-Versuch auf (erster Versuch je Index zählt) und liefert den Stand.</summary>
    public async Task<WeeklyPostProgressDto> RecordAttemptAsync(int weeklyPostId, int userId, RecordWeeklyAttemptDto dto)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");

        var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
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
        var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;

        var perUser = await _db.WeeklyPostAttempts
            .Where(a => a.WeeklyPostId == weeklyPostId)
            .GroupBy(a => a.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Played = g.Count(),
                Solved = g.Count(a => a.Solved),
                TotalSeconds = g.Sum(a => a.TimeSeconds),
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
                    Name = prof?.DisplayName ?? uname ?? $"#{u.UserId}",
                    DiscordId = prof?.DiscordId,
                    DiscordUsername = prof?.DiscordUsername,
                    PlayedCount = u.Played,
                    SolvedCount = u.Solved,
                    TotalSeconds = u.TotalSeconds,
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
    /// Posts ohne Versuche werden weggelassen (Frontend zeigt dort nichts). Parst nur die PGNs der
    /// gespielten Posts (nicht aller) → günstig.
    /// </summary>
    public async Task<List<WeeklyPostProgressDto>> GetAllProgressAsync(int userId)
    {
        var attempts = await _db.WeeklyPostAttempts
            .Where(a => a.UserId == userId)
            .Select(a => new { a.WeeklyPostId, a.Solved, a.TimeSeconds })
            .ToListAsync();

        var result = new List<WeeklyPostProgressDto>();
        foreach (var grp in attempts.GroupBy(a => a.WeeklyPostId))
        {
            var post = await _db.WeeklyPosts.FindAsync(grp.Key);
            if (post == null) continue;   // Post inzwischen gelöscht → ignorieren
            var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
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
        return result;
    }

    /// <summary>Aktueller Fortschritt des Users für einen Wochenpost.</summary>
    public async Task<WeeklyPostProgressDto> GetProgressAsync(int weeklyPostId, int userId)
    {
        var post = await _db.WeeklyPosts.FindAsync(weeklyPostId)
            ?? throw new KeyNotFoundException("Weekly post not found.");
        var total = PgnImportService.ParsePgn(post.FileName, post.PgnContent).Puzzles.Count;
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
