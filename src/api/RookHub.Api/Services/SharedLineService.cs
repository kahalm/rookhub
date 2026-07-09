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

        return await StoreAsync(userId, repertoireId, dto.Title, rep.Name, dto.Pgn.Trim(), null, ct);
    }

    /// <summary>
    /// Teilt eine „freistehende" Linie (nicht an ein RookHub-Repertoire gebunden) — genutzt von der
    /// RepCheck-Extension, die die aktuell auf chess.com/lichess gespielte Zugfolge teilt. Der Server
    /// baut aus der SAN-Zugliste ein PGN. Dedup je Besitzer wie sonst. <c>null</c> bei leerer Zugliste.
    /// </summary>
    public async Task<ShareLineResultDto?> CreateStandaloneAsync(int userId, IEnumerable<string>? moves, string? title, CancellationToken ct = default)
    {
        var sans = (moves ?? Enumerable.Empty<string>())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .Take(600)
            .ToList();
        if (sans.Count == 0) return null;
        // Identität einer freistehenden Line = ihre Zugfolge (NICHT der variable Seitentitel) →
        // Dedup über die Züge, damit derselbe Spielstand denselben Link liefert.
        return await StoreAsync(userId, null, title, null, BuildLinePgn(sans, title), "ext|" + string.Join(' ', sans), ct);
    }

    private async Task<ShareLineResultDto?> StoreAsync(int userId, int? repertoireId, string? title, string? repertoireName, string pgn, string? dedupSource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return null;
        var hash = Sha256Hex(dedupSource ?? pgn);

        // Dedup je Besitzer über den PGN-Hash → derselbe Link bei erneutem Teilen.
        var existing = await _db.SharedLines.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.LineHash == hash, ct);
        if (existing != null) return new ShareLineResultDto { ShareToken = existing.ShareToken };

        var cleanTitle = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
        if (cleanTitle is { Length: > 200 }) cleanTitle = cleanTitle[..200];

        var entity = new SharedLine
        {
            OwnerUserId = userId,
            RepertoireId = repertoireId,
            Title = cleanTitle,
            RepertoireName = repertoireName,
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

    /// <summary>Baut aus einer SAN-Hauptlinie (ab Grundstellung) ein minimales PGN mit Zugnummern.</summary>
    private static string BuildLinePgn(List<string> sans, string? title)
    {
        var evt = string.IsNullOrWhiteSpace(title) ? "Repertoire line" : title!.Trim();
        evt = evt.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var sb = new StringBuilder();
        sb.Append("[Event \"").Append(evt).Append("\"]\n[White \"?\"]\n[Black \"?\"]\n[Result \"*\"]\n\n");
        for (var i = 0; i < sans.Count; i++)
        {
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
            sb.Append(sans[i]).Append(' ');
        }
        sb.Append("*\n");
        return sb.ToString();
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
