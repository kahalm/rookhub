using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class ProfileService
{
    private readonly AppDbContext _db;

    public ProfileService(AppDbContext db) => _db = db;

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

        if (dto.DisplayName != null) profile.DisplayName = dto.DisplayName;
        if (dto.FideId != null) profile.FideId = dto.FideId;
        if (dto.ChessResultsId != null) profile.ChessResultsId = dto.ChessResultsId;
        if (dto.ChessComUsername != null) profile.ChessComUsername = dto.ChessComUsername;
        if (dto.LichessUsername != null) profile.LichessUsername = dto.LichessUsername;

        await _db.SaveChangesAsync();
        return MapToDto(user);
    }

    private static ProfileDto MapToDto(AppUser user) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        DisplayName = user.Profile?.DisplayName,
        FideId = user.Profile?.FideId,
        ChessResultsId = user.Profile?.ChessResultsId,
        ChessComUsername = user.Profile?.ChessComUsername,
        LichessUsername = user.Profile?.LichessUsername
    };
}
