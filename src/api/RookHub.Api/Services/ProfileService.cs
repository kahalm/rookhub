using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class ProfileService
{
    private readonly AppDbContext _db;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(AppDbContext db, IBackgroundTaskQueue taskQueue, ILogger<ProfileService> logger)
    {
        _db = db;
        _taskQueue = taskQueue;
        _logger = logger;
    }

    public async Task<ProfileDto> GetProfileAsync(int userId)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        return MapToDto(user);
    }

    public async Task<ProfileDto> GetProfileByUsernameAsync(string username)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new KeyNotFoundException("User not found.");

        return MapToDto(user);
    }

    public async Task<ProfileDto> UpdateProfileAsync(int userId, UpdateProfileDto dto)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        var profile = user.Profile ?? new UserProfile { UserId = userId };
        if (user.Profile == null)
        {
            user.Profile = profile;
            _db.UserProfiles.Add(profile);
        }

        if (dto.FirstName != null) profile.FirstName = dto.FirstName;
        if (dto.LastName != null) profile.LastName = dto.LastName;
        if (dto.DisplayName != null) profile.DisplayName = dto.DisplayName;
        if (dto.FideId != null) profile.FideId = dto.FideId;
        if (dto.ChessResultsId != null) profile.ChessResultsId = dto.ChessResultsId;
        if (dto.ChessComUsername != null) profile.ChessComUsername = dto.ChessComUsername;
        if (dto.LichessUsername != null) profile.LichessUsername = dto.LichessUsername;

        await _db.SaveChangesAsync();

        // Trigger auto-subscription if ChessResultsId is set and LastName is available
        if (!string.IsNullOrWhiteSpace(profile.ChessResultsId) && !string.IsNullOrWhiteSpace(profile.LastName))
        {
            await _taskQueue.EnqueueAsync(async (sp, ct) =>
            {
                var db = sp.GetRequiredService<AppDbContext>();
                var proxy = sp.GetRequiredService<CrawlerProxyService>();
                var autoSub = sp.GetRequiredService<AutoSubscriptionService>();
                await autoSub.CheckUserAsync(db, proxy, userId, ct);
            });
        }

        return MapToDto(user);
    }

    private static ProfileDto MapToDto(AppUser user) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        FirstName = user.Profile?.FirstName,
        LastName = user.Profile?.LastName,
        DisplayName = user.Profile?.DisplayName,
        FideId = user.Profile?.FideId,
        ChessResultsId = user.Profile?.ChessResultsId,
        ChessComUsername = user.Profile?.ChessComUsername,
        LichessUsername = user.Profile?.LichessUsername
    };
}
