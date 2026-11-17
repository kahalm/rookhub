using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>Was ein Teilen-Link gerade ist: gültig (mit Eintrag), abgelaufen bzw. widerrufen (410) oder unbekannt (404).</summary>
public enum TrainingShareState { Valid, Gone, Unknown }

/// <summary>
/// Teilen-Links eines Eintrags der Trainerstunden (<see cref="TrainingLessonShare"/>, Wunsch 2026-11-17). Anlegen, Auflisten
/// und Widerrufen NUR durch das Konto des Eintrags (auch ein Admin nur an eigenen Einträgen — er teilt keine fremden
/// Gespräche); ein fremder Eintrag ist 404. Gelesen wird über <see cref="ResolveAsync"/>: jede Gast-Anfrage (Stunde UND
/// jede Bereichsanfrage des Videos) schlägt das Token neu nach, die Id des Eintrags kommt nur aus der Zeile — eine Id in
/// der Adresse gibt es auf dem Gast-Weg nicht. Muster: <c>AnonGameShares</c> (Token im Klartext, erst auf Zuruf).
/// </summary>
public sealed class TrainingLessonShares(AppDbContext db)
{
    /// <summary>Wählbare Fristen in Tagen (Oberfläche: 1 / 7 / 30, Vorgabe 7).</summary>
    public static readonly int[] AllowedDays = [1, 7, 30];
    public const int DefaultDays = 7;

    /// <summary>So viele gleichzeitig aktive Links je Eintrag; mehr ist kein Teilen mehr, sondern ein Leck.</summary>
    public const int MaxActivePerLesson = 20;

    private Task<bool> OwnsAsync(int userId, int lessonId, CancellationToken ct) =>
        db.TrainingLessons.AnyAsync(l => l.Id == lessonId && l.UserId == userId, ct);

    private static TrainingShareDto ToDto(TrainingLessonShare s) => new(s.Id, s.Token, s.CreatedAt, s.ExpiresAt);

    /// <summary>Aktive Links (nicht abgelaufen, nicht widerrufen), neueste zuerst; <c>null</c> = fremder/unbekannter Eintrag.</summary>
    public async Task<List<TrainingShareDto>?> ListAsync(int userId, int lessonId, CancellationToken ct = default)
    {
        if (!await OwnsAsync(userId, lessonId, ct)) return null;
        var now = DateTime.UtcNow;
        var rows = await db.TrainingLessonShares.AsNoTracking()
            .Where(s => s.LessonId == lessonId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>Neuer Link mit <paramref name="days"/> Tagen Frist (<c>null</c> = <see cref="DefaultDays"/>); <c>null</c> = fremd/unbekannt.</summary>
    public async Task<TrainingShareDto?> CreateAsync(int userId, int lessonId, int? days, CancellationToken ct = default)
    {
        var d = days ?? DefaultDays;
        if (!AllowedDays.Contains(d)) throw new DomainValidationException("Frist muss 1, 7 oder 30 Tage sein.");
        if (!await OwnsAsync(userId, lessonId, ct)) return null;
        var now = DateTime.UtcNow;
        var active = await db.TrainingLessonShares.CountAsync(s => s.LessonId == lessonId && s.RevokedAt == null && s.ExpiresAt > now, ct);
        if (active >= MaxActivePerLesson)
            throw new DomainValidationException($"Höchstens {MaxActivePerLesson} aktive Links je Eintrag — bitte zuerst einen widerrufen.");
        var share = new TrainingLessonShare
        {
            LessonId = lessonId,
            // 256 Bit; der Unique-Index fängt die (praktisch ausgeschlossene) Kollision.
            Token = await ShareTokens.NewUniqueAsync(t => db.TrainingLessonShares.AnyAsync(x => x.Token == t, ct), SecretTokens.DefaultBytes),
            CreatedAt = now,
            ExpiresAt = now.AddDays(d),
        };
        db.TrainingLessonShares.Add(share);
        await db.SaveChangesAsync(ct);
        return ToDto(share);
    }

    /// <summary>Widerrufen; <c>false</c> = fremder Eintrag, unbekannter oder schon widerrufener Link.</summary>
    public async Task<bool> RevokeAsync(int userId, int lessonId, int shareId, CancellationToken ct = default)
    {
        if (!await OwnsAsync(userId, lessonId, ct)) return false;
        var share = await db.TrainingLessonShares.FirstOrDefaultAsync(s => s.Id == shareId && s.LessonId == lessonId && s.RevokedAt == null, ct);
        if (share is null) return false;
        share.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Der Eintrag hinter dem Token — nur wenn der Link gilt. Ein Token, das nicht wie eines aussieht, wird gar nicht
    /// erst nachgeschlagen.</summary>
    public async Task<(TrainingShareState State, int LessonId)> ResolveAsync(string? token, CancellationToken ct = default)
    {
        if (!LooksLikeToken(token)) return (TrainingShareState.Unknown, 0);
        var row = await db.TrainingLessonShares.AsNoTracking()
            .Where(s => s.Token == token).Select(s => new { s.LessonId, s.ExpiresAt, s.RevokedAt }).FirstOrDefaultAsync(ct);
        if (row is null) return (TrainingShareState.Unknown, 0);
        if (row.RevokedAt != null || row.ExpiresAt <= DateTime.UtcNow) return (TrainingShareState.Gone, 0);
        return (TrainingShareState.Valid, row.LessonId);
    }

    /// <summary>Base64URL, 20–64 Zeichen.</summary>
    internal static bool LooksLikeToken(string? token) =>
        token is { Length: >= 20 and <= 64 } && token.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');
}
