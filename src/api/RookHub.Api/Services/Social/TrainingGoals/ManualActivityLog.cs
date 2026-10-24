using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>Manuelle Offline-Aktivitäten (selbst gemeldet): auflisten, anlegen, ändern, löschen.</summary>
internal sealed class ManualActivityLog
{
    private readonly AppDbContext _db;
    private readonly Action<int> _invalidateDailySeries;

    /// <param name="invalidateDailySeries">verwirft die gecachte Tagesreihe des Users (Fassade, Codereview N9-005).</param>
    public ManualActivityLog(AppDbContext db, Action<int> invalidateDailySeries)
    {
        _db = db;
        _invalidateDailySeries = invalidateDailySeries;
    }

    private void InvalidateDailySeries(int userId) => _invalidateDailySeries(userId);

    // ----- Manuelle Offline-Aktivitäten (selbst gemeldet) ------------------

    /// <summary>Eigene manuelle Einträge des Users (neueste zuerst), limitiert.</summary>
    public async Task<List<ManualActivityDto>> ListManualAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        return await _db.ManualActivities.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new ManualActivityDto
            {
                Id = m.Id,
                Date = m.Date.ToString("yyyy-MM-dd"),
                Kind = m.Kind,
                Amount = m.Amount,
                Note = m.Note,
                Theme = m.Theme,
            })
            .ToListAsync();
    }

    /// <summary>Legt einen manuellen Eintrag an. Wirft <see cref="ArgumentException"/> bei ungültigem Datum/Zukunft.</summary>
    public async Task<ManualActivityDto> AddManualAsync(int userId, ManualActivityInputDto dto)
    {
        var date = ParseManualDate(dto.Date);
        var entity = new ManualActivity
        {
            UserId = userId,
            Date = date,
            Kind = dto.Kind,
            Amount = ClampAmount(dto.Kind, dto.Amount),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            Theme = dto.Theme,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ManualActivities.Add(entity);
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
        return ToDto(entity);
    }

    /// <summary>Ändert einen eigenen manuellen Eintrag. Gibt null zurück, wenn er nicht existiert/nicht dem User gehört.</summary>
    public async Task<ManualActivityDto?> UpdateManualAsync(int userId, int id, ManualActivityInputDto dto)
    {
        var entity = await _db.ManualActivities.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (entity == null) return null;
        entity.Date = ParseManualDate(dto.Date);
        entity.Kind = dto.Kind;
        entity.Amount = ClampAmount(dto.Kind, dto.Amount);
        entity.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        entity.Theme = dto.Theme;
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
        return ToDto(entity);
    }

    /// <summary>Löscht einen eigenen manuellen Eintrag. true, wenn etwas gelöscht wurde.</summary>
    public async Task<bool> DeleteManualAsync(int userId, int id)
    {
        var entity = await _db.ManualActivities.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (entity == null) return false;
        _db.ManualActivities.Remove(entity);
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
        return true;
    }

    /// <summary>Parst yyyy-MM-dd und lehnt Zukunftsdaten (UTC) ab.</summary>
    private static DateOnly ParseManualDate(string raw)
    {
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date))
            throw new ArgumentException("Ungültiges Datum (erwartet yyyy-MM-dd).");
        if (date > DateOnly.FromDateTime(DateTime.UtcNow.Date))
            throw new ArgumentException("Datum darf nicht in der Zukunft liegen.");
        return date;
    }

    /// <summary>OTB-Partien: 1–<see cref="TrainingCaps.ManualGamesCap"/>; Minuten-Arten: 1–<see cref="TrainingCaps.ManualMinutesCap"/>.</summary>
    private static int ClampAmount(ManualActivityKind kind, int amount)
        => kind == ManualActivityKind.OtbGame
            ? Math.Clamp(amount, 1, TrainingCaps.ManualGamesCap)
            : Math.Clamp(amount, 1, TrainingCaps.ManualMinutesCap);

    internal static ManualActivityDto ToDto(ManualActivity m) => new()
    {
        Id = m.Id,
        Date = m.Date.ToString("yyyy-MM-dd"),
        Kind = m.Kind,
        Amount = m.Amount,
        Note = m.Note,
        Theme = m.Theme,
    };
}
