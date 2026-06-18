using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Speichert/liest vom User auf chess.com/lichess gespeicherte Partien (RepCheck „Partie speichern").
/// Aus der SAN-Zugliste + Metadaten wird serverseitig ein PGN gebaut. Jede Partie bekommt ein
/// eindeutiges ShareToken für den öffentlichen Teilen-Link.
/// </summary>
public class SavedGameService
{
    private readonly AppDbContext _db;
    public SavedGameService(AppDbContext db) => _db = db;

    private static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase)
        { "chess.com", "lichess" };

    private static readonly HashSet<string> AllowedResults = new() { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>Normalisiert die gemeldete Herkunft auf <c>chess.com</c>/<c>lichess</c> (sonst null).</summary>
    public static string? NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var s = source.Trim().ToLowerInvariant();
        if (s is "chesscom" or "chess.com" or "www.chess.com") return "chess.com";
        if (s is "lichess" or "lichess.org") return "lichess";
        return AllowedSources.Contains(s) ? s : null;
    }

    /// <summary>Legt eine gespeicherte Partie an (oder gibt die bereits vorhandene zurück, wenn
    /// dieselbe ExternalId für denselben User+Source schon existiert — verhindert Doppel-Klicks).
    /// Wirft bei ungültiger Eingabe.</summary>
    public async Task<SavedGameDetailDto> SaveAsync(int userId, SaveGameInputDto dto)
    {
        var source = NormalizeSource(dto.Source) ?? throw new ArgumentException("Invalid source.");
        var moves = (dto.Moves ?? new())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (moves.Count == 0) throw new ArgumentException("No moves.");
        if (moves.Count > 600) throw new ArgumentException("Too many moves (max 600 plies).");

        var externalId = string.IsNullOrWhiteSpace(dto.ExternalId) ? null : dto.ExternalId.Trim();

        // Dedup: gleicher User + Source + ExternalId → bestehende Partie zurückgeben.
        if (externalId != null)
        {
            var existing = await _db.SavedGames.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == userId && g.Source == source && g.ExternalId == externalId);
            if (existing != null) return MapDetail(existing);
        }

        var result = dto.Result?.Trim();
        if (result == null || !AllowedResults.Contains(result)) result = "*";

        var entity = new SavedGame
        {
            UserId = userId,
            Source = source,
            ExternalId = externalId,
            White = Clip(dto.White, 120),
            Black = Clip(dto.Black, 120),
            Result = result,
            PlayedAt = dto.PlayedAt,
            SourceUrl = Clip(dto.SourceUrl, 1000),
            Pgn = BuildPgn(moves, dto, result),
            ShareToken = await GenerateUniqueTokenAsync(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.SavedGames.Add(entity);
        await _db.SaveChangesAsync();
        return MapDetail(entity);
    }

    /// <summary>Gespeicherte Partien des Users, neueste zuerst (ohne PGN).</summary>
    public async Task<List<SavedGameDto>> ListAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        return await _db.SavedGames.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.CreatedAt)
            .Take(take)
            .Select(g => new SavedGameDto
            {
                Id = g.Id,
                Source = g.Source,
                White = g.White,
                Black = g.Black,
                Result = g.Result,
                PlayedAt = g.PlayedAt,
                SourceUrl = g.SourceUrl,
                ShareToken = g.ShareToken,
                MoveCount = CountPlies(g.Pgn),
                CreatedAt = g.CreatedAt,
            })
            .ToListAsync();
    }

    /// <summary>Detail einer eigenen Partie inkl. PGN; null wenn nicht gefunden / fremd.</summary>
    public async Task<SavedGameDetailDto?> GetAsync(int userId, int id)
    {
        var g = await _db.SavedGames.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        return g == null ? null : MapDetail(g);
    }

    /// <summary>Löscht eine eigene Partie; false wenn nicht gefunden / fremd.</summary>
    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var g = await _db.SavedGames.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (g == null) return false;
        _db.SavedGames.Remove(g);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Öffentliche Sicht über das ShareToken; null wenn unbekannt.</summary>
    public async Task<SharedGameDto?> GetSharedAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = await _db.SavedGames.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShareToken == token);
        return g == null ? null : new SharedGameDto
        {
            Source = g.Source,
            White = g.White,
            Black = g.Black,
            Result = g.Result,
            PlayedAt = g.PlayedAt,
            SourceUrl = g.SourceUrl,
            Pgn = g.Pgn,
            CreatedAt = g.CreatedAt,
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string? Clip(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    /// <summary>Baut ein PGN aus SAN-Zugliste + Headern (Seven-Tag-Roster, Best-Effort).</summary>
    public static string BuildPgn(List<string> moves, SaveGameInputDto dto, string result)
    {
        var sb = new StringBuilder();
        sb.Append("[Event \"RepCheck saved game\"]\n");
        sb.Append("[Site \"").Append(Header(dto.SourceUrl)).Append("\"]\n");
        sb.Append("[Date \"").Append(dto.PlayedAt?.ToString("yyyy.MM.dd") ?? "????.??.??").Append("\"]\n");
        sb.Append("[White \"").Append(Header(dto.White)).Append("\"]\n");
        sb.Append("[Black \"").Append(Header(dto.Black)).Append("\"]\n");
        sb.Append("[Result \"").Append(result).Append("\"]\n\n");

        for (int i = 0; i < moves.Count; i++)
        {
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
            sb.Append(moves[i]).Append(' ');
        }
        sb.Append(result);
        return sb.ToString();
    }

    /// <summary>Header-Wert säubern: leere → "?", Anführungszeichen/Zeilenumbrüche entfernen.</summary>
    private static string Header(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        return value.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    /// <summary>Zählt die Halbzüge eines gebauten PGN (Movetext nach der Leerzeile).</summary>
    private static int CountPlies(string pgn)
    {
        var idx = pgn.IndexOf("\n\n", StringComparison.Ordinal);
        var movetext = idx >= 0 ? pgn[(idx + 2)..] : pgn;
        var count = 0;
        foreach (var token in movetext.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 0) continue;
            if (char.IsDigit(token[0]) && token.Contains('.')) continue;   // Zugnummer "12."
            if (AllowedResults.Contains(token)) continue;                  // Ergebnis-Token
            count++;
        }
        return count;
    }

    private async Task<string> GenerateUniqueTokenAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = NewToken();
            if (!await _db.SavedGames.AnyAsync(g => g.ShareToken == token)) return token;
        }
        return NewToken();   // extrem unwahrscheinlicher Kollisions-Fallback
    }

    /// <summary>URL-sicheres Zufallstoken (~22 Zeichen aus 16 Bytes).</summary>
    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static SavedGameDetailDto MapDetail(SavedGame g) => new()
    {
        Id = g.Id,
        Source = g.Source,
        White = g.White,
        Black = g.Black,
        Result = g.Result,
        PlayedAt = g.PlayedAt,
        SourceUrl = g.SourceUrl,
        ShareToken = g.ShareToken,
        MoveCount = CountPlies(g.Pgn),
        CreatedAt = g.CreatedAt,
        Pgn = g.Pgn,
    };
}
