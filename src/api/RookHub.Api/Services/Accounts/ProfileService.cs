using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts.Erasure;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Tournaments;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Accounts;

namespace RookHub.Api.Services.Accounts;

public class ProfileService
{
    private readonly AppDbContext _db;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<ProfileService> _logger;
    private readonly BookAdminService _bookAdmin;
    private readonly IEmailSender _email;
    private readonly DiscordLinkService _discordLink;

    // bookAdmin verpflichtend (siehe CourseService): sonst baut der Dienst an der DI vorbei.
    // email: Hinweis an die BISHERIGE Adresse, wenn der Reset-Anker wechselt.
    // discordLink: vermerkt eine frei gewordene Discord-ID samt bisherigem Konto (A1-011, Tabelle DiscordLinkReleases).
    public ProfileService(AppDbContext db, IBackgroundTaskQueue taskQueue, ILogger<ProfileService> logger, BookAdminService bookAdmin,
        IEmailSender email, DiscordLinkService discordLink)
    {
        _db = db;
        _taskQueue = taskQueue;
        _logger = logger;
        _bookAdmin = bookAdmin;
        _email = email;
        _discordLink = discordLink;
    }

    public async Task<ProfileDto> GetProfileAsync(int userId)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        return MapToDto(user);
    }

    /// <param name="allowEmailChange">false im Impersonations-Kontext: die E-Mail ist der
    /// Reset-Anker, ihre Änderung wäre eine dauerhafte Kontoübernahme. Ein UNVERÄNDERTER Wert
    /// darf trotzdem durch — die UI schickt das Feld bei JEDEM Speichern mit, ein pauschales
    /// Ablehnen würde dem Support auch die Korrektur von Anzeigename/Schach-IDs verbauen.</param>
    public async Task<ProfileDto> UpdateProfileAsync(int userId, UpdateProfileDto dto, bool allowEmailChange = true)
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
        var oldLichessUsername = profile.LichessUsername;
        var oldChessComUsername = profile.ChessComUsername;

        // E-Mail: null = unverändert lassen; "" = entfernen; sonst validieren + auf Dublette prüfen.
        // Normalisierung (trim + lowercase) wie bei der Registrierung, damit der Unique-Index greift.
        string? previousEmail = null;
        var emailAnchorChanged = false;
        if (dto.Email != null)
        {
            var normalizedEmail = string.IsNullOrWhiteSpace(dto.Email)
                ? null
                : dto.Email.Trim().ToLowerInvariant();

            if (normalizedEmail != null && !new EmailAddressAttribute().IsValid(normalizedEmail))
                throw new ArgumentException("Email is not a valid email address.");

            if (!allowEmailChange && !string.Equals(normalizedEmail, user.Email, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Email cannot be changed while impersonating another user.");

            // Der Reset-Anker wechselt nur gegen das aktuelle Passwort — wie Passwort ändern und
            // Konto löschen. Sonst genügte eine kurz erbeutete Sitzung (XSS, fremdes Gerät): eigene
            // Adresse eintragen, „Passwort vergessen", und das Opfer ist dauerhaft ausgesperrt.
            // VOR der Dublettenprüfung, damit die Sitzung allein keine fremden Adressen abfragen kann.
            // Verglichen wird normalisiert: eine Alt-Adresse in anderer Schreibweise, die die UI
            // unverändert zurückschickt, ist KEIN Wechsel (sonst scheiterte jedes Profil-Speichern).
            emailAnchorChanged = !string.Equals(normalizedEmail, user.Email?.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            if (emailAnchorChanged)
            {
                if (string.IsNullOrEmpty(dto.CurrentPassword))
                    throw new UnauthorizedAccessException("Current password is required to change the email address.");
                if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                    throw new UnauthorizedAccessException("Current password is incorrect.");
            }

            // Auch nicht der Benutzername eines ANDEREN Kontos: der Login gibt dem Benutzernamen Vorrang,
            // die E-Mail-Anmeldung landete sonst immer dort (Codereview A1-010). Nur bei einem echten
            // Wechsel: die UI schickt die E-Mail bei jedem Speichern mit, eine Bestandskollision darf
            // das Speichern von Vorname/FIDE-ID usw. nicht blockieren (das Opfer wäre ausgesperrt).
            if (normalizedEmail != null && await _db.AppUsers
                    .AnyAsync(u => u.Id != userId && (u.Email == normalizedEmail
                        || (emailAnchorChanged && u.Username.ToLower() == normalizedEmail))))
                throw new InvalidOperationException("This email address is already in use.");

            previousEmail = user.Email;
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

        await ResetPlayTimeOnUsernameChangeAsync(userId, PlayTimeService.Lichess, oldLichessUsername, profile.LichessUsername);
        await ResetPlayTimeOnUsernameChangeAsync(userId, PlayTimeService.ChessCom, oldChessComUsername, profile.ChessComUsername);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (dto.Email != null && AuthService.IsUniqueViolation(ex))
        {
            // TOCTOU: zwei Konten setzen (fast) gleichzeitig dieselbe E-Mail → die Vorab-Prüfung
            // passiert beide, der zweite Insert verletzt den Unique-Index auf Email. Sauber als
            // Kollision (409) statt 500 behandeln. Nur bei echtem Duplikat — ein transienter
            // DB-Fehler würde sonst fälschlich „E-Mail vergeben" melden.
            throw new InvalidOperationException("This email address is already in use.");
        }

        if (emailAnchorChanged)
            await NotifyPreviousEmailAsync(user, previousEmail);

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

    /// <summary>Die Partienzählung fürs Wochenziel folgt dem VERKNÜPFTEN Konto: Cursor und Tageszählungen
    /// liegen je (User, Plattform), nicht je Benutzername. Wechselt der Name (Tippfehler korrigiert, anderes
    /// Konto), fragte der nächste Sync das neue Konto erst ab der letzten Partie des alten ab und dessen
    /// Partien blieben gezählt (Codereview F5-021). Deshalb hier die Tageszählungen der Plattform verwerfen
    /// und den Cursor auf 0 setzen — der nächste Sync baut sie über das Erst-Lookback neu auf.
    /// <see cref="PlayTimeSync.LastSyncedAt"/> bleibt, damit ein Namenswechsel die Abruf-Sperre nicht umgeht.
    /// Groß-/Kleinschreibung zählt nicht (Lichess und chess.com unterscheiden sie nicht). Wird im selben
    /// SaveChanges wie die Profiländerung geschrieben.</summary>
    private async Task ResetPlayTimeOnUsernameChangeAsync(int userId, string platform, string? oldName, string? newName)
    {
        if (string.Equals((oldName ?? "").Trim(), (newName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return;
        _db.PlayTimeDailies.RemoveRange(
            await _db.PlayTimeDailies.Where(p => p.UserId == userId && p.Platform == platform).ToListAsync());
        var sync = await _db.PlayTimeSyncs.FirstOrDefaultAsync(s => s.UserId == userId && s.Platform == platform);
        if (sync != null)
        {
            sync.LastGameTimestamp = 0;
            sync.LastError = null;
        }
    }

    /// <summary>
    /// Hinweis an die BISHERIGE Adresse, dass der Reset-Anker gewechselt hat — sonst erführe das
    /// Opfer einer Übernahme nichts davon, alle weiteren Mails gehen ja an die neue Adresse.
    /// Best-effort: die Änderung ist gespeichert, ein Mail-Fehler wird nur geloggt.
    /// </summary>
    private async Task NotifyPreviousEmailAsync(AppUser user, string? previousEmail)
    {
        _logger.LogInformation("Profile: email address changed for user {UserId}", user.Id);
        if (string.IsNullOrWhiteSpace(previousEmail)) return;

        var (subject, html, text) = BuildEmailChangedNotice(user.Username, removed: user.Email == null);
        try
        {
            await _email.SendAsync(previousEmail, subject, html, text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile: notice about the email change could not be sent for user {UserId}", user.Id);
        }
    }

    private static (string subject, string html, string text) BuildEmailChangedNotice(string username, bool removed)
    {
        const string subject = "RookHub — E-Mail-Adresse geändert";
        var what = removed
            ? "die E-Mail-Adresse deines RookHub-Kontos wurde soeben entfernt."
            : "die E-Mail-Adresse deines RookHub-Kontos wurde soeben geändert. Links zum Zurücksetzen des Passworts gehen ab jetzt an die neue Adresse.";
        const string notYou =
            "Warst du das nicht, ändere sofort dein Passwort und wende dich an einen RookHub-Admin.";
        var text =
            $"Hallo {username},\n\n" +
            $"{what}\n" +
            "Diese Nachricht geht an die bisherige Adresse, damit du davon erfährst.\n\n" +
            $"{notYou}\n\n" +
            "— RookHub";
        var html =
            $"<p>Hallo {System.Net.WebUtility.HtmlEncode(username)},</p>" +
            $"<p>{what} Diese Nachricht geht an die bisherige Adresse, damit du davon erfährst.</p>" +
            $"<p><strong>{notYou}</strong></p>" +
            "<p>— RookHub</p>";
        return (subject, html, text);
    }

    /// <summary>
    /// Verknüpft das (bereits verifizierte) Discord-Konto mit dem User.
    /// Wirft <see cref="InvalidOperationException"/>, wenn die Discord-ID bereits an einen
    /// anderen RookHub-User gebunden ist (Controller → 409). Ersetzt sie eine ANDERE bisherige ID,
    /// wird diese frei (<see cref="DiscordLinkService.MarkReleasedAsync"/>).
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

        var previousDiscordId = profile.DiscordId;
        profile.DiscordId = discordId;
        profile.DiscordUsername = discordUsername;
        // Vermerk im selben Save wie der Wechsel: scheitert er (409), entsteht auch kein Vermerk.
        if (previousDiscordId != null && previousDiscordId != discordId)
            await _discordLink.MarkReleasedAsync(previousDiscordId, userId);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            // TOCTOU: zwei Accounts verknüpfen (fast) gleichzeitig dieselbe Discord-ID → die
            // Vorab-Prüfung passiert beide, der zweite Insert verletzt den Unique-Index auf
            // DiscordId. Sauber als Kollision (409) statt 500 behandeln — nur bei echtem
            // Duplikat, sonst hieße ein Timeout fälschlich „schon verknüpft".
            throw new InvalidOperationException("This Discord account is already linked to another RookHub user.");
        }
        _logger.LogInformation("Linked Discord account {DiscordId} to user {UserId}.", discordId, userId);
        return MapToDto(user);
    }

    /// <summary>Hebt die Discord-Verknüpfung des Users auf (idempotent). Die ID wird frei: ihre bis jetzt
    /// ausgestellten Bot-Tokens löst danach nur noch dieser User ein (<see cref="DiscordLinkService.MarkReleasedAsync"/>).</summary>
    public async Task<ProfileDto> UnlinkDiscordAsync(int userId)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.Profile != null && (user.Profile.DiscordId != null || user.Profile.DiscordUsername != null))
        {
            var releasedDiscordId = user.Profile.DiscordId;
            user.Profile.DiscordId = null;
            user.Profile.DiscordUsername = null;
            if (releasedDiscordId != null) await _discordLink.MarkReleasedAsync(releasedDiscordId, userId);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Unlinked Discord account from user {UserId}.", userId);
        }

        return MapToDto(user);
    }

    /// <summary>
    /// Löscht den Account DSGVO-konform: Identität + PII werden anonymisiert (AppUser in-place,
    /// Login dauerhaft gesperrt), persönliche Inhalte/Verknüpfungen entfernt, die Solve-Statistik
    /// bleibt anonym (unter der UserId) erhalten. Mit entfernt werden auch die PERSÖNLICHEN Bücher
    /// (<see cref="Models.Book.OwnerUserId"/>, inkl. aller abhängigen Kursdaten über den bestehenden
    /// <see cref="BookAdminService.DeleteBookAsync"/>-Löschpfad) sowie die Admin-Direktnachrichten
    /// (<c>AdminMessages</c> + <c>MessageThreads</c> — Freitexte des Users sind PII).
    /// Verlangt das korrekte Passwort.
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

        await EraseAsync(user);
    }

    /// <summary>
    /// Derselbe Löschkern wie <see cref="DeleteAccountAsync"/>, aber OHNE Passwortprüfung — für die
    /// Admin-Löschung (<see cref="AdminService.DeleteUserAsync"/>). Vorher löschte der Admin die Nutzerzeile
    /// hart und verließ sich auf die FK-Kaskaden: Spalten ohne FK (Verteiler der Kalk-Serie, persönliche
    /// Bücher, Vereins-Entwürfe, Katalog-Freigaben …) blieben als Waisen stehen, Restrict-FKs (Freigaben,
    /// Challenges) ließen die Löschung mit 409 scheitern. Idempotent wie die Selbstlöschung.
    /// </summary>
    /// <exception cref="KeyNotFoundException">User existiert nicht.</exception>
    public async Task EraseUserAsync(int userId)
    {
        var user = await _db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.DeletedAt != null)
            return; // bereits gelöscht -> idempotent

        await EraseAsync(user);
    }

    /// <summary>Der eigentliche Löschkern (Aufrufer haben Existenz, Idempotenz und ggf. das Passwort geprüft).</summary>
    private async Task EraseAsync(AppUser user)
    {
        // Die Löschpflicht je Bereich in fester Reihenfolge, dann die Anonymisierung (Zerlegung Z15).
        await new UserDataEraser(_db, _bookAdmin, _discordLink).EraseAsync(user);
        _logger.LogInformation("AccountDeleted: user {UserId} anonymized (stats retained).", user.Id);
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
