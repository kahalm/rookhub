using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Verwaltung + Zeitsteuerung der Kalkulations-Ausgaben (Phase 1). Verwaltung nur durch Buch-Besitzer
/// oder Admin. Das eigentliche Sichtbarkeits-Gating der Kapitel liegt in <see cref="CalculationService"/>
/// (dort werden die Stellungen ausgeliefert) — hier nur CRUD und die Betrachter-Liste (freigegebene
/// Ausgaben inkl. Video).
/// </summary>
public class CalcEditionService
{
    private readonly AppDbContext _db;
    public CalcEditionService(AppDbContext db) => _db = db;

    /// <summary>Darf der User die Ausgaben dieses Buchs verwalten? (Besitzer oder Admin.)</summary>
    public async Task<bool> CanManageAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
        => isAdmin || await _db.Books.AnyAsync(b => b.Id == bookId && b.OwnerUserId == userId, ct);

    private static CalcEditionDto Map(CalcEdition e, DateTime now) => new()
    {
        Id = e.Id, BookId = e.BookId, Chapter = e.Chapter, Title = e.Title, VideoUrl = e.VideoUrl,
        PublishAt = e.PublishAt, TesterPreviewAt = e.TesterPreviewAt, Released = e.PublishAt <= now,
    };

    /// <summary>Alle Ausgaben eines Buchs (Verwaltungssicht — inkl. noch nicht freigegebener).</summary>
    public async Task<List<CalcEditionDto>> ListAsync(int bookId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var eds = await _db.CalcEditions.Where(e => e.BookId == bookId).OrderBy(e => e.PublishAt).ToListAsync(ct);
        return eds.Select(e => Map(e, now)).ToList();
    }

    /// <summary>Für den Betrachter sichtbare Ausgaben (Phase 1: bereits freigegeben) inkl. Video.</summary>
    public async Task<List<CalcEditionDto>> ListVisibleAsync(int bookId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var eds = await _db.CalcEditions.Where(e => e.BookId == bookId && e.PublishAt <= now)
            .OrderBy(e => e.PublishAt).ToListAsync(ct);
        return eds.Select(e => Map(e, now)).ToList();
    }

    /// <summary>Upsert je (Buch, Kapitel).</summary>
    public async Task<CalcEditionDto> UpsertAsync(int bookId, CalcEditionInputDto input, CancellationToken ct = default)
    {
        var chapter = input.Chapter.Trim();
        var now = DateTime.UtcNow;
        var e = await _db.CalcEditions.FirstOrDefaultAsync(x => x.BookId == bookId && x.Chapter == chapter, ct);
        if (e is null)
        {
            e = new CalcEdition { BookId = bookId, Chapter = chapter, CreatedAt = now };
            _db.CalcEditions.Add(e);
        }
        e.Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim();
        e.VideoUrl = string.IsNullOrWhiteSpace(input.VideoUrl) ? null : input.VideoUrl.Trim();
        e.PublishAt = input.PublishAt;
        e.TesterPreviewAt = input.TesterPreviewAt;
        e.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return Map(e, now);
    }

    public async Task<bool> DeleteAsync(int bookId, int editionId, CancellationToken ct = default)
    {
        var e = await _db.CalcEditions.FirstOrDefaultAsync(x => x.Id == editionId && x.BookId == bookId, ct);
        if (e is null) return false;
        _db.CalcEditions.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ===== Privater Verteiler (Phase 2) =====================================

    /// <summary>Mitglieder des Serien-Verteilers eines Buchs (Verwaltungssicht, inkl. Benutzername).</summary>
    public async Task<List<CalcSeriesMemberDto>> ListMembersAsync(int bookId, CancellationToken ct = default)
    {
        var members = await _db.CalcSeriesMembers.Where(m => m.BookId == bookId)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var ids = members.Select(m => m.UserId).ToList();
        var names = await _db.AppUsers.Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return members.Select(m => new CalcSeriesMemberDto
        {
            UserId = m.UserId,
            Username = names.TryGetValue(m.UserId, out var n) ? n : "?",
            IsTester = m.IsTester,
            CreatedAt = m.CreatedAt,
        }).ToList();
    }

    /// <summary>Mitglied hinzufügen oder Tester-Häkchen ändern (per Benutzername, case-insensitiv).
    /// Gibt das Mitglied zurück; <c>null</c>, wenn es keinen Nutzer mit diesem Namen gibt.</summary>
    public async Task<CalcSeriesMemberDto?> UpsertMemberAsync(int bookId, string username, bool isTester, CancellationToken ct = default)
    {
        var name = (username ?? string.Empty).Trim();
        if (name.Length == 0) return null;
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username.ToLower() == name.ToLower(), ct);
        if (user is null) return null;

        var existing = await _db.CalcSeriesMembers.FirstOrDefaultAsync(m => m.BookId == bookId && m.UserId == user.Id, ct);
        if (existing is null)
        {
            existing = new CalcSeriesMember { BookId = bookId, UserId = user.Id, IsTester = isTester, CreatedAt = DateTime.UtcNow };
            _db.CalcSeriesMembers.Add(existing);
        }
        else
        {
            existing.IsTester = isTester;
        }
        await _db.SaveChangesAsync(ct);
        return new CalcSeriesMemberDto { UserId = user.Id, Username = user.Username, IsTester = existing.IsTester, CreatedAt = existing.CreatedAt };
    }

    /// <summary>Mitglied entfernen. <c>false</c>, wenn es nicht (mehr) im Verteiler stand.</summary>
    public async Task<bool> RemoveMemberAsync(int bookId, int userId, CancellationToken ct = default)
    {
        var m = await _db.CalcSeriesMembers.FirstOrDefaultAsync(x => x.BookId == bookId && x.UserId == userId, ct);
        if (m is null) return false;
        _db.CalcSeriesMembers.Remove(m);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>„Gesehen"-Vermerke aller Ausgaben eines Buchs (Phase 3): welches Mitglied welche Woche
    /// wann geöffnet hat, neuste zuletzt. Nur Besitzer/Admin (Controller prüft).</summary>
    public async Task<List<CalcEditionViewDto>> ListViewsAsync(int bookId, CancellationToken ct = default)
    {
        var editions = await _db.CalcEditions.Where(e => e.BookId == bookId)
            .Select(e => new { e.Id, e.Chapter }).ToListAsync(ct);
        var chapterById = editions.ToDictionary(e => e.Id, e => e.Chapter);
        var ids = chapterById.Keys.ToList();
        var views = await _db.CalcEditionViews.Where(v => ids.Contains(v.CalcEditionId)).ToListAsync(ct);
        var userIds = views.Select(v => v.UserId).Distinct().ToList();
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return views.OrderBy(v => v.ViewedAt).Select(v => new CalcEditionViewDto
        {
            EditionId = v.CalcEditionId,
            Chapter = chapterById.TryGetValue(v.CalcEditionId, out var c) ? c : string.Empty,
            UserId = v.UserId,
            Username = names.TryGetValue(v.UserId, out var n) ? n : "?",
            ViewedAt = v.ViewedAt,
        }).ToList();
    }
}
