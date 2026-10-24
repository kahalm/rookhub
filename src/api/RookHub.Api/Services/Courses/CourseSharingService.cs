using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Kurs mit ausgewählten Personen teilen. Nur der Besitzer eines PERSÖNLICHEN Kurses
/// (<see cref="Book.OwnerUserId"/> == userId) darf ihn teilen; die Empfänger müssen befreundet sein (wie bei
/// Puzzle-Challenges) — sonst wird der Empfänger mit Grund übersprungen. Gruppen-/globale Bücher werden über
/// die Gruppen-Freigabe geteilt, nicht hier. Aus <see cref="CourseService"/> herausgelöst (Codereview
/// 2026-09-29, A7-010/Z4); der <see cref="CourseService"/> bleibt die Fassade.
/// </summary>
public class CourseSharingService
{
    private readonly AppDbContext _db;
    private readonly FriendService _friends;
    private readonly NotificationService _notifications;

    public CourseSharingService(AppDbContext db, FriendService friends, NotificationService notifications)
    {
        _db = db;
        _friends = friends;
        _notifications = notifications;
    }

    /// <summary>Wirft <see cref="NotFoundException"/> (→404), wenn das Buch fehlt, und
    /// <see cref="ForbiddenException"/> (→403), wenn der User nicht der Besitzer ist.</summary>
    private async Task<Book> EnsureOwnedBookAsync(int userId, int bookId)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId)
            ?? throw new NotFoundException("Book not found.");
        if (book.OwnerUserId != userId)
            throw new ForbiddenException("Only the owner can share this course.");
        return book;
    }

    /// <summary>Teilt einen eigenen Kurs mit mehreren (befreundeten) Nutzern. Idempotent: bereits
    /// geteilte Empfänger werden übersprungen (Grund <c>duplicate</c>), Fremde als <c>not_friends</c>,
    /// unbekannte als <c>not_found</c>, man selbst als <c>self</c>. Legt je neuem Empfänger eine
    /// In-App-Benachrichtigung an. Die Mechanik (Skip-Gründe, Unique-Race) liegt gemeinsam mit dem
    /// Repertoire-Teilen in <see cref="ShareServiceBatch"/> — hier nur das Kurs-Spezifische.</summary>
    public async Task<CourseShareResultDto> ShareCourseAsync(int userId, int bookId, List<int> recipientUserIds, bool isAdmin)
    {
        var book = await EnsureOwnedBookAsync(userId, bookId);

        var outcome = await ShareServiceBatch.ShareAsync(_db, _friends, userId, recipientUserIds, isAdmin,
            loadAlreadySharedAsync: async ids => (await _db.CourseShares
                .Where(cs => cs.BookId == bookId && ids.Contains(cs.RecipientId))
                .Select(cs => cs.RecipientId)
                .ToListAsync()).ToHashSet(),
            addShare: rid => _db.CourseShares.Add(new CourseShare
            {
                BookId = bookId, OwnerId = userId, RecipientId = rid, SharedAt = DateTime.UtcNow,
            }),
            onSharedAsync: async toNotify =>
            {
                var ownerName = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
                await _notifications.CreateManyAsync(toNotify, NotificationType.CourseShared,
                    new Dictionary<string, string> { ["username"] = ownerName, ["courseName"] = book.DisplayName }, "/courses");
            });

        return new CourseShareResultDto
        {
            Shared = outcome.Shared,
            Skipped = outcome.Skipped.Select(s => new CourseShareSkipDto { UserId = s.UserId, Reason = s.Reason }).ToList(),
        };
    }

    /// <summary>Mit welchen Nutzern ist dieser eigene Kurs aktuell geteilt? (Für den Teilen-Dialog.)</summary>
    public async Task<List<CourseShareRecipientDto>> GetShareRecipientsAsync(int userId, int bookId)
    {
        await EnsureOwnedBookAsync(userId, bookId);
        return await _db.CourseShares
            .Where(cs => cs.BookId == bookId && cs.OwnerId == userId)
            .OrderBy(cs => cs.SharedAt)
            .Select(cs => new CourseShareRecipientDto
            {
                UserId = cs.RecipientId,
                Username = cs.Recipient.Username,
                DisplayName = cs.Recipient.Profile != null ? cs.Recipient.Profile.DisplayName : null,
                SharedAt = cs.SharedAt,
            })
            .ToListAsync();
    }

    /// <summary>Nimmt die Freigabe eines eigenen Kurses für einen Empfänger zurück (idempotent).
    /// Nur der Besitzer darf das; fremdes/nicht existierendes Buch → 403/404.</summary>
    public async Task UnshareCourseAsync(int userId, int bookId, int recipientId)
    {
        await EnsureOwnedBookAsync(userId, bookId);
        var share = await _db.CourseShares.FirstOrDefaultAsync(cs =>
            cs.BookId == bookId && cs.OwnerId == userId && cs.RecipientId == recipientId);
        if (share == null) return;
        _db.CourseShares.Remove(share);
        await _db.SaveChangesAsync();
    }
}
