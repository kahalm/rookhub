using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;
using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Chessable-Aktivität von der Extension: Zeit-Häppchen anhängen, Kurs-History je Kurs-ID und die manuelle
/// Themen-Zuordnung eines Kurses.
/// </summary>
internal sealed class ChessableActivityLog
{
    private readonly AppDbContext _db;
    private readonly Action<int> _invalidateDailySeries;

    /// <param name="invalidateDailySeries">verwirft die gecachte Tagesreihe des Users (Fassade, Codereview N9-005).</param>
    public ChessableActivityLog(AppDbContext db, Action<int> invalidateDailySeries)
    {
        _db = db;
        _invalidateDailySeries = invalidateDailySeries;
    }

    private void InvalidateDailySeries(int userId) => _invalidateDailySeries(userId);

    // ----- Chessable-Aktivität (von der Extension) -------------------------

    /// <summary>
    /// Chessable-Modus-/Navigations-Labels, die die Extension historisch fälschlich als Kursnamen
    /// meldete (Dialog-Überschriften der Trainings-SPA). Solche „Namen" werden verworfen — lieber
    /// über die Kurs-ID heilen oder gar kein Name als ein falscher.
    /// </summary>
    internal static bool IsModeLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var t = name.Trim().ToLowerInvariant();
        if (t is "practice moves" or "learn moves" or "review" or "overview" or "leaderboard"
            or "move trainer" or "movetrainer" or "next" or "previous" or "prev" or "continue"
            or "weiter" or "home" or "practice" or "learn" or "variations" or "variation")
            return true;
        // „Next/Previous chapter|variation|move|line" bzw. deutsche Entsprechungen —
        // auch Kapitel-/Zeilenlabels wie „Kapitel 7:" sind keine Kursnamen.
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(next|previous|prev|nächst\w*|naechst\w*|vorherig\w*|vorig\w*|letzt\w*)\b")
            && System.Text.RegularExpressions.Regex.IsMatch(t, "(chapter|variation|move|line|kapitel|variante|zug|linie)"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(kapitel|chapter)\s*\d*\s*:?$")) return true;
        return false;
    }

