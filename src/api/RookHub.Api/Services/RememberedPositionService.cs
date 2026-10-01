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
/// nur DOM-Heuristik verfügbar), nimmt der Server ihn aus der bereits gecachten Kursliste des Users
/// (<see cref="ChessableCredential.CachedCoursesJson"/>) — bewusst OHNE Live-Abruf (N8-006): der
/// Extension-Weg läuft auch mit <c>Chessable:Enabled=false</c>, und ein Abruf je Anfrage ginge am
/// Schalter und am Bearer-Breaker vorbei über den geteilten VPN-Tunnel. Dieselbe Regel wie
/// <see cref="TrainingGoalService"/> und <see cref="ChessableImportService"/>; kennt der Cache den Kurs
/// noch nicht, trägt <see cref="ListAsync"/> den Namen später nach.
/// </summary>
public class RememberedPositionService
{
    private readonly AppDbContext _db;

    public RememberedPositionService(AppDbContext db)
    {
        _db = db;
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

    /// <summary>
    /// Herkunft einer gemerkten Stellung, wie sie die Liste als Link rendert (Codereview F4-014; PD-011 liess den
    /// Wert ungeprueft, solange er nie als href erschien): eine absolute http(s)-Adresse (die Chessable-Seite aus
    /// RepCheck) oder ein Pfad IN der App (<c>/analysis/jobs</c>). Alles andere — javascript:, data:, ein
    /// protokoll-relatives <c>//host</c> — wird <c>null</c>. Beim Speichern und beim Ausliefern (Altbestand).
    /// </summary>
    public static string? SafeSourceUrl(string? raw)
    {
        var s = Clean(raw, 1000);
        if (s is null) return null;
        if (s.StartsWith('/')) return s.StartsWith("//") || s.StartsWith("/\\") ? null : s;
        return Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? s : null;
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
            SourceUrl = SafeSourceUrl(dto.SourceUrl),
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
        foreach (var p in list) p.SourceUrl = SafeSourceUrl(p.SourceUrl);

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
        await AttachAnalysisAsync(userId, list);
        return list;
    }

    /// <summary>Hintergrund-Analyseaufträge zu den Stellungen anhängen (Match über die ersten 4 FEN-Felder —
    /// Zugzähler unterscheiden Chessable-FEN und Analyse-FEN oft). Bei mehreren Aufträgen je Stellung der jüngste.</summary>
    private async Task AttachAnalysisAsync(int userId, List<RememberedPositionDto> list)
    {
        if (list.Count == 0) return;
        // Bewusst OHNE ResultJson: die Roh-Zeilen sind bis zu 256 KB groß, und diese Liste braucht nur die
        // Bewertung — die steht seit v0.383.0 als EvalText in der Zeile.
        var jobs = await _db.AnalysisJobs.AsNoTracking()
            .Where(j => j.UserId == userId)
            .Select(j => new { j.Id, j.Fen, j.Status, j.ReachedDepth, j.TargetDepth, j.MultiPv, j.EvalText, j.UpdatedAt })
            .ToListAsync();
        if (jobs.Count == 0) return;
        var byFen = jobs
            .GroupBy(j => RepertoireAnalyzeService.NormalizeFen(j.Fen))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.UpdatedAt).First());
        foreach (var p in list)
        {
            if (!byFen.TryGetValue(RepertoireAnalyzeService.NormalizeFen(p.Fen), out var j)) continue;
            p.Analysis = new RememberedAnalysisDto(j.Id, j.Status.ToString().ToLowerInvariant(), j.ReachedDepth, j.TargetDepth,
                j.MultiPv, j.EvalText, j.UpdatedAt);
        }
    }

    /// <summary>Löscht eine gemerkte Stellung des Users (idempotent). <c>false</c> wenn nicht vorhanden/fremd.</summary>
    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var entity = await _db.RememberedPositions
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (entity is null) return false;
        _db.RememberedPositions.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Kursname aus der gecachten Kursliste des Users — kein Netzwerk, nie werfend (N8-006).</summary>
    private async Task<string?> ResolveCourseNameAsync(int userId, string? courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId)) return null;
        var cached = await LoadCachedCourseMapAsync(userId);
        return cached.TryGetValue(courseId, out var name) ? Clean(name, 200) : null;
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
