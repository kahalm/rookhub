using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Effektives Ziel des Users (persönlich &gt; Gruppen-Vorlage, zuletzt aktualisierte &gt; keins) und die Pflege
/// von persönlichem Ziel und Gruppen-Vorlage.
/// </summary>
internal sealed class TrainingGoalStore
{
    private readonly AppDbContext _db;
    private readonly Action<int> _invalidateDailySeries;

    /// <param name="invalidateDailySeries">verwirft die gecachte Tagesreihe des Users (Fassade, Codereview N9-005).</param>
    public TrainingGoalStore(AppDbContext db, Action<int> invalidateDailySeries)
    {
        _db = db;
        _invalidateDailySeries = invalidateDailySeries;
    }

    private void InvalidateDailySeries(int userId) => _invalidateDailySeries(userId);

    // ----- Ziel-Auflösung --------------------------------------------------

    /// <summary>Effektives Ziel des Users: persönlich &gt; Gruppen-Vorlage (zuletzt aktualisierte) &gt; keins.</summary>
    public async Task<TrainingGoalDto> GetEffectiveGoalAsync(int userId)
    {
        var personal = await _db.UserTrainingGoals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId);
        if (personal != null)
            return Map(personal.DailyMinutes, personal.PlayGames, personal.WeeklyDaysTarget, "personal", null);

        var mine = GroupMembership.GroupIdsOf(_db, userId);
        var tmpl = await _db.GroupTrainingGoals.AsNoTracking()
            .Where(g => mine.Contains(g.GroupId))
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new { g.DailyMinutes, g.PlayGames, g.WeeklyDaysTarget, GroupName = g.Group!.Name })
            .FirstOrDefaultAsync();
        if (tmpl != null)
            return Map(tmpl.DailyMinutes, tmpl.PlayGames, tmpl.WeeklyDaysTarget, "group", tmpl.GroupName);

        return new TrainingGoalDto { Source = "none" };
    }

    /// <summary>Setzt/aktualisiert den persönlichen Override des Users und gibt das neue effektive Ziel zurück.</summary>
    public async Task<TrainingGoalDto> SetPersonalGoalAsync(int userId, TrainingGoalInputDto dto)
    {
        var goal = await _db.UserTrainingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
        var now = DateTime.UtcNow;
        if (goal == null)
        {
            goal = new UserTrainingGoal { UserId = userId, CreatedAt = now };
            _db.UserTrainingGoals.Add(goal);
        }
        Apply(goal, dto);
        goal.UpdatedAt = now;
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);
        return Map(goal.DailyMinutes, goal.PlayGames, goal.WeeklyDaysTarget, "personal", null);
    }

    /// <summary>Entfernt den persönlichen Override → der User fällt auf die Gruppen-Vorlage (falls vorhanden) zurück.</summary>
    public async Task<TrainingGoalDto> DeletePersonalGoalAsync(int userId)
    {
        var goal = await _db.UserTrainingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
        if (goal != null)
        {
            _db.UserTrainingGoals.Remove(goal);
            await _db.SaveChangesAsync();
            InvalidateDailySeries(userId);
        }
        return await GetEffectiveGoalAsync(userId);
    }

    // ----- Gruppen-Vorlage (Admin) -----------------------------------------

    public async Task<TrainingGoalDto> GetGroupGoalAsync(int groupId)
    {
        var g = await _db.GroupTrainingGoals.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId);
        return g == null
            ? new TrainingGoalDto { Source = "none" }
            : Map(g.DailyMinutes, g.PlayGames, g.WeeklyDaysTarget, "group", null);
    }

    public async Task<TrainingGoalDto> SetGroupGoalAsync(int groupId, TrainingGoalInputDto dto)
    {
        var g = await _db.GroupTrainingGoals.FirstOrDefaultAsync(x => x.GroupId == groupId);
        var now = DateTime.UtcNow;
        if (g == null)
        {
            g = new GroupTrainingGoal { GroupId = groupId, CreatedAt = now };
            _db.GroupTrainingGoals.Add(g);
        }
        Apply(g, dto);
        g.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return Map(g.DailyMinutes, g.PlayGames, g.WeeklyDaysTarget, "group", null);
    }

    public async Task DeleteGroupGoalAsync(int groupId)
    {
        var g = await _db.GroupTrainingGoals.FirstOrDefaultAsync(x => x.GroupId == groupId);
        if (g != null)
        {
            _db.GroupTrainingGoals.Remove(g);
            await _db.SaveChangesAsync();
        }
    }

    private static void Apply(GroupTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.DailyMinutes = dto.DailyMinutes;
        g.PlayGames = dto.PlayGames;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static void Apply(UserTrainingGoal g, TrainingGoalInputDto dto)
    {
        g.DailyMinutes = dto.DailyMinutes;
        g.PlayGames = dto.PlayGames;
        g.WeeklyDaysTarget = dto.WeeklyDaysTarget;
    }

    private static TrainingGoalDto Map(int dailyMinutes, int playGames, int weekly, string source, string? groupName) => new()
    {
        DailyMinutes = dailyMinutes,
        PlayGames = playGames,
        WeeklyDaysTarget = weekly,
        Source = source,
        GroupName = groupName,
    };
}
