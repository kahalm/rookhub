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
}
