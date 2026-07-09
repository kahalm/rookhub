using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Erzeugt und liest öffentliche Nur-Ansehen-Links für einzelne Repertoire-Linien
/// (<c>/l/{token}</c>). Analog zum öffentlichen Partie-Link (<see cref="SavedGameService"/>):
/// die Linie wird als eigenständiges PGN-Snapshot gespeichert, der Link ist unabhängig vom
/// Original-Repertoire.
/// </summary>
public class SharedLineService
{
    private readonly AppDbContext _db;

    public SharedLineService(AppDbContext db) => _db = db;

    /// <summary>
    /// Legt einen Teilen-Link für eine Linie des Repertoires <paramref name="repertoireId"/> an.
    /// Zugriff: Besitzer ODER jemand, mit dem das Repertoire geteilt ist. Dieselbe Linie erneut
    /// geteilt (gleicher PGN-Hash je Besitzer) liefert den bestehenden Link zurück (kein Duplikat).
    /// Gibt <c>null</c> zurück, wenn kein Zugriff / Repertoire nicht existiert.
    /// </summary>
    public async Task<ShareLineResultDto?> CreateAsync(int userId, int repertoireId, ShareLineInputDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Pgn) || !RepertoireService.LooksLikePgn(dto.Pgn)) return null;

        var rep = await _db.Repertoires.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repertoireId, ct);
        if (rep == null) return null;

        var isOwner = rep.UserId == userId;
        var isRecipient = !isOwner && await _db.RepertoireShares
            .AnyAsync(s => s.RepertoireId == repertoireId && s.RecipientId == userId, ct);
        if (!isOwner && !isRecipient) return null;

        var pgn = dto.Pgn.Trim();
        var hash = Sha256Hex(pgn);

        // Dedup je Besitzer über den PGN-Hash → derselbe Link bei erneutem Teilen.
        var existing = await _db.SharedLines.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.LineHash == hash, ct);
        if (existing != null) return new ShareLineResultDto { ShareToken = existing.ShareToken };

        var title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title!.Trim();
        if (title is { Length: > 200 }) title = title[..200];

        var entity = new SharedLine
        {
            OwnerUserId = userId,
            RepertoireId = repertoireId,
            Title = title,
            RepertoireName = rep.Name,
            Pgn = pgn,
            LineHash = hash,
            ShareToken = await GenerateUniqueTokenAsync(ct),
            CreatedAt = DateTime.UtcNow,
        };
        _db.SharedLines.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race: paralleler Teilen-Klick derselben Linie hat den (Owner,LineHash)-Unique zuerst belegt.
            _db.ChangeTracker.Clear();
            var raced = await _db.SharedLines.AsNoTracking()
                .FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.LineHash == hash, ct);
            if (raced != null) return new ShareLineResultDto { ShareToken = raced.ShareToken };
            throw;
        }
        return new ShareLineResultDto { ShareToken = entity.ShareToken };
    }

    /// <summary>Öffentliche Sicht über das Token; <c>null</c> wenn unbekannt.</summary>
    public async Task<SharedLineDto?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var s = await _db.SharedLines.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShareToken == token, ct);
        if (s == null) return null;
        return new SharedLineDto
        {
            ShareToken = s.ShareToken,
            Title = s.Title,
            RepertoireName = s.RepertoireName,
            Pgn = s.Pgn,
            CreatedAt = s.CreatedAt,
        };
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<string> GenerateUniqueTokenAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = NewToken();
            if (!await _db.SharedLines.AnyAsync(s => s.ShareToken == token, ct)) return token;
        }
        return NewToken();
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
