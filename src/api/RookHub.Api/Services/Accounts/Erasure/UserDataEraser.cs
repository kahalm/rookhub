using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Was jeder Schritt der Kontolöschung braucht: der Kontext, das Konto und der Admin-Löschpfad für Bücher.</summary>
internal sealed record UserErasureContext(AppDbContext Db, int UserId, BookAdminService BookAdmin);

/// <summary>
/// Der Löschkern der Kontolöschung (aus <see cref="ProfileService"/> gezogen, Zerlegung Z15, A9-006) — Selbst- UND
/// Admin-Löschung laufen hierüber (<see cref="ProfileService.EraseUserAsync"/>). Erst räumt jeder Bereich seine Zeilen ab
/// (<see cref="Steps"/>, eine FESTE Reihenfolge, kein Assembly-Scan: persönliche Bücher speichern je Buch selbst und stehen
/// deshalb zuerst, Chessable-Rohdaten löschen relational sofort, Punktepartien müssen vor den Partie-Analysen weg —
/// Restrict), dann wird die Identität IN PLACE anonymisiert und alles in EINEM Save geschrieben
/// (<see cref="AnonymizeAsync"/>). Die Schritte speichern nicht selbst (Ausnahmen oben).
/// <para>Ein Bereich kann mehrmals in der Liste stehen — die Reihenfolge ist die der bisherigen Methode, Zeile für
/// Zeile, und wird nicht nach Bereichen umsortiert.</para>
/// </summary>
internal sealed class UserDataEraser
{
    // 0) Persönliche Bücher zuerst (CourseDataEraser.OwnedBooksAsync — speichert je Buch selbst).
    // 1) Persönliche Inhalte & Verknüpfungen hart entfernen (keine Statistik):
    //    Freundschaften (FK Restrict -> müssen explizit weg), Puzzle-Challenges und Revanche-Vermerke in beiden
    //    Rollen (A1-018; abhängiges Ende ihrer Restrict-FKs, blockieren also nichts), Repertoires (cascadet Dateien),
    //    Turnier-Abos/-Favoriten/-Einstellungen, Gruppen-Mitgliedschaften, API-Tokens,
    //    Chessable-Bearer + Reset-Tokens, öffentliche Share-Inhalte, gemerkte Stellungen,
    //    Admin-Direktnachrichten (Freitext = PII) samt Thread-Metadaten.
    internal static readonly IReadOnlyList<Func<UserErasureContext, Task>> Steps = new Func<UserErasureContext, Task>[]
    {
        CourseDataEraser.OwnedBooksAsync,
        SocialDataEraser.FriendshipsAsync,
        PuzzleDataEraser.ChallengesAsync,
        PuzzleDataEraser.RevengeNotificationsAsync,
        RepertoireDataEraser.RepertoiresAsync,
        TournamentDataEraser.FollowingAsync,
        AccountDataEraser.ViewStatesAsync,
        LeagueDataEraser.UploadsAsync,
        ClubDataEraser.MembershipAsync,
        KidsDataEraser.ProgressAsync,
        TrainingDataEraser.MarksAndWorksheetsAsync,
        EbookDataEraser.ReadingAsync,
        AccountDataEraser.AccessAsync,
        ScoresheetDataEraser.ScansAsync,
        GamesDataEraser.SavedAndSharedAsync,
        ChessableDataEraser.RememberedPositionsAsync,
        SocialDataEraser.MessagesAndNotificationsAsync,
        AccountDataEraser.RolesAsync,
        AnalysisDataEraser.JobsAndHistoryAsync,
        ChessableDataEraser.RawDataAsync,
        AnalysisDataEraser.CalculationAndAnalysesAsync,
        TournamentDataEraser.MonitorsAsync,
        TrainingDataEraser.ActivitiesAsync,
        CourseDataEraser.CalcAndCatalogAsync,
        ChessableDataEraser.ThemesAndImportsAsync,
        CourseDataEraser.LinksAndFavoritesAsync,
        RepertoireDataEraser.LearningStateAsync,
        CourseDataEraser.TranslationJobsAsync,
        TrainingDataEraser.ManualNotesAsync,
    };

    private readonly AppDbContext _db;
    private readonly BookAdminService _bookAdmin;
    private readonly DiscordLinkService _discordLink;

    public UserDataEraser(AppDbContext db, BookAdminService bookAdmin, DiscordLinkService discordLink)
    {
        _db = db; _bookAdmin = bookAdmin; _discordLink = discordLink;
    }

    /// <summary>Löscht und anonymisiert das Konto (Aufrufer haben Existenz, Idempotenz und ggf. das Passwort geprüft).</summary>
    public async Task EraseAsync(AppUser user)
    {
        var ctx = new UserErasureContext(_db, user.Id, _bookAdmin);
        foreach (var step in Steps) await step(ctx);
        await AnonymizeAsync(user);
    }

    /// <summary>Namen in fremden Benachrichtigungen ersetzen, Discord-ID freigeben, Identität und Profil anonymisieren —
    /// im selben Save wie alle Löschungen der Schritte.</summary>
    private async Task AnonymizeAsync(AppUser user)
    {
        var userId = user.Id;
        // Den Namen tragen auch Benachrichtigungen ANDERER Nutzer (data.username: Freundschaft, Challenge, Teilen,
        // Neuanmeldung an alle Admins …) — sie bekommen unten, im selben Save, den anonymisierten Namen (A9-003).
        var mentions = await NotificationService.MentioningUsernameAsync(_db, user.Username, userId);
        // Vor der Schleife: ein Wiederholungslauf sähe die schon geleerte ID nicht mehr.
        var releasedDiscordId = user.Profile?.DiscordId;
        // Die Discord-ID wird frei wie beim Trennen: alte Bot-Links bekommt kein anderes Konto (A1-011). Der Vermerk
        // geht im selben Save wie die Anonymisierung in DiscordLinkReleases — vorher ist das Konto nicht „weg".
        if (releasedDiscordId != null) await _discordLink.MarkReleasedAsync(releasedDiscordId, userId);

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
    }
}
