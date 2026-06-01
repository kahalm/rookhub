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

        // Identitäts-Felder VOR dem Überschreiben merken — die Auto-Subscription (Crawler-Call)
        // darf nur bei einer echten Identitätsänderung feuern, nicht bei jedem Profil-PUT
        // (z.B. reine Einstellungs-Saves wie Brett-Theme/Stockfish-Tiefe via PreferencesService).
        var oldChessResultsId = profile.ChessResultsId;
        var oldLastName = profile.LastName;
        var oldFirstName = profile.FirstName;
        var oldFideId = profile.FideId;

        if (dto.FirstName != null) profile.FirstName = dto.FirstName;
        if (dto.LastName != null) profile.LastName = dto.LastName;
        if (dto.DisplayName != null) profile.DisplayName = dto.DisplayName;
        if (dto.FideId != null) profile.FideId = dto.FideId;
        if (dto.ChessResultsId != null) profile.ChessResultsId = dto.ChessResultsId;
        if (dto.ChessComUsername != null) profile.ChessComUsername = dto.ChessComUsername;
        if (dto.LichessUsername != null) profile.LichessUsername = dto.LichessUsername;
        if (dto.BoardTheme != null) profile.BoardTheme = dto.BoardTheme;
        if (dto.PieceSet != null) profile.PieceSet = dto.PieceSet;
        if (dto.StockfishDepth != null) profile.StockfishDepth = Math.Clamp(dto.StockfishDepth.Value, 1, 24);
        if (dto.PuzzleDifficulty != null) profile.PuzzleDifficulty = dto.PuzzleDifficulty;
        if (dto.BookStockfishDepth != null) profile.BookStockfishDepth = Math.Clamp(dto.BookStockfishDepth.Value, 1, 24);

        await _db.SaveChangesAsync();

        // Trigger auto-subscription nur, wenn sich die Schach-Identität tatsächlich geändert hat
        // (ChessResultsId/LastName/FirstName/FideId) UND ChessResultsId + LastName gesetzt sind.
        // Verhindert, dass reine Einstellungs-Updates (Theme/Tiefe/Schwierigkeit) den Crawler
        // (`/api/players/tournaments`) wiederholt aufrufen.
        var identityChanged =
            !string.Equals(oldChessResultsId, profile.ChessResultsId, StringComparison.Ordinal) ||
            !string.Equals(oldLastName, profile.LastName, StringComparison.Ordinal) ||
            !string.Equals(oldFirstName, profile.FirstName, StringComparison.Ordinal) ||
            !string.Equals(oldFideId, profile.FideId, StringComparison.Ordinal);

        if (identityChanged
            && !string.IsNullOrWhiteSpace(profile.ChessResultsId)
            && !string.IsNullOrWhiteSpace(profile.LastName))
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
        LichessUsername = user.Profile?.LichessUsername,
        BoardTheme = user.Profile?.BoardTheme,
        PieceSet = user.Profile?.PieceSet,
        StockfishDepth = user.Profile?.StockfishDepth,
        PuzzleDifficulty = user.Profile?.PuzzleDifficulty,
        BookStockfishDepth = user.Profile?.BookStockfishDepth
    };
}
