using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „+"-Markierungen besonders guter Stellungen (0.749.0) — markieren, zurücknehmen, auflisten. Eine Stellung je Nutzer
/// höchstens einmal (Schlüssel = FEN ohne Zugzähler); ein zweites Markieren aktualisiert nur die Herkunft, wo die neue
/// mehr weiß. Vorrat für ein späteres „Stellungen für alle nachspielen".
/// </summary>
public partial class MarkedPositionService
{
    /// <summary>So viele Markierungen hält ein Konto höchstens — eine Bremse gegen Massen-Einträge, kein Sammelziel.</summary>
    public const int MaxPerUser = 5000;

    private static readonly HashSet<string> Contexts = new() { "analysis", "board", "mistake" };

    [GeneratedRegex("^[a-h][1-8][a-h][1-8][qrbn]?$")]
    private static partial Regex UciPattern();

    private readonly AppDbContext _db;

    public MarkedPositionService(AppDbContext db) => _db = db;

    /// <summary>Stellungsschlüssel: Brett, Seite am Zug, Rochade, en passant (wie <see cref="GapSolver.PositionKey"/>).</summary>
    public static string? KeyOf(string? fen)
    {
        var norm = FenListParser.NormalizeFen(fen);
        return norm == null ? null : GapSolver.PositionKey(norm);
    }

    public async Task<List<MarkedPositionDto>> ListAsync(int userId, CancellationToken ct = default)
        => (await _db.MarkedPositions.AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Take(MaxPerUser)
                .ToListAsync(ct))
            .Select(Map).ToList();

    /// <summary>Markieren. <c>(null, "invalidFen")</c> bei unbrauchbarer FEN, <c>(null, "limit")</c> am Deckel.</summary>
    public async Task<(MarkedPositionDto? Mark, string? Error)> MarkAsync(int userId, MarkPositionInputDto input,
        CancellationToken ct = default)
    {
        var fen = FenListParser.NormalizeFen(input.Fen);
        if (fen == null) return (null, "invalidFen");
        var key = GapSolver.PositionKey(fen);

        int? savedGameId = null;
        if (input.SavedGameId is int sg && await _db.SavedGames.AnyAsync(g => g.Id == sg && g.UserId == userId, ct))
            savedGameId = sg;
        var bestUci = input.BestUci?.Trim().ToLowerInvariant();
        if (bestUci != null && !UciPattern().IsMatch(bestUci)) bestUci = null;
        var context = input.Context != null && Contexts.Contains(input.Context) ? input.Context : "analysis";
        var token = string.IsNullOrWhiteSpace(input.ShareToken) || input.ShareToken.Length > 32 ? null : input.ShareToken.Trim();
        var ply = input.Ply is >= 0 and < GameMistakeProgressService.MaxPly ? input.Ply : null;
        var clubGameId = input.ClubGameId is > 0 ? input.ClubGameId : null;

        var row = await _db.MarkedPositions.FirstOrDefaultAsync(p => p.UserId == userId && p.PositionKey == key, ct);
        if (row == null)
        {
            if (await _db.MarkedPositions.CountAsync(p => p.UserId == userId, ct) >= MaxPerUser) return (null, "limit");
            row = new MarkedPosition { UserId = userId, PositionKey = key, Fen = fen, Context = context, CreatedAt = DateTime.UtcNow };
            _db.MarkedPositions.Add(row);
        }
        // Herkunft ergänzen, nie mit „weiß nichts" überschreiben.
        row.SavedGameId ??= savedGameId;
        row.LeagueClubGameId ??= clubGameId;
        row.ShareToken ??= token;
        row.Ply ??= ply;
        row.BestUci ??= bestUci;
        await _db.SaveChangesAsync(ct);
        return (Map(row), null);
    }

    /// <summary>Markierung einer Stellung zurücknehmen; <c>false</c>, wenn es keine gab.</summary>
    public async Task<bool> UnmarkAsync(int userId, string? fen, CancellationToken ct = default)
    {
        var key = KeyOf(fen);
        if (key == null) return false;
        var row = await _db.MarkedPositions.FirstOrDefaultAsync(p => p.UserId == userId && p.PositionKey == key, ct);
        if (row == null) return false;
        _db.MarkedPositions.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static MarkedPositionDto Map(MarkedPosition p) => new()
    {
        Id = p.Id, Fen = p.Fen, PositionKey = p.PositionKey, Context = p.Context, SavedGameId = p.SavedGameId,
        ClubGameId = p.LeagueClubGameId, ShareToken = p.ShareToken, Ply = p.Ply, BestUci = p.BestUci, CreatedAt = p.CreatedAt,
    };
}
