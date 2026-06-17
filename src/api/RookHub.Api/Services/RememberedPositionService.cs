using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Speichert/liest vom User auf chessable.com „gemerkte" Stellungen (RepCheck „Remember line").
/// Append-only Sammelbecken ohne festen Verwendungszweck (Anzeige folgt evtl. später).
/// </summary>
public class RememberedPositionService
{
    private readonly AppDbContext _db;
    public RememberedPositionService(AppDbContext db) => _db = db;

    /// <summary>Mindest-Plausibilitaet einer FEN (Placement + Zugrecht); haelt offensichtlichen Müll fern.</summary>
    private static readonly Regex FenRegex =
        new(@"^[1-8rnbqkpRNBQKP/]+\s[wb]\s", RegexOptions.Compiled);

    public static bool LooksLikeFen(string? fen)
        => !string.IsNullOrWhiteSpace(fen) && fen.Length <= 120 && FenRegex.IsMatch(fen.Trim());

    /// <summary>Legt eine gemerkte Stellung an; gibt sie als DTO zurueck. Wirft bei ungueltiger FEN.</summary>
    public async Task<RememberedPositionDto> SaveAsync(int userId, RememberLineInputDto dto)
    {
        if (!LooksLikeFen(dto.Fen))
            throw new ArgumentException("Invalid FEN.");

        var entity = new RememberedPosition
        {
            UserId = userId,
            Fen = dto.Fen.Trim(),
            CourseId = string.IsNullOrWhiteSpace(dto.CourseId) ? null : dto.CourseId.Trim(),
            SourceUrl = string.IsNullOrWhiteSpace(dto.SourceUrl) ? null : dto.SourceUrl.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.RememberedPositions.Add(entity);
        await _db.SaveChangesAsync();
        return Map(entity);
    }

    /// <summary>Gemerkte Stellungen des Users, neueste zuerst (max <paramref name="take"/>).</summary>
    public async Task<List<RememberedPositionDto>> ListAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        return await _db.RememberedPositions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new RememberedPositionDto
            {
                Id = p.Id,
                Fen = p.Fen,
                CourseId = p.CourseId,
                SourceUrl = p.SourceUrl,
                CreatedAt = p.CreatedAt,
            })
            .ToListAsync();
    }

    private static RememberedPositionDto Map(RememberedPosition p) => new()
    {
        Id = p.Id,
        Fen = p.Fen,
        CourseId = p.CourseId,
        SourceUrl = p.SourceUrl,
        CreatedAt = p.CreatedAt,
    };
}
