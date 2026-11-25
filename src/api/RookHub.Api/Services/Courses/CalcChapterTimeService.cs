using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Kapitel-Uhr des Kalkulations-Modus im Konto (1.020.1, <see cref="CalcChapterTime"/>). Gespeichert wird der
/// aufsummierte Wert; der höhere gewinnt (zwei Geräte zählen nicht gegeneinander zurück, ein altes Gerät setzt nichts
/// herab). Zugriff wie bei den Stellungen (<see cref="CourseAccess"/>).
/// </summary>
public class CalcChapterTimeService
{
    public const int MaxChapterLength = 200;
    /// <summary>Höchstens so viele Kapitel je Anfrage.</summary>
    public const int MaxChaptersPerRequest = 500;
    /// <summary>Obergrenze je Kapitel (1000 h) — gegen Unsinn aus dem Browser.</summary>
    public const int MaxSeconds = 3_600_000;

    private readonly AppDbContext _db;

    public CalcChapterTimeService(AppDbContext db) => _db = db;

    public async Task<Dictionary<string, int>> GetAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin, ct);
        return await _db.CalcChapterTimes.AsNoTracking()
            .Where(t => t.UserId == userId && t.BookId == bookId)
            .ToDictionaryAsync(t => t.Chapter, t => t.Seconds, StringComparer.Ordinal, ct);
    }

    /// <summary>Kapitelzeiten übernehmen (je Kapitel der höhere Wert) → der danach gültige Stand des Buchs.</summary>
    public async Task<Dictionary<string, int>> SetAsync(int userId, int bookId, bool isAdmin, IReadOnlyDictionary<string, int> times,
        CancellationToken ct = default)
    {
        if (times.Count > MaxChaptersPerRequest)
            throw new DomainValidationException($"At most {MaxChaptersPerRequest} chapters per request.");
        foreach (var (chapter, seconds) in times)
        {
            if (chapter is null || chapter.Length > MaxChapterLength) throw new DomainValidationException("Chapter name too long.");
            if (seconds < 0) throw new DomainValidationException("Seconds must not be negative.");
        }
        await EnsureAccessAsync(userId, bookId, isAdmin, ct);
        var rows = await _db.CalcChapterTimes.Where(t => t.UserId == userId && t.BookId == bookId)
            .ToDictionaryAsync(t => t.Chapter, StringComparer.Ordinal, ct);
        var now = DateTime.UtcNow;
        foreach (var (chapter, raw) in times)
        {
            var seconds = Math.Min(raw, MaxSeconds);
            if (!rows.TryGetValue(chapter, out var row))
            {
                row = new CalcChapterTime { UserId = userId, BookId = bookId, Chapter = chapter, Seconds = seconds, UpdatedAt = now };
                _db.CalcChapterTimes.Add(row);
                rows[chapter] = row;
            }
            else if (seconds > row.Seconds)
            {
                row.Seconds = seconds;
                row.UpdatedAt = now;
            }
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (_db.ChangeTracker.Entries<CalcChapterTime>().Any(e => e.State == EntityState.Added))
        {
            // Zwei Geräte legen dasselbe Kapitel gleichzeitig an: neu laden und den höheren Wert noch einmal setzen.
            _db.ChangeTracker.Clear();
            return await SetAsync(userId, bookId, isAdmin, times, ct);
        }
        return rows.ToDictionary(kv => kv.Key, kv => kv.Value.Seconds, StringComparer.Ordinal);
    }

    private async Task EnsureAccessAsync(int userId, int bookId, bool isAdmin, CancellationToken ct)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct))
            throw new NotFoundException("Book not found.");
    }
}