    /// <summary>Hängt ein Häppchen aktiver Chessable-Trainingszeit an (Zeitstempel serverseitig).
    /// Fließt über <see cref="ChessableProgress"/> in die Quelle „chessable" des Trackers.
    /// Modus-Labels als „Kursname" werden verworfen und — falls eine Kurs-ID mitkommt — aus der
    /// gecachten Kursliste des Users geheilt (cache-only, kein Live-Abruf im Minutentakt).</summary>
    public async Task RecordChessableActivityAsync(int userId, ChessableActivityInputDto dto)
    {
        var courseId = string.IsNullOrWhiteSpace(dto.CourseId) ? null : dto.CourseId.Trim();
        var courseName = string.IsNullOrWhiteSpace(dto.CourseName) ? null : dto.CourseName.Trim();
        if (IsModeLabel(courseName)) courseName = null;
        if (courseName is null && courseId is not null)
            courseName = await CachedChessableCourseNameAsync(userId, courseId);
        _db.ChessableActivities.Add(new ChessableActivity
        {
            UserId = userId,
            TimeSeconds = Math.Clamp(dto.SecondsActive, 0, TrainingCaps.PerChessableFlushCapSeconds),
            MovesTrained = Math.Max(0, dto.MovesTrained),
            LinesTrained = Math.Max(0, dto.LinesTrained),
            CourseKind = dto.CourseKind,
            CourseId = courseId?.Length > 32 ? courseId[..32] : courseId,
            CourseName = courseName?.Length > 200 ? courseName[..200] : courseName,
            AttemptedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
    }

    /// <summary>Kursname aus der bereits gecachten Kursliste des Users (ChessableCredential.
    /// CachedCoursesJson) — bewusst OHNE Live-Abruf: Aktivitäts-Häppchen kommen im Minutentakt.</summary>
    private async Task<string?> CachedChessableCourseNameAsync(int userId, string courseId)
    {
        var json = await _db.ChessableCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.CachedCoursesJson)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<ChessableCourseDto>>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var name = list?.FirstOrDefault(c => c.Bid == courseId)?.Name;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch { return null; }
    }

    // ----- Chessable-Kurs-History + manuelle Themen-Zuordnung --------------

    /// <summary>Gruppiert die Chessable-Aktivitäten des Users nach Kurs-ID (nur Aktivitäten MIT Kurs-ID)
    /// und liefert pro Kurs Zeit/Züge + ermitteltes Thema (manuelle Zuordnung &gt; Repertoire-Auto &gt; keins).
    /// <paramref name="unassignedOnly"/> = nur Kurse ohne feststehendes Thema.</summary>
    public async Task<List<ChessableCourseSummaryDto>> GetChessableCoursesAsync(int userId, bool unassignedOnly = false)
    {
        var acts = await _db.ChessableActivities.AsNoTracking()
            .Where(a => a.UserId == userId && a.CourseId != null && a.CourseId != "")
            .Select(a => new { a.CourseId, a.CourseName, a.CourseKind, a.TimeSeconds, a.MovesTrained, a.LinesTrained, a.AttemptedAt })
            .ToListAsync();

        var assignments = await _db.ChessableCourseThemes.AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToDictionaryAsync(t => t.CourseId, t => t.Theme);

        var summaries = acts
            .GroupBy(a => a.CourseId!)
            .Select(g =>
            {
                var latest = g.OrderByDescending(a => a.AttemptedAt).First();
                var autoKind = g.Select(a => a.CourseKind).FirstOrDefault(k => k != null);
                var hasManual = assignments.TryGetValue(g.Key, out var manual);
                // Jüngster BRAUCHBARER Name — Altbestand trägt teils Modus-Labels („Practice Moves").
                var bestName = g.OrderByDescending(a => a.AttemptedAt)
                    .Select(a => a.CourseName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && !IsModeLabel(n))
                    ?? latest.CourseName;
                return new ChessableCourseSummaryDto
                {
                    CourseId = g.Key,
                    CourseName = bestName,
                    TotalSeconds = g.Sum(a => Math.Min(a.TimeSeconds, TrainingCaps.PerChessableFlushCapSeconds)),
                    TotalMoves = g.Sum(a => a.MovesTrained),
                    TotalLines = g.Sum(a => a.LinesTrained),
                    ActivityCount = g.Count(),
                    LastActivityAt = latest.AttemptedAt,
                    AssignedTheme = hasManual ? TrainingThemes.Name(manual) : null,
                    AutoTheme = autoKind?.ToString().ToLowerInvariant(),
                    IsAssigned = hasManual || autoKind != null,
                };
            })
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();

        return unassignedOnly ? summaries.Where(s => !s.IsAssigned).ToList() : summaries;
    }

    /// <summary>Setzt/aktualisiert die manuelle Themen-Zuordnung eines Chessable-Kurses (Upsert je User+Kurs).
    /// Übernimmt einen Kursnamen, wenn (noch) keiner gespeichert ist. Liefert false, wenn die Kurs-ID leer ist.</summary>
    public async Task<bool> SetChessableCourseThemeAsync(int userId, string courseId, ChessableTheme theme)
    {
        courseId = (courseId ?? string.Empty).Trim();
        if (courseId.Length == 0) return false;
        if (courseId.Length > 32) courseId = courseId[..32];

        var existing = await _db.ChessableCourseThemes
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CourseId == courseId);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            // Kursnamen aus der jüngsten Aktivität dieses Kurses übernehmen (best-effort).
            var name = await _db.ChessableActivities.AsNoTracking()
                .Where(a => a.UserId == userId && a.CourseId == courseId && a.CourseName != null)
                .OrderByDescending(a => a.AttemptedAt)
                .Select(a => a.CourseName)
                .FirstOrDefaultAsync();
            _db.ChessableCourseThemes.Add(new ChessableCourseTheme
            {
                UserId = userId, CourseId = courseId, CourseName = name,
                Theme = theme, CreatedAt = now, UpdatedAt = now,
            });
        }
        else
        {
            existing.Theme = theme;
            existing.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
        return true;
    }

    /// <summary>Entfernt die manuelle Themen-Zuordnung eines Kurses (Rückfall auf Auto/unzugeordnet).
    /// Liefert false, wenn keine Zuordnung existierte.</summary>
    public async Task<bool> ClearChessableCourseThemeAsync(int userId, string courseId)
    {
        courseId = (courseId ?? string.Empty).Trim();
        var existing = await _db.ChessableCourseThemes
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CourseId == courseId);
        if (existing == null) return false;
        _db.ChessableCourseThemes.Remove(existing);
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
        return true;
    }
}
