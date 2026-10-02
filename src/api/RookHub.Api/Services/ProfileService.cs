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
    private readonly BookAdminService _bookAdmin;
    private readonly IEmailSender _email;

    // bookAdmin verpflichtend (siehe CourseService): sonst baut der Dienst an der DI vorbei.
    // email: Hinweis an die BISHERIGE Adresse, wenn der Reset-Anker wechselt.
    public ProfileService(AppDbContext db, IBackgroundTaskQueue taskQueue, ILogger<ProfileService> logger, BookAdminService bookAdmin,
        IEmailSender email)
    {
        _db = db;
        _taskQueue = taskQueue;
        _logger = logger;
        _bookAdmin = bookAdmin;
        _email = email;
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
        var userId = user.Id;

        // 0) Persönliche Bücher (importierte/erstellte Kurse) samt Abhängigen über den bestehenden
        //    Admin-Löschpfad entfernen — der räumt Puzzles, Fortschritte, Freigaben, Links etc.
        //    konsistent ab (jeder DeleteBookAsync speichert für sich; ein Wiederholungslauf nach
        //    einem Teil-Fehlschlag ist dank Idempotenz der Restschritte unkritisch).
        var ownedBookIds = await _db.Books.Where(b => b.OwnerUserId == userId).Select(b => b.Id).ToListAsync();
        foreach (var bookId in ownedBookIds)
            await _bookAdmin.DeleteBookAsync(bookId);

        // 1) Persönliche Inhalte & Verknüpfungen hart entfernen (keine Statistik):
        //    Freundschaften (FK Restrict -> müssen explizit weg), Repertoires (cascadet Dateien),
        //    Turnier-Abos/-Favoriten/-Einstellungen, Gruppen-Mitgliedschaften, API-Tokens,
        //    Chessable-Bearer + Reset-Tokens, öffentliche Share-Inhalte, gemerkte Stellungen,
        //    Admin-Direktnachrichten (Freitext = PII) samt Thread-Metadaten.
        _db.Friendships.RemoveRange(
            await _db.Friendships.Where(f => f.RequesterId == userId || f.AddresseeId == userId).ToListAsync());
        _db.Repertoires.RemoveRange(await _db.Repertoires.Where(r => r.UserId == userId).ToListAsync());
        _db.TournamentSubscriptions.RemoveRange(await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync());
        _db.TournamentFavorites.RemoveRange(await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync());
        _db.TournamentFavoriteDismissals.RemoveRange(await _db.TournamentFavoriteDismissals.Where(d => d.UserId == userId).ToListAsync());
        _db.TournamentUserSettings.RemoveRange(await _db.TournamentUserSettings.Where(s => s.UserId == userId).ToListAsync());
        // Turnier-Suchprofile: sie tragen die Koordinaten des Wohnorts und benannte Zweitorte
        // ("Ferienhaus Kaernten") — das ist persoenlicher als jedes Turnier-Abo.
        _db.TournamentSearchProfiles.RemoveRange(await _db.TournamentSearchProfiles.Where(p => p.UserId == userId).ToListAsync());
        _db.TournamentDirectoryIgnores.RemoveRange(await _db.TournamentDirectoryIgnores.Where(i => i.UserId == userId).ToListAsync());
        // Wen jemand verfolgt hat, ist eine Auskunft ueber ihn — und ueber die Verfolgten. Der
        // geholte Verlauf bleibt (der gehoert dem Spieler, nicht dem Konto), die Liste geht.
        _db.TrackedPlayers.RemoveRange(await _db.TrackedPlayers.Where(t => t.UserId == userId).ToListAsync());
        _db.UserViewStates.RemoveRange(await _db.UserViewStates.Where(v => v.UserId == userId).ToListAsync());
        // Vereins-Datenbank (LeagueHub): die Partien bleiben (sie gehören zur Liga, nicht zum Konto), nur der Vermerk,
        // wer sie hochgeladen hat, geht — bei anonymisierten gibt es ihn ohnehin nicht.
        foreach (var g in await _db.LeagueClubGames.Where(g => g.UploadedByUserId == userId).ToListAsync())
            g.UploadedByUserId = null;
        // Offene Entwürfe von PGN-Importen (0.595.0) gehen ganz — sie tragen den Rohtext samt Klarnamen.
        _db.LeagueClubDrafts.RemoveRange(await _db.LeagueClubDrafts.Where(d => d.UserId == userId).ToListAsync());
        // ClubHub: die Verknüpfung mit dem Karteiblatt lösen (das Blatt gehört dem Verein und bleibt) und Trainer-
        // Zuteilungen entfernen — das Konto wird IN PLACE anonymisiert, weder SetNull noch Cascade feuern.
        foreach (var m in await _db.ClubMembers.Where(m => m.LinkedUserId == userId).ToListAsync())
            m.LinkedUserId = null;
        _db.ClubGroupTrainers.RemoveRange(await _db.ClubGroupTrainers.Where(t => t.UserId == userId).ToListAsync());
        // KidHub-Fortschritt: Spielstand des Kindes, keine Statistik fuer andere — geht mit dem Konto.
        _db.KidsLevelProgresses.RemoveRange(await _db.KidsLevelProgresses.Where(p => p.UserId == userId).ToListAsync());
        _db.KidsCourseProgresses.RemoveRange(await _db.KidsCourseProgresses.Where(p => p.UserId == userId).ToListAsync());
        _db.KidsCourseLines.RemoveRange(await _db.KidsCourseLines.Where(l => l.UserId == userId).ToListAsync());
        // Aufgabenblätter: Überschriften und Begleittexte sind FREITEXT des Nutzers (und oft
        // über seine Schüler geschrieben) — die gehen mit; die Aufgaben cascaden am Blatt.
        _db.GameMistakeProgresses.RemoveRange(await _db.GameMistakeProgresses.Where(m => m.UserId == userId).ToListAsync());
        _db.Worksheets.RemoveRange(await _db.Worksheets.Where(w => w.UserId == userId).ToListAsync());
        _db.UserGroups.RemoveRange(await _db.UserGroups.Where(g => g.UserId == userId).ToListAsync());
        // API-Tokens (chess.com-Extension u. a.) widerrufen — ein gelöschtes Konto behält keinen Zugang.
        _db.UserApiTokens.RemoveRange(await _db.UserApiTokens.Where(t => t.UserId == userId).ToListAsync());
        // Live-Drittanbieter-Credential + Einmal-Tokens: dürfen nach der Löschung nicht fortbestehen
        // (der Chessable-Bearer bliebe sonst mit dem Server-Key entschlüsselbar).
        _db.ChessableCredentials.RemoveRange(await _db.ChessableCredentials.Where(c => c.UserId == userId).ToListAsync());
        // Ebenso der Lichess-OAuth-Token der External-Engine-Anbindung: Die Konto-Löschung
        // anonymisiert die AppUser-Zeile IN PLACE, der Cascade-FK feuert hier also NICHT — ohne
        // diese Zeile bliebe ein fremder, weiterhin gültiger Lichess-Token dauerhaft in der DB.
        _db.LichessEngineCredentials.RemoveRange(await _db.LichessEngineCredentials.Where(c => c.UserId == userId).ToListAsync());
        // Engines „RookHub direkt": ihr Selector nimmt sonst weiter Arbeit an (der Provider pollt ja weiter),
        // und die Zeile trüge den Namen der Maschine eines gelöschten Kontos.
        _db.ExternalEngineRegistrations.RemoveRange(await _db.ExternalEngineRegistrations.Where(r => r.UserId == userId).ToListAsync());
        _db.PasswordResetTokens.RemoveRange(await _db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync());
        // Ein noch offener Uebergabe-Code wuerde sonst nach der Loeschung noch Sekunden lang
        // eine Anmeldung erzeugen (der Einloeser prueft zwar DeletedAt — die Zeile hat hier aber
        // ohnehin nichts mehr verloren).
        _db.AuthHandoffTokens.RemoveRange(await _db.AuthHandoffTokens.Where(t => t.UserId == userId).ToListAsync());
        // Öffentlich abrufbare Inhalte mit Klarnamen/Fremddaten: geteilte Partien (/g/{token}) und
        // geteilte Linien (/l/{token}) — die Share-Links müssen mit dem Konto verschwinden.
        // Formular-Fotos zuerst (sie hängen an den Partien, tragen aber auch Namen und Handschrift — und eine
        // gescheiterte Einlesung hat gar keine Partie). Ohne das Foto zu laden: es ist das Schwergewicht der Zeile.
        var scanKeys = await ScoresheetScanService.KeysAsync(_db.ScoresheetScans.Where(s => s.UserId == userId));
        ScoresheetScanService.RemovePagesWithoutLoading(_db,
            await ScoresheetScanService.PageKeysAsync(_db, scanKeys.Select(k => k.Id).ToList()));
        ScoresheetScanService.RemoveWithoutLoading(_db, scanKeys);
        _db.SavedGames.RemoveRange(await _db.SavedGames.Where(g => g.UserId == userId).ToListAsync());
        // „Partie rekonstruieren": erst die Teile, dann die Kopfzeilen — die Löschung anonymisiert
        // nur die Nutzerzeile, es feuert also kein Cascade-FK (und InMemory cascadet ohnehin nicht).
        var reconstructionIds = await _db.GameReconstructions.Where(r => r.UserId == userId).Select(r => r.Id).ToListAsync();
        if (reconstructionIds.Count > 0)
        {
            _db.GameReconstructionParts.RemoveRange(
                await _db.GameReconstructionParts.Where(p => reconstructionIds.Contains(p.GameReconstructionId)).ToListAsync());
            _db.GameReconstructions.RemoveRange(
                await _db.GameReconstructions.Where(r => r.UserId == userId).ToListAsync());
        }
        _db.SharedLines.RemoveRange(await _db.SharedLines.Where(l => l.OwnerUserId == userId).ToListAsync());
        // Auf chessable.com gemerkte Stellungen (Kursname/Quell-URL) — persönlich, keine Statistik.
        _db.RememberedPositions.RemoveRange(await _db.RememberedPositions.Where(r => r.UserId == userId).ToListAsync());
        // Admin-Direktnachrichten: die Nachrichtentexte des Users sind PII und müssen mit dem Konto
        // verschwinden (beide Richtungen des Threads — der Kontext der Admin-Antworten identifiziert
        // den User genauso). Thread-Metadaten (Claim) hängen daran und gehen mit.
        _db.AdminMessages.RemoveRange(await _db.AdminMessages.Where(m => m.UserId == userId).ToListAsync());
        _db.MessageThreads.RemoveRange(await _db.MessageThreads.Where(t => t.UserId == userId).ToListAsync());
        // Web-Push: Endpunkt + Schlüssel des GERÄTS. Ohne diese Zeilen schickt der Server weiter
        // Benachrichtigungen an das Gerät eines gelöschten Kontos — der Verteiler der Kalkulations-Serie
        // sammelt die Nutzer-Id ja weiter ein, solange die Mitgliedschaft steht (deshalb auch die).
        _db.UserPushSubscriptions.RemoveRange(await _db.UserPushSubscriptions.Where(x => x.UserId == userId).ToListAsync());
        _db.NotificationPushSettings.RemoveRange(await _db.NotificationPushSettings.Where(x => x.UserId == userId).ToListAsync());
        _db.CalcSeriesMembers.RemoveRange(await _db.CalcSeriesMembers.Where(x => x.UserId == userId).ToListAsync());
        // In-App-Benachrichtigungen tragen Freitext-Daten (Kurs-/Absendernamen) → PII.
        _db.Notifications.RemoveRange(await _db.Notifications.Where(x => x.UserId == userId).ToListAsync());
        // Rollen/Rechte: ein gelöschtes Konto behält keine Berechtigung (der Login ist gesperrt, aber
        // die Zeile wäre bei einer Wiederverwendung der Id ein stiller Rechte-Übertrag).
        _db.UserRoles.RemoveRange(await _db.UserRoles.Where(x => x.UserId == userId).ToListAsync());
        // Hintergrund-Analyseaufträge tragen Stellungen samt selbst gewählten Titeln (PII-nah) und
        // würden nach der Löschung weiter Rechenzeit auf der Engine des Kontos verbrauchen.
        _db.AnalysisJobs.RemoveRange(await _db.AnalysisJobs.Where(x => x.UserId == userId).ToListAsync());
        // Analyse-Verlauf (0.603.0): Stellungen und Zugfolgen des Nutzers.
        _db.AnalysisHistoryEntries.RemoveRange(await _db.AnalysisHistoryEntries.Where(h => h.UserId == userId).ToListAsync());
        // Zugvergleiche (0.602.0): die Zeilen cascaden am Kopf; ihre Aufträge stehen in AnalysisJobs und gehen oben mit.
        // Unter InMemory cascadet nichts — die Zeilen deshalb ausdrücklich.
        var comparisons = await _db.MoveComparisons.Include(c => c.Lines).Where(c => c.UserId == userId).ToListAsync();
        _db.MoveComparisonLines.RemoveRange(comparisons.SelectMany(c => c.Lines));
        _db.MoveComparisons.RemoveRange(comparisons);
        // Chessable-Rohdaten des Nutzers: getReview-Linien, Sitzungszüge und „schwierige Züge" sind
        // fremder Kursinhalt, an die Person gebunden — keine Statistik, die anonym weiterleben könnte.
        // Ohne sie zu laden: LONGTEXT-JSON bis zum Kontingent je Konto (Altbestand auch weit mehr), das
        // RemoveRange(ToListAsync()) holte alles in die API, nur um es zu löschen. Relational ein DELETE je
        // Tabelle — läuft sofort wie DeleteBookAsync oben, ein Wiederholungslauf findet danach nichts mehr;
        // InMemory (Tests) kennt kein ExecuteDelete.
        if (_db.Database.IsRelational())
        {
            await _db.ChessableReviewLines.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _db.ChessableSessionMoves.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _db.ChessableProblemMoves.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        }
        else
        {
            _db.ChessableReviewLines.RemoveRange(await _db.ChessableReviewLines.Where(x => x.UserId == userId).ToListAsync());
            _db.ChessableSessionMoves.RemoveRange(await _db.ChessableSessionMoves.Where(x => x.UserId == userId).ToListAsync());
            _db.ChessableProblemMoves.RemoveRange(await _db.ChessableProblemMoves.Where(x => x.UserId == userId).ToListAsync());
        }
        // Eigene Analysebäume des Kalkulations-Modus: Nutzerarbeit mit Freitext, kein Aggregat.
        _db.CalculationTrees.RemoveRange(await _db.CalculationTrees.Where(x => x.UserId == userId).ToListAsync());
        // Punktepartie-Durchläufe samt geratenen Zügen: reine Nutzerarbeit an konkreten Partien.
        // MUSS vor den Partie-Analysen stehen — die Sitzung zeigt per Restrict auf die Analyse.
        var guessSessions = await _db.GuessSessions.Where(g => g.UserId == userId).Select(g => g.Id).ToListAsync();
        if (guessSessions.Count > 0)
        {
            _db.GuessMoves.RemoveRange(
                await _db.GuessMoves.Where(m => guessSessions.Contains(m.GuessSessionId)).ToListAsync());
            _db.GuessSessions.RemoveRange(await _db.GuessSessions.Where(g => g.UserId == userId).ToListAsync());
        }
        // Partie-Analysen samt ihrer Stellungen: PGN und Kandidatenlisten sind Nutzerarbeit an
        // konkreten Partien (oft eigenen) — keine anonyme Statistik. Die Stellungen hängen per
        // Cascade daran, werden hier aber explizit mitgenommen (InMemory cascadet nicht).
        var gameAnalysisIds = await _db.GameAnalyses.Where(g => g.UserId == userId).Select(g => g.Id).ToListAsync();
        if (gameAnalysisIds.Count > 0)
        {
            _db.GameAnalysisPositions.RemoveRange(
                await _db.GameAnalysisPositions.Where(p => gameAnalysisIds.Contains(p.GameAnalysisId)).ToListAsync());
            _db.GameAnalyses.RemoveRange(await _db.GameAnalyses.Where(g => g.UserId == userId).ToListAsync());
        }
        // Turnier-Runden-Monitor: personenbezogenes Beobachten eines Turniers (löst Benachrichtigungen
        // aus) — nach der Löschung gibt es niemanden mehr, der benachrichtigt werden möchte.
        _db.TournamentMonitors.RemoveRange(await _db.TournamentMonitors.Where(x => x.UserId == userId).ToListAsync());
        // Eigene Aktivitäts-Vorlagen und laufende Timer: Freitext-Bezeichnungen = PII.
        _db.ActivityPresets.RemoveRange(await _db.ActivityPresets.Where(x => x.UserId == userId).ToListAsync());
        _db.ActivityTimers.RemoveRange(await _db.ActivityTimers.Where(x => x.UserId == userId).ToListAsync());
        // „Gesehen"-Vermerke der Kalkulations-Serie: WER WANN welche Ausgabe geöffnet hat — ein
        // personenbezogenes Nutzungsprotokoll, keine Statistik (der Verteiler ist ohnehin weg).
        _db.CalcEditionViews.RemoveRange(await _db.CalcEditionViews.Where(x => x.UserId == userId).ToListAsync());
        // Katalog-Freigaben/-Anfragen des Nutzers (als Besitzer UND als Anfragender): Freigaben auf
        // Inhalte eines gelöschten Kontos sind gegenstandslos, die Anfrage nennt beide Personen.
        _db.CatalogGrants.RemoveRange(await _db.CatalogGrants
            .Where(x => x.OwnerUserId == userId || x.SubjectUserId == userId).ToListAsync());
        _db.CatalogRequests.RemoveRange(await _db.CatalogRequests
            .Where(x => x.OwnerUserId == userId || x.RequesterUserId == userId).ToListAsync());
        // Chessable-Themenzuordnung + Import-Aufträge: benennen die Kurse der fremden Bibliothek.
        _db.ChessableCourseThemes.RemoveRange(await _db.ChessableCourseThemes.Where(x => x.UserId == userId).ToListAsync());
        _db.ChessableImports.RemoveRange(await _db.ChessableImports
            .Where(x => x.UserId == userId || x.BearerUserId == userId).ToListAsync());
        // Persönliche Kurs-Verknüpfung (Buch↔Workbook) und Puzzle-Favoriten: reine Nutzerwahl.
        _db.CourseLinks.RemoveRange(await _db.CourseLinks.Where(x => x.UserId == userId).ToListAsync());
        _db.FavoritePuzzles.RemoveRange(await _db.FavoritePuzzles.Where(x => x.UserId == userId).ToListAsync());
        // Repertoire-Lernstand + eigene SR-Intervalle: die Repertoires selbst gehen oben mit, ihre
        // Karten-Zustände hängen aber an (UserId, LineKey) und blieben sonst als Waisen liegen.
        _db.RepertoireCardStates.RemoveRange(await _db.RepertoireCardStates.Where(x => x.UserId == userId).ToListAsync());
        _db.RepertoireSrSettings.RemoveRange(await _db.RepertoireSrSettings.Where(x => x.UserId == userId).ToListAsync());
        // Offene Kurs-Übersetzungsaufträge des Nutzers (RequestedByUserId hat bewusst keinen FK): nach der Löschung
        // wartet niemand mehr darauf, und sie hielten den Platz in der Warteschlange. Zurückgezogen statt gelöscht —
        // ein LAUFENDER bricht beim nächsten Zwischenstand von selbst ab (CourseTranslationJobService.RunAsync).
        // Aufträge an EIGENEN Büchern sind oben schon mit dem Buch gegangen.
        foreach (var job in await _db.CourseTranslationJobs.Where(j => j.RequestedByUserId == userId
                     && (j.Status == CourseTranslationJobStatus.Queued || j.Status == CourseTranslationJobStatus.Running))
                     .ToListAsync())
        {
            job.Status = CourseTranslationJobStatus.Cancelled;
            job.FinishedAt = DateTime.UtcNow;
            job.LastError = "account deleted";
        }
        // Manuelle Aktivitäten bleiben als (anonyme) Trainingsstatistik, aber die Freitext-Notiz (PII) wird geleert.
        var manualWithNote = await _db.ManualActivities.Where(a => a.UserId == userId && a.Note != null).ToListAsync();
        foreach (var a in manualWithNote) a.Note = null;

        // Den Namen tragen auch Benachrichtigungen ANDERER Nutzer (data.username: Freundschaft, Challenge, Teilen,
        // Neuanmeldung an alle Admins …) — sie bekommen unten, im selben Save, den anonymisierten Namen (A9-003).
        var mentions = await NotificationService.MentioningUsernameAsync(_db, user.Username, userId);

        // 2) Identität anonymisieren (in-place) -> nicht re-identifizierbar, Login gesperrt.
        //    FALLE Username-Squatting: „deleted_{id}"/„deleted_{id}@deleted.invalid" sind normale,
        //    vorab registrierbare Werte — hätte sie jemand belegt, schlüge der Unique-Index zu und
        //    die Löschung fiele dauerhaft auf 500. Darum: freien Namen vorab suchen (Zufallssuffix)
        //    und den TOCTOU-Rest über einen Kollisions-Retry am Unique-Index abfangen.
        for (var attempt = 0; ; attempt++)
        {
            var suffix = attempt == 0 ? "" : "_" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var username = $"deleted_{userId}{suffix}";
            var email = $"deleted_{userId}{suffix}@deleted.invalid";
            // Vorab-Prüfung (greift auch in den InMemory-Tests, die keine Unique-Indizes erzwingen).
            if (await _db.AppUsers.AnyAsync(u => u.Id != userId && (u.Username == username || u.Email == email)))
                continue;

            user.Username = username;
            user.Email = email;
            user.PasswordHash = PasswordHashing.Hash(Guid.NewGuid().ToString());
            user.IsAdmin = false;
            user.DeletedAt = DateTime.UtcNow;
            foreach (var n in mentions) NotificationService.SetUsername(n, username);

            // 3) Profil-PII entfernen (Statistik-Tabellen referenzieren weiterhin die UserId).
            if (user.Profile is { } p)
            {
                p.FirstName = p.LastName = p.DisplayName = null;
                p.FideId = p.ChessResultsId = p.ChessComUsername = p.LichessUsername = null;
                p.DiscordId = p.DiscordUsername = null;
            }

            try
            {
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (attempt < 5 && AuthService.IsUniqueViolation(ex))
            {
                // Race: der Name wurde ZWISCHEN Prüfung und Save registriert → mit neuem Suffix erneut.
            }
        }
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
