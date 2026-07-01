using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Speichert/liest vom User auf chessable.com „gemerkte" Stellungen (RepCheck „Remember line").
/// Append-only Sammelbecken ohne festen Verwendungszweck (Anzeige folgt evtl. später).
///
/// Kursname: die Extension liefert ihn — wenn der User einen Chessable-Bearer hinterlegt hat —
/// bereits autoritativ (aus der Chessable-API) mit. Fehlt er (z. B. Userscript ohne Token, oder
/// nur DOM-Heuristik verfügbar), löst der Server ihn aus dem gespeicherten Bearer des Users auf:
/// bevorzugt aus der bereits gecachten Kursliste (<see cref="ChessableCredential.CachedCoursesJson"/>,
/// nächtlich aktualisiert), sonst best-effort per Live-Abruf.
/// </summary>
public class RememberedPositionService
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _chessable;
    private readonly ILogger<RememberedPositionService> _logger;

    public RememberedPositionService(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService chessable,
        ILogger<RememberedPositionService> logger)
    {
        _db = db;
        _encryption = encryption;
        _chessable = chessable;
        _logger = logger;
    }

    /// <summary>Mindest-Plausibilitaet einer FEN (Placement + Zugrecht); haelt offensichtlichen Müll fern.</summary>
    private static readonly Regex FenRegex =
        new(@"^[1-8rnbqkpRNBQKP/]+\s[wb]\s", RegexOptions.Compiled);

    public static bool LooksLikeFen(string? fen)
        => !string.IsNullOrWhiteSpace(fen) && fen.Length <= 120 && FenRegex.IsMatch(fen.Trim());

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>Legt eine gemerkte Stellung an; gibt sie als DTO zurueck. Wirft bei ungueltiger FEN.</summary>
    public async Task<RememberedPositionDto> SaveAsync(int userId, RememberLineInputDto dto)
    {
        if (!LooksLikeFen(dto.Fen))
            throw new ArgumentException("Invalid FEN.");

        var courseId = Clean(dto.CourseId, 32);
        var courseName = Clean(dto.CourseName, 200)
            ?? await ResolveCourseNameAsync(userId, courseId);

        var entity = new RememberedPosition
        {
            UserId = userId,
            Fen = dto.Fen.Trim(),
            CourseId = courseId,
            CourseName = courseName,
            SourceUrl = Clean(dto.SourceUrl, 1000),
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
        var list = await _db.RememberedPositions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new RememberedPositionDto
            {
                Id = p.Id,
                Fen = p.Fen,
                CourseId = p.CourseId,
                CourseName = p.CourseName,
                SourceUrl = p.SourceUrl,
                CreatedAt = p.CreatedAt,
            })
            .ToListAsync();

        // Backfill für Alt-Einträge ohne Namen: aus der (bereits vorhandenen) gecachten
        // Kursliste des Users — rein in-memory, kein Netz-Call.
        if (list.Any(p => p.CourseName is null && p.CourseId is not null))
        {
            var map = await LoadCachedCourseMapAsync(userId);
            if (map.Count > 0)
                foreach (var p in list)
                    if (p.CourseName is null && p.CourseId is not null && map.TryGetValue(p.CourseId, out var name))
                        p.CourseName = name;
        }
        return list;
    }

    /// <summary>Löst den Kursnamen aus dem gespeicherten Chessable-Bearer des Users auf:
    /// erst aus der gecachten Kursliste, sonst best-effort per Live-Abruf. Nie werfend.</summary>
    private async Task<string?> ResolveCourseNameAsync(int userId, string? courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId)) return null;

        var cred = await _db.ChessableCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is null) return null;

        // 1) Cache-first (nächtlich aktualisiert) — kein Netzwerk.
        var cached = ParseCourseMap(cred.CachedCoursesJson);
        if (cached.TryGetValue(courseId, out var cachedName)) return cachedName;

        // 2) Live-Fallback nur, wenn ein brauchbarer Bearer vorhanden ist (nicht gesperrt).
        if (cred.BlockedAt is not null) return null;
        var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
        if (string.IsNullOrWhiteSpace(bearer)) return null;

        try
        {
            var courses = await _chessable.GetCoursesAsync(bearer);
            var match = courses.FirstOrDefault(c => c.Bid == courseId);
            return Clean(match?.Name, 200);
        }
        catch (Exception ex)
        {
            // Kursname ist „nice to have" — ein Chessable-/Proxy-Ausfall darf das Merken nicht scheitern lassen.
            _logger.LogDebug(ex, "Kursname-Auflösung für User {UserId} / Kurs {CourseId} fehlgeschlagen", userId, courseId);
            return null;
        }
    }

    private async Task<Dictionary<string, string>> LoadCachedCourseMapAsync(int userId)
    {
        var json = await _db.ChessableCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.CachedCoursesJson)
            .FirstOrDefaultAsync();
        return ParseCourseMap(json);
    }

    private static Dictionary<string, string> ParseCourseMap(string? json)
    {
        var map = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            var list = JsonSerializer.Deserialize<List<ChessableCourseDto>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list is not null)
                foreach (var c in list)
                    if (!string.IsNullOrWhiteSpace(c.Bid) && !string.IsNullOrWhiteSpace(c.Name))
                        map[c.Bid] = c.Name;
        }
        catch { /* korrupter Cache → leer */ }
        return map;
    }

    private static RememberedPositionDto Map(RememberedPosition p) => new()
    {
        Id = p.Id,
        Fen = p.Fen,
        CourseId = p.CourseId,
        CourseName = p.CourseName,
        SourceUrl = p.SourceUrl,
        CreatedAt = p.CreatedAt,
    };
}
