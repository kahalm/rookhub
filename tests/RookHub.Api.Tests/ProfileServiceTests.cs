using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ProfileServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProfileService _profileService;

    public ProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var logger = NullLogger<ProfileService>.Instance;
        _profileService = TestServices.Profile(_db, new NoOpTaskQueue(), logger);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserWithPasswordAsync(string username, string password)
    {
        var user = new Models.AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = true,
            Profile = new Models.UserProfile { DisplayName = "Real Name", FideId = "12345", DiscordId = "d1", DiscordUsername = "real#1" }
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Entitäten mit Nutzerbezug, die die Kontolöschung ABSICHTLICH stehen lässt — jeweils
    /// mit dem Grund. Alles andere MUSS in <c>DeleteAccountAsync</c> auftauchen.</summary>
    private static readonly Dictionary<string, string> DeletionExempt = new(StringComparer.Ordinal)
    {
        ["EndlessSessions"] = "anonyme Laufstatistik (bleibt unter der Id, ohne PII)",
        ["ManualActivities"] = "Trainingsstatistik; die Notiz (PII) wird geleert statt gelöscht",
        ["PuzzleAttempts"] = "anonyme Löse-Statistik",
        ["BookPuzzleAttempts"] = "anonyme Löse-Statistik",
        ["CourseAttempts"] = "anonymes Zeit-Log des Trainingsziel-Trackers",
        ["CoursePuzzleResults"] = "anonymer Kursfortschritt",
        ["CourseInfoViews"] = "anonymer Kursfortschritt",
        ["CourseProgresses"] = "anonymer Kursfortschritt",
        ["WeeklyPostAttempts"] = "anonyme Löse-Statistik",
        ["ChessableActivities"] = "anonymes Zeit-Log (Chessable-Kategorie)",
        ["PlayTimeDailies"] = "anonyme Partienzählung",
        ["PlayTimeSyncs"] = "nur Cursor/Zeitstempel, keine PII",
        ["UserGroups"] = "Gruppenzugehörigkeit trägt keine PII und stirbt mit der Gruppe",
        ["UserTrainingGoals"] = "Zielwerte (Zahlen), keine PII",
        ["EndlessProgresses"] = "Konfiguration/Highscore ohne PII",
        ["SharedPuzzleAttempts"] = "IdentityKey, kein FK auf den Nutzer",
        ["CourseFlashcardMarks"] = "Markierungen ohne PII",
        ["RepertoireFlashcardMarks"] = "Markierungen ohne PII",
        ["CoursePins"] = "Markierungen ohne PII",
        ["AdminMessages"] = "Konversation mit dem Admin-Team (Nachweis; wird beim Löschen des Threads entfernt)",
        ["MessageThreads"] = "Metadaten der Admin-Konversation",
        ["CiBuildReports"] = "kein Nutzerbezug (Repo-Zeile)",
        ["UserProfiles"] = "wird IN PLACE anonymisiert (über user.Profile), nicht gelöscht — "
                           + "die Statistik-Tabellen zeigen weiter auf die UserId",
    };

    [Fact]
    public void DeleteAccount_CoversEveryUserOwnedTable()
    {
        // Die Löschung ANONYMISIERT die Nutzerzeile — es feuert also KEIN Cascade-FK, jede
        // nutzereigene Tabelle muss von Hand aufgeräumt werden. Der Aufzählungs-Test darüber kann
        // das nicht absichern: eine NEUE Tabelle steht dort naturgemäß auch nicht. Deshalb hier
        // gegen das DbContext-Modell prüfen (genau so blieben Push-Abos, Analyseaufträge und
        // Verteiler-Mitgliedschaften nach der ersten Fassung liegen).
        var source = ReadDeleteAccountSource();
        var missing = new List<string>();

        foreach (var prop in typeof(AppDbContext).GetProperties()
                     .Where(p => p.PropertyType.IsGenericType
                                 && p.PropertyType.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>)))
        {
            var entity = prop.PropertyType.GetGenericArguments()[0];
            var ownsUser = entity.GetProperty("UserId") != null || entity.GetProperty("OwnerUserId") != null;
            if (!ownsUser || entity == typeof(Models.AppUser)) continue;
            if (DeletionExempt.ContainsKey(prop.Name)) continue;
            if (!source.Contains($"_db.{prop.Name}", StringComparison.Ordinal)) missing.Add(prop.Name);
        }

        Assert.True(missing.Count == 0,
            "Nutzereigene Tabelle(n) ohne Behandlung in DeleteAccountAsync (DSGVO-Löschung lässt die "
            + "Daten dauerhaft liegen — aufräumen ODER mit Grund in DeletionExempt eintragen):\n  "
            + string.Join("\n  ", missing.OrderBy(m => m, StringComparer.Ordinal)));
    }

    /// <summary>Quelltext von <c>ProfileService.DeleteAccountAsync</c> (Methodenrumpf). Der Pfad kommt
    /// über <see cref="CallerFilePathAttribute"/>, ist also im normalen Lauf immer auflösbar — ein
    /// stilles Überspringen gäbe es hier nicht, der Test scheitert stattdessen.</summary>
    private static string ReadDeleteAccountSource([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        string? file = null;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Services", "ProfileService.cs");
            if (File.Exists(candidate)) { file = candidate; break; }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(file);
        var src = File.ReadAllText(file!);
        var start = src.IndexOf("public async Task DeleteAccountAsync", StringComparison.Ordinal);
        Assert.True(start >= 0, "DeleteAccountAsync nicht gefunden — Methode umbenannt?");
        var end = src.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return end > start ? src[start..end] : src[start..];
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_Throws_AndKeepsData()
    {
        var id = await CreateUserWithPasswordAsync("delme1", "secret123");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _profileService.DeleteAccountAsync(id, "wrong-password"));
        var user = await _db.AppUsers.Include(u => u.Profile).FirstAsync(u => u.Id == id);
        Assert.Null(user.DeletedAt);
        Assert.Equal("Real Name", user.Profile!.DisplayName);
    }

    [Fact]
    public async Task DeleteAccount_CorrectPassword_Anonymizes_RemovesPersonal_KeepsStats()
    {
        var id = await CreateUserWithPasswordAsync("delme2", "secret123");
        var other = await CreateUserWithPasswordAsync("frienduser", "x");
        _db.Friendships.Add(new Models.Friendship { RequesterId = id, AddresseeId = other, Status = Models.FriendshipStatus.Accepted });
        _db.EndlessSessions.Add(new Models.EndlessSession { UserId = id, Timestamp = 1, TotalSolved = 7 });
        _db.UserApiTokens.Add(new Models.UserApiToken { UserId = id, Name = "ext", TokenHash = "h", Prefix = "rkh_abc", Scope = "extension" });
        // Secrets/öffentliche Inhalte/PII, die mit der Löschung verschwinden müssen:
        _db.ChessableCredentials.Add(new Models.ChessableCredential { UserId = id, EncryptedBearer = "enc" });
        _db.LichessEngineCredentials.Add(new Models.LichessEngineCredential { UserId = id, EncryptedToken = "enc-lip" });
        _db.PasswordResetTokens.Add(new Models.PasswordResetToken { UserId = id, TokenHash = "prt", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        _db.SavedGames.Add(new Models.SavedGame { UserId = id, Source = "chess.com", Pgn = "1. e4", ShareToken = "g-tok" });
        _db.SharedLines.Add(new Models.SharedLine { OwnerUserId = id, Pgn = "1. e4", LineHash = "lh", ShareToken = "l-tok" });
        _db.RememberedPositions.Add(new Models.RememberedPosition { UserId = id, Fen = "8/8/8/8/8/8/8/8 w - - 0 1" });
        // Später dazugekommene Tabellen, die beim Erweitern der Löschung vergessen wurden:
        _db.UserPushSubscriptions.Add(new Models.UserPushSubscription { UserId = id, Endpoint = "https://push.example/abc", P256dh = "p", Auth = "a" });
        _db.NotificationPushSettings.Add(new Models.NotificationPushSetting { UserId = id, EnabledCategories = "courses" });
        _db.CalcSeriesMembers.Add(new Models.CalcSeriesMember { BookId = 4242, UserId = id, IsTester = true });
        _db.Notifications.Add(new Models.Notification { UserId = id, Type = Models.NotificationType.CourseShared, DataJson = "{}" });
        _db.AnalysisJobs.Add(new Models.AnalysisJob { UserId = id, Fen = "8/8/8/8/8/8/8/8 w - - 0 1", EngineId = "eei_x", Title = "meine Turmstellung", TargetDepth = 30, MultiPv = 3 });
        _db.CalculationTrees.Add(new Models.CalculationTree { UserId = id, BookId = 4242, BookPuzzleId = 77, TreeJson = "{\"m\":[]}" });
        _db.ChessableSessionMoves.Add(new Models.ChessableSessionMove { UserId = id, Bid = "1", Oid = "2", MovesJson = "[]" });
        _db.ChessableReviewLines.Add(new Models.ChessableReviewLine { UserId = id, Bid = "1", Oid = "2", Json = "{}" });
        _db.ChessableProblemMoves.Add(new Models.ChessableProblemMove { UserId = id, Bid = "1", Oid = "2" });
        // Nachträglich dazugekommene nutzereigene Tabellen (vom Reflection-Test aufgedeckt):
        _db.ChessableImports.Add(new Models.ChessableImport { UserId = id, Bid = "12345", CourseName = "Mein Kurs", Target = "book" });
        _db.CalcEditionViews.Add(new Models.CalcEditionView { CalcEditionId = 9, UserId = id, ViewedAt = DateTime.UtcNow });
        _db.FavoritePuzzles.Add(new Models.FavoritePuzzle { UserId = id, PuzzleId = 5 });
        _db.TournamentMonitors.Add(new Models.TournamentMonitor { UserId = id, CrawlerTournamentId = "42", ActiveUntil = DateTime.UtcNow.AddDays(3) });
        _db.RepertoireSrSettings.Add(new Models.RepertoireSrSettings { UserId = id, IntervalsJson = "[]" });
        // Manuelle Aktivität bleibt als Statistik, aber die Notiz (PII) wird geleert:
        _db.ManualActivities.Add(new Models.ManualActivity { UserId = id, Date = new DateOnly(2026, 7, 1), Kind = Models.ManualActivityKind.OtbGame, Amount = 1, Note = "gegen Max am Vereinsabend" });
        await _db.SaveChangesAsync();

        await _profileService.DeleteAccountAsync(id, "secret123");

        var user = await _db.AppUsers.Include(u => u.Profile).FirstAsync(u => u.Id == id);
        // Identität anonymisiert + Login gesperrt
        Assert.NotNull(user.DeletedAt);
        Assert.Equal($"deleted_{id}", user.Username);
        Assert.Contains("@deleted.invalid", user.Email);
        Assert.False(user.IsAdmin);
        Assert.False(BCrypt.Net.BCrypt.Verify("secret123", user.PasswordHash));
        // PII entfernt
        Assert.Null(user.Profile!.DisplayName);
        Assert.Null(user.Profile.FideId);
        Assert.Null(user.Profile.DiscordId);
        // persönliche Verknüpfung weg
        Assert.False(await _db.Friendships.AnyAsync(f => f.RequesterId == id || f.AddresseeId == id));
        // Statistik bleibt (anonym, unter der UserId)
        Assert.True(await _db.EndlessSessions.AnyAsync(s => s.UserId == id && s.TotalSolved == 7));
        // API-Tokens widerrufen (kein Zugang nach Löschung)
        Assert.False(await _db.UserApiTokens.AnyAsync(t => t.UserId == id));
        // Live-Bearer + Einmal-Tokens + öffentliche Share-Inhalte + gemerkte Stellungen sind weg
        Assert.False(await _db.ChessableCredentials.AnyAsync(c => c.UserId == id));
        // Der Lichess-OAuth-Token gehört dazu: die Löschung anonymisiert die User-Zeile nur, der
        // Cascade-FK feuert also nicht — ohne die explizite Aufräumzeile bliebe er entschlüsselbar liegen.
        Assert.False(await _db.LichessEngineCredentials.AnyAsync(c => c.UserId == id));
        Assert.False(await _db.PasswordResetTokens.AnyAsync(t => t.UserId == id));
        Assert.False(await _db.SavedGames.AnyAsync(g => g.UserId == id));
        Assert.False(await _db.SharedLines.AnyAsync(l => l.OwnerUserId == id));
        Assert.False(await _db.RememberedPositions.AnyAsync(r => r.UserId == id));
        // Web-Push: ohne diese Zeilen schickte der Server weiter Benachrichtigungen an das Gerät eines
        // gelöschten Kontos (der Serien-Verteiler sammelt die Id ja weiter ein, solange er sie kennt).
        Assert.False(await _db.UserPushSubscriptions.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.NotificationPushSettings.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.CalcSeriesMembers.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.Notifications.AnyAsync(x => x.UserId == id));
        // Analyseaufträge tragen selbst gewählte Titel (PII-nah) und verbrauchten weiter Rechenzeit.
        Assert.False(await _db.AnalysisJobs.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.CalculationTrees.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.ChessableSessionMoves.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.ChessableReviewLines.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.ChessableProblemMoves.AnyAsync(x => x.UserId == id));
        // Die nachträglich ergänzten Tabellen sind ebenfalls leer (keine Waisen mit Kursnamen,
        // Nutzungsprotokollen oder Beobachtungsaufträgen eines gelöschten Kontos).
        Assert.False(await _db.ChessableImports.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.CalcEditionViews.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.FavoritePuzzles.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.TournamentMonitors.AnyAsync(x => x.UserId == id));
        Assert.False(await _db.RepertoireSrSettings.AnyAsync(x => x.UserId == id));
        // Manuelle Aktivität bleibt (Statistik), aber ohne Freitext-Notiz
        var manual = await _db.ManualActivities.SingleAsync(a => a.UserId == id);
        Assert.Null(manual.Note);
    }

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new Models.AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new Models.UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task GetProfile_ReturnsProfile()
    {
        var userId = await CreateUserAsync();
        var profile = await _profileService.GetProfileAsync(userId);
        Assert.Equal("testuser", profile.Username);
    }

    [Fact]
    public async Task UpdateProfile_UpdatesFields()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            DisplayName = "Test User",
            FideId = "12345",
            ChessComUsername = "testplayer"
        });

        Assert.Equal("Test User", result.DisplayName);
        Assert.Equal("12345", result.FideId);
        Assert.Equal("testplayer", result.ChessComUsername);
    }

    [Fact]
    public async Task GetPublicProfileByUsername_ReturnsReducedProfile_WithoutPii()
    {
        var userId = await CreateUserAsync("alice");
        // Sensible Felder setzen, die NICHT öffentlich erscheinen dürfen.
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            DisplayName = "Alice A.",
            FirstName = "Alice",
            LastName = "Anderson",
            FideId = "999",
            ChessResultsId = "777",
        });
        await _profileService.LinkDiscordAsync(userId, "discord-123", "alice#1");

        var profile = await _profileService.GetPublicProfileByUsernameAsync("alice");

        Assert.Equal("alice", profile.Username);
        Assert.Equal("Alice A.", profile.DisplayName);
        Assert.Equal("999", profile.FideId);
        // PublicProfileDto besitzt KEINE DiscordId/ChessResultsId/Klarnamen-Felder (Compile-Garantie),
        // d.h. diese Daten können gar nicht anonym geleakt werden.
    }

    [Fact]
    public async Task UpdateProfile_SetsFirstNameLastName()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            FirstName = "Johann",
            LastName = "Huber"
        });

        Assert.Equal("Johann", result.FirstName);
        Assert.Equal("Huber", result.LastName);
    }

    [Fact]
    public async Task GetProfile_ReturnsFirstNameLastName()
    {
        var userId = await CreateUserAsync();
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            FirstName = "Maria",
            LastName = "Schmidt"
        });

        var profile = await _profileService.GetProfileAsync(userId);
        Assert.Equal("Maria", profile.FirstName);
        Assert.Equal("Schmidt", profile.LastName);
    }

    [Fact]
    public async Task UpdateProfile_WithPreferences_PersistsAll()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BoardTheme = "green",
            PieceSet = "merida",
            StockfishDepth = 20,
            PuzzleDifficulty = "schwer",
            BookStockfishDepth = 12
        });

        Assert.Equal("green", result.BoardTheme);
        Assert.Equal("merida", result.PieceSet);
        Assert.Equal(20, result.StockfishDepth);
        Assert.Equal("schwer", result.PuzzleDifficulty);
        Assert.Equal(12, result.BookStockfishDepth);
    }

    [Fact]
    public async Task GetProfile_IncludesPreferences()
    {
        var userId = await CreateUserAsync();
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BoardTheme = "blue",
            PieceSet = "fantasy",
            StockfishDepth = 8
        });

        var profile = await _profileService.GetProfileAsync(userId);
        Assert.Equal("blue", profile.BoardTheme);
        Assert.Equal("fantasy", profile.PieceSet);
        Assert.Equal(8, profile.StockfishDepth);
    }

    [Fact]
    public async Task UpdateProfile_NullPreferences_ExistingKept()
    {
        var userId = await CreateUserAsync();
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BoardTheme = "wood",
            PieceSet = "spatial",
            StockfishDepth = 18
        });

        // Update only DisplayName, preferences should stay unchanged
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            DisplayName = "Unchanged Prefs"
        });

        Assert.Equal("wood", result.BoardTheme);
        Assert.Equal("spatial", result.PieceSet);
        Assert.Equal(18, result.StockfishDepth);
        Assert.Equal("Unchanged Prefs", result.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_StockfishDepthRange_Clamped()
    {
        var userId = await CreateUserAsync();

        // Too high
        var result1 = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            StockfishDepth = 50
        });
        Assert.Equal(24, result1.StockfishDepth);

        // Too low
        var result2 = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            StockfishDepth = 0
        });
        Assert.Equal(1, result2.StockfishDepth);

        // BookStockfishDepth too high
        var result3 = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BookStockfishDepth = 99
        });
        Assert.Equal(24, result3.BookStockfishDepth);
    }

    [Fact]
    public async Task UpdateProfile_PreferenceOnly_DoesNotTriggerAutoSubscription()
    {
        var userId = await CreateUserAsync();
        var queue = new CountingTaskQueue();
        var service = TestServices.Profile(_db, queue);

        // Identität setzen (ein Trigger erwartet).
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { ChessResultsId = "T1", LastName = "Müller" });
        Assert.Equal(1, queue.EnqueuedCount);

        // Reine Einstellung (kein Identitäts-Feld) -> KEIN weiterer Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { BoardTheme = "blue" });
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { StockfishDepth = 20 });
        Assert.Equal(1, queue.EnqueuedCount);
    }

    [Fact]
    public async Task UpdateProfile_IdentityChange_TriggersAutoSubscription()
    {
        var userId = await CreateUserAsync();
        var queue = new CountingTaskQueue();
        var service = TestServices.Profile(_db, queue);

        await service.UpdateProfileAsync(userId, new UpdateProfileDto { ChessResultsId = "T1", LastName = "Müller" });
        Assert.Equal(1, queue.EnqueuedCount);

        // Nachname geändert -> erneuter Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { LastName = "Meier" });
        Assert.Equal(2, queue.EnqueuedCount);

        // Gleicher Nachname erneut gesetzt (keine echte Änderung) -> kein Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { LastName = "Meier" });
        Assert.Equal(2, queue.EnqueuedCount);

        // Auch eine FideId-Änderung zählt als Identitätsänderung -> Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { FideId = "999" });
        Assert.Equal(3, queue.EnqueuedCount);
    }

    [Fact]
    public async Task UpdateProfile_SetsEmail_NormalizedLowercaseTrimmed()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "  New.Mail@Example.COM  " });
        Assert.Equal("new.mail@example.com", result.Email);
        Assert.Equal("new.mail@example.com", (await _db.AppUsers.FindAsync(userId))!.Email);
    }

    [Fact]
    public async Task UpdateProfile_EmptyEmail_ClearsEmail()
    {
        var userId = await CreateUserAsync(); // startet mit testuser@example.com
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "" });
        Assert.Null(result.Email);
        Assert.Null((await _db.AppUsers.FindAsync(userId))!.Email);
    }

    [Fact]
    public async Task UpdateProfile_NullEmail_LeavesEmailUnchanged()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { DisplayName = "X" });
        Assert.Equal("testuser@example.com", result.Email);
    }

    [Fact]
    public async Task UpdateProfile_InvalidEmail_Throws()
    {
        var userId = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(
            () => _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "not-an-email" }));
    }

    [Fact]
    public async Task UpdateProfile_DuplicateEmail_Throws()
    {
        await CreateUserAsync("alice");          // alice@example.com
        var bobId = await CreateUserAsync("bob"); // bob@example.com
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profileService.UpdateProfileAsync(bobId, new UpdateProfileDto { Email = "ALICE@example.com" }));
    }

    [Fact]
    public async Task UpdateProfile_SameEmailAsOwn_Succeeds()
    {
        var userId = await CreateUserAsync(); // testuser@example.com
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "testuser@example.com" });
        Assert.Equal("testuser@example.com", result.Email);
    }
}
