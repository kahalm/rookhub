using System.ComponentModel.DataAnnotations;
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

    /// <summary>Öffentliche (reduzierte) Profil-Sicht — ohne PII/Discord/Einstellungen.</summary>
    public async Task<PublicProfileDto> GetPublicProfileByUsernameAsync(string username)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new KeyNotFoundException("User not found.");

        return new PublicProfileDto
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.Profile?.DisplayName,
            FideId = user.Profile?.FideId,
            ChessComUsername = user.Profile?.ChessComUsername,
            LichessUsername = user.Profile?.LichessUsername,
        };
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

        // E-Mail: null = unverändert lassen; "" = entfernen; sonst validieren + auf Dublette prüfen.
        // Normalisierung (trim + lowercase) wie bei der Registrierung, damit der Unique-Index greift.
        if (dto.Email != null)
        {
            var normalizedEmail = string.IsNullOrWhiteSpace(dto.Email)
                ? null
                : dto.Email.Trim().ToLowerInvariant();

            if (normalizedEmail != null && !new EmailAddressAttribute().IsValid(normalizedEmail))
                throw new ArgumentException("Email is not a valid email address.");

            if (normalizedEmail != null && await _db.AppUsers
                    .AnyAsync(u => u.Id != userId && u.Email == normalizedEmail))
                throw new InvalidOperationException("This email address is already in use.");

            user.Email = normalizedEmail;
        }

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

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (dto.Email != null)
        {
            // TOCTOU: zwei Konten setzen (fast) gleichzeitig dieselbe E-Mail → die Vorab-Prüfung
            // passiert beide, der zweite Insert verletzt den Unique-Index auf Email. Sauber als
            // Kollision (409) statt 500 behandeln.
            throw new InvalidOperationException("This email address is already in use.");
        }

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

    /// <summary>
    /// Verknüpft das (bereits verifizierte) Discord-Konto mit dem User.
    /// Wirft <see cref="InvalidOperationException"/>, wenn die Discord-ID bereits an einen
    /// anderen RookHub-User gebunden ist (Controller → 409).
    /// </summary>
    public async Task<ProfileDto> LinkDiscordAsync(int userId, string discordId, string? discordUsername)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        // Kollision: gehört die Discord-ID schon einem anderen User?
        var ownerId = await _db.UserProfiles
            .Where(p => p.DiscordId == discordId)
            .Select(p => (int?)p.UserId)
            .FirstOrDefaultAsync();
        if (ownerId != null && ownerId != userId)
            throw new InvalidOperationException("This Discord account is already linked to another RookHub user.");

        var profile = user.Profile ?? new UserProfile { UserId = userId };
        if (user.Profile == null)
        {
            user.Profile = profile;
            _db.UserProfiles.Add(profile);
        }

        profile.DiscordId = discordId;
        profile.DiscordUsername = discordUsername;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // TOCTOU: zwei Accounts verknüpfen (fast) gleichzeitig dieselbe Discord-ID → die
            // Vorab-Prüfung passiert beide, der zweite Insert verletzt den Unique-Index auf
            // DiscordId. Sauber als Kollision (409) statt 500 behandeln.
            throw new InvalidOperationException("This Discord account is already linked to another RookHub user.");
        }
        _logger.LogInformation("Linked Discord account {DiscordId} to user {UserId}.", discordId, userId);
        return MapToDto(user);
    }

    /// <summary>Hebt die Discord-Verknüpfung des Users auf (idempotent).</summary>
    public async Task<ProfileDto> UnlinkDiscordAsync(int userId)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.Profile != null && (user.Profile.DiscordId != null || user.Profile.DiscordUsername != null))
        {
            user.Profile.DiscordId = null;
            user.Profile.DiscordUsername = null;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Unlinked Discord account from user {UserId}.", userId);
        }

        return MapToDto(user);
    }

    /// <summary>
    /// Löscht den Account DSGVO-konform: Identität + PII werden anonymisiert (AppUser in-place,
    /// Login dauerhaft gesperrt), persönliche Inhalte/Verknüpfungen entfernt, die Solve-Statistik
    /// bleibt anonym (unter der UserId) erhalten. Verlangt das korrekte Passwort.
    /// </summary>
    /// <exception cref="KeyNotFoundException">User existiert nicht.</exception>
    /// <exception cref="UnauthorizedAccessException">Passwort falsch.</exception>
    public async Task DeleteAccountAsync(int userId, string password)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.DeletedAt != null)
            return; // bereits gelöscht -> idempotent

        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("Password is incorrect.");

        // 1) Persönliche Inhalte & Verknüpfungen hart entfernen (keine Statistik):
        //    Freundschaften (FK Restrict -> müssen explizit weg), Repertoires (cascadet Dateien),
        //    Turnier-Abos/-Favoriten/-Einstellungen, Gruppen-Mitgliedschaften, API-Tokens,
        //    Chessable-Bearer + Reset-Tokens, öffentliche Share-Inhalte, gemerkte Stellungen.
        _db.Friendships.RemoveRange(
            await _db.Friendships.Where(f => f.RequesterId == userId || f.AddresseeId == userId).ToListAsync());
        _db.Repertoires.RemoveRange(await _db.Repertoires.Where(r => r.UserId == userId).ToListAsync());
        _db.TournamentSubscriptions.RemoveRange(await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync());
        _db.TournamentFavorites.RemoveRange(await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync());
        _db.TournamentUserSettings.RemoveRange(await _db.TournamentUserSettings.Where(s => s.UserId == userId).ToListAsync());
        _db.UserGroups.RemoveRange(await _db.UserGroups.Where(g => g.UserId == userId).ToListAsync());
        // API-Tokens (chess.com-Extension u. a.) widerrufen — ein gelöschtes Konto behält keinen Zugang.
        _db.UserApiTokens.RemoveRange(await _db.UserApiTokens.Where(t => t.UserId == userId).ToListAsync());
        // Live-Drittanbieter-Credential + Einmal-Tokens: dürfen nach der Löschung nicht fortbestehen
        // (der Chessable-Bearer bliebe sonst mit dem Server-Key entschlüsselbar).
        _db.ChessableCredentials.RemoveRange(await _db.ChessableCredentials.Where(c => c.UserId == userId).ToListAsync());
        _db.PasswordResetTokens.RemoveRange(await _db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync());
        // Öffentlich abrufbare Inhalte mit Klarnamen/Fremddaten: geteilte Partien (/g/{token}) und
        // geteilte Linien (/l/{token}) — die Share-Links müssen mit dem Konto verschwinden.
        _db.SavedGames.RemoveRange(await _db.SavedGames.Where(g => g.UserId == userId).ToListAsync());
        _db.SharedLines.RemoveRange(await _db.SharedLines.Where(l => l.OwnerUserId == userId).ToListAsync());
        // Auf chessable.com gemerkte Stellungen (Kursname/Quell-URL) — persönlich, keine Statistik.
        _db.RememberedPositions.RemoveRange(await _db.RememberedPositions.Where(r => r.UserId == userId).ToListAsync());
        // Manuelle Aktivitäten bleiben als (anonyme) Trainingsstatistik, aber die Freitext-Notiz (PII) wird geleert.
        var manualWithNote = await _db.ManualActivities.Where(a => a.UserId == userId && a.Note != null).ToListAsync();
        foreach (var a in manualWithNote) a.Note = null;

        // 2) Identität anonymisieren (in-place) -> nicht re-identifizierbar, Login gesperrt.
        user.Username = $"deleted_{userId}";
        user.Email = $"deleted_{userId}@deleted.invalid";
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());
        user.IsAdmin = false;
        user.DeletedAt = DateTime.UtcNow;

        // 3) Profil-PII entfernen (Statistik-Tabellen referenzieren weiterhin die UserId).
        if (user.Profile is { } p)
        {
            p.FirstName = p.LastName = p.DisplayName = null;
            p.FideId = p.ChessResultsId = p.ChessComUsername = p.LichessUsername = null;
            p.DiscordId = p.DiscordUsername = null;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("AccountDeleted: user {UserId} anonymized (stats retained).", userId);
    }

    private static ProfileDto MapToDto(AppUser user) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        Email = user.Email,
        FirstName = user.Profile?.FirstName,
        LastName = user.Profile?.LastName,
        DisplayName = user.Profile?.DisplayName,
        FideId = user.Profile?.FideId,
        ChessResultsId = user.Profile?.ChessResultsId,
        ChessComUsername = user.Profile?.ChessComUsername,
        LichessUsername = user.Profile?.LichessUsername,
        DiscordId = user.Profile?.DiscordId,
        DiscordUsername = user.Profile?.DiscordUsername,
        BoardTheme = user.Profile?.BoardTheme,
        PieceSet = user.Profile?.PieceSet,
        StockfishDepth = user.Profile?.StockfishDepth,
        PuzzleDifficulty = user.Profile?.PuzzleDifficulty,
        BookStockfishDepth = user.Profile?.BookStockfishDepth
    };
}
