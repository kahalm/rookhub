using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;

namespace RookHub.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<PuzzleChallenge> PuzzleChallenges => Set<PuzzleChallenge>();
    public DbSet<FavoritePuzzle> FavoritePuzzles => Set<FavoritePuzzle>();
    public DbSet<RevengeNotification> RevengeNotifications => Set<RevengeNotification>();
    public DbSet<Repertoire> Repertoires => Set<Repertoire>();
    public DbSet<RepertoireFile> RepertoireFiles => Set<RepertoireFile>();
    public DbSet<TournamentSubscription> TournamentSubscriptions => Set<TournamentSubscription>();
    public DbSet<TournamentFavorite> TournamentFavorites => Set<TournamentFavorite>();
    public DbSet<TournamentUserSetting> TournamentUserSettings => Set<TournamentUserSetting>();
    public DbSet<TournamentMonitor> TournamentMonitors => Set<TournamentMonitor>();
    public DbSet<Puzzle> Puzzles => Set<Puzzle>();
    public DbSet<PuzzleAttempt> PuzzleAttempts => Set<PuzzleAttempt>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PuzzleTag> PuzzleTags => Set<PuzzleTag>();
    public DbSet<BookPuzzle> BookPuzzles => Set<BookPuzzle>();
    public DbSet<BookPuzzleAttempt> BookPuzzleAttempts => Set<BookPuzzleAttempt>();
    public DbSet<SharedPuzzleAttempt> SharedPuzzleAttempts => Set<SharedPuzzleAttempt>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<EndlessProgress> EndlessProgresses => Set<EndlessProgress>();
    public DbSet<EndlessSession> EndlessSessions => Set<EndlessSession>();
    public DbSet<CourseProgress> CourseProgresses => Set<CourseProgress>();
    public DbSet<CoursePuzzleResult> CoursePuzzleResults => Set<CoursePuzzleResult>();
    public DbSet<CoursePin> CoursePins => Set<CoursePin>();
    public DbSet<CourseShare> CourseShares => Set<CourseShare>();
    public DbSet<CourseLink> CourseLinks => Set<CourseLink>();
    public DbSet<CourseInfoView> CourseInfoViews => Set<CourseInfoView>();
    public DbSet<CourseAttempt> CourseAttempts => Set<CourseAttempt>();
    public DbSet<BookGroupAccess> BookGroupAccesses => Set<BookGroupAccess>();
    public DbSet<WeeklyPost> WeeklyPosts => Set<WeeklyPost>();
    public DbSet<WeeklyPostAttempt> WeeklyPostAttempts => Set<WeeklyPostAttempt>();
    public DbSet<DailyPuzzle> DailyPuzzles => Set<DailyPuzzle>();
    public DbSet<UserApiToken> UserApiTokens => Set<UserApiToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<GroupTrainingGoal> GroupTrainingGoals => Set<GroupTrainingGoal>();
    public DbSet<UserTrainingGoal> UserTrainingGoals => Set<UserTrainingGoal>();
    public DbSet<PlayTimeDaily> PlayTimeDailies => Set<PlayTimeDaily>();
    public DbSet<PlayTimeSync> PlayTimeSyncs => Set<PlayTimeSync>();
    public DbSet<ChessableCredential> ChessableCredentials => Set<ChessableCredential>();
    public DbSet<ChessableImport> ChessableImports => Set<ChessableImport>();
    public DbSet<MenuItemSetting> MenuItemSettings => Set<MenuItemSetting>();
    public DbSet<MenuItemGroupAccess> MenuItemGroupAccesses => Set<MenuItemGroupAccess>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AdminMessage> AdminMessages => Set<AdminMessage>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<ChessableActivity> ChessableActivities => Set<ChessableActivity>();
    public DbSet<ChessableCourseTheme> ChessableCourseThemes => Set<ChessableCourseTheme>();
    public DbSet<ManualActivity> ManualActivities => Set<ManualActivity>();
    public DbSet<ActivityPreset> ActivityPresets => Set<ActivityPreset>();
    public DbSet<ActivityTimer> ActivityTimers => Set<ActivityTimer>();
    public DbSet<RememberedPosition> RememberedPositions => Set<RememberedPosition>();
    public DbSet<CiBuildReport> CiBuildReports => Set<CiBuildReport>();
    public DbSet<SavedGame> SavedGames => Set<SavedGame>();
    public DbSet<RepertoireCardState> RepertoireCardStates => Set<RepertoireCardState>();
    public DbSet<RepertoireSrSettings> RepertoireSrSettings => Set<RepertoireSrSettings>();
    public DbSet<RepertoireShare> RepertoireShares => Set<RepertoireShare>();
    public DbSet<UserPushSubscription> UserPushSubscriptions => Set<UserPushSubscription>();
    public DbSet<NotificationPushSetting> NotificationPushSettings => Set<NotificationPushSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<UserProfile>(e =>
        {
            e.HasOne(p => p.User)
             .WithOne(u => u.Profile)
             .HasForeignKey<UserProfile>(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // Eine Discord-ID ist höchstens einem RookHub-User zugeordnet.
            // (MySQL behandelt mehrere NULLs als verschieden → nicht-verknüpfte Profile sind ok.)
            e.HasIndex(p => p.DiscordId).IsUnique();
        });

        modelBuilder.Entity<Friendship>(e =>
        {
            e.HasOne(f => f.Requester)
             .WithMany()
             .HasForeignKey(f => f.RequesterId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(f => f.Addressee)
             .WithMany()
             .HasForeignKey(f => f.AddresseeId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();

            // Richtungsunabhaengige Eindeutigkeit: genau eine Zeile pro ungeordnetem Paar.
            // Der direktionale Index oben verhindert NICHT, dass gleichzeitige A->B und
            // B->A zwei Zeilen erzeugen. STORED computed columns + Unique-Index loesen das
            // auf DB-Ebene (MariaDB kennt keine gefilterten/funktionalen Indizes) — analog
            // zur Crawler-ActiveKey-Loesung. Greift mit dem catch(DbUpdateException) in
            // FriendService.SendRequestAsync zusammen, das dann sauber 409/Fehler liefert.
            e.Property<int>("PairLow").HasComputedColumnSql("LEAST(RequesterId, AddresseeId)", stored: true);
            e.Property<int>("PairHigh").HasComputedColumnSql("GREATEST(RequesterId, AddresseeId)", stored: true);
            e.HasIndex("PairLow", "PairHigh").IsUnique();
        });

        modelBuilder.Entity<PuzzleChallenge>(e =>
        {
            // Zwei FKs auf AppUser → Restrict (wie Friendship), sonst mehrere Cascade-Pfade.
            // Konten werden ohnehin anonymisiert statt hart gelöscht (DSGVO-Flow).
            e.HasOne(c => c.FromUser)
             .WithMany()
             .HasForeignKey(c => c.FromUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.ToUser)
             .WithMany()
             .HasForeignKey(c => c.ToUserId)
             .OnDelete(DeleteBehavior.Restrict);

            // PuzzleId ist polymorph (Puzzles ODER BookPuzzles, je nach Source) → bewusst KEIN FK.
            // Existenz wird je Quelle im ChallengeService geprüft.

            // Posteingang/Badge: offene Challenges an einen Empfänger.
            e.HasIndex(c => new { c.ToUserId, c.Status });
            // Gesendete Challenges eines Absenders.
            e.HasIndex(c => c.FromUserId);
            // Dedup offener Challenges je Quelle+Puzzle.
            e.HasIndex(c => new { c.Source, c.PuzzleId });
        });

        modelBuilder.Entity<FavoritePuzzle>(e =>
        {
            e.HasOne(f => f.User)
             .WithMany()
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // PuzzleId ist polymorph (Puzzles ODER BookPuzzles, je nach Source) → bewusst KEIN FK.
            // Ein Puzzle je User+Quelle nur einmal favorisierbar.
            e.HasIndex(f => new { f.UserId, f.Source, f.PuzzleId }).IsUnique();
            // Auflistung „neueste zuerst".
            e.HasIndex(f => new { f.UserId, f.CreatedAt });
        });

        modelBuilder.Entity<RevengeNotification>(e =>
        {
            // Zwei FKs auf AppUser → Restrict (wie Friendship/PuzzleChallenge).
            e.HasOne(n => n.AvengerUser)
             .WithMany()
             .HasForeignKey(n => n.AvengerUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(n => n.TargetUser)
             .WithMany()
             .HasForeignKey(n => n.TargetUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(n => n.Puzzle)
             .WithMany()
             .HasForeignKey(n => n.PuzzleId)
             .OnDelete(DeleteBehavior.Restrict);

            // Feed + Badge: (un)gelesene Benachrichtigungen eines Targets.
            e.HasIndex(n => new { n.TargetUserId, n.SeenAt });
        });

        modelBuilder.Entity<Notification>(e =>
        {
            // Generische In-App-Benachrichtigung; wird mit dem User gelöscht.
            e.HasOne(n => n.User)
             .WithMany()
             .HasForeignKey(n => n.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(n => n.Type).HasMaxLength(60);
            e.Property(n => n.Link).HasMaxLength(300);

            // Feed + Badge: (un)gelesene Benachrichtigungen eines Users.
            e.HasIndex(n => new { n.UserId, n.SeenAt });
        });

        modelBuilder.Entity<AdminMessage>(e =>
        {
            // Thread-Schlüssel = der Nicht-Admin-Teilnehmer; wird mit dem User gelöscht.
            e.HasOne(m => m.User)
             .WithMany()
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(m => m.Body).HasMaxLength(4000);

            // Thread laden (UserId, chronologisch) + Admin-Badge (ungelesene User-Antworten).
            e.HasIndex(m => new { m.UserId, m.CreatedAt });
            e.HasIndex(m => new { m.FromAdmin, m.SeenByAdminAt });
        });

        modelBuilder.Entity<MessageThread>(e =>
        {
            // Schlüssel = User; wird mit dem User gelöscht. ClaimedByAdminId bewusst ohne FK.
            e.HasKey(t => t.UserId);
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Repertoire>(e =>
        {
            e.HasOne(r => r.User)
             .WithMany(u => u.Repertoires)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepertoireFile>(e =>
        {
            e.HasOne(rf => rf.Repertoire)
             .WithMany(r => r.Files)
             .HasForeignKey(rf => rf.RepertoireId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TournamentSubscription>(e =>
        {
            e.HasOne(ts => ts.User)
             .WithMany(u => u.TournamentSubscriptions)
             .HasForeignKey(ts => ts.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ts => new { ts.UserId, ts.CrawlerTournamentId }).IsUnique();
            e.HasIndex(ts => ts.CrawlerTournamentId);
        });

        modelBuilder.Entity<TournamentFavorite>(e =>
        {
            e.HasOne(tf => tf.User)
             .WithMany(u => u.TournamentFavorites)
             .HasForeignKey(tf => tf.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(tf => new { tf.UserId, tf.CrawlerTournamentId, tf.PlayerSnr }).IsUnique();
            e.HasIndex(tf => new { tf.UserId, tf.CrawlerTournamentId, tf.TeamSnr }).IsUnique();
        });

        modelBuilder.Entity<TournamentUserSetting>(e =>
        {
            e.HasOne(s => s.User)
             .WithMany(u => u.TournamentUserSettings)
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(s => new { s.UserId, s.CrawlerTournamentId }).IsUnique();
        });

        modelBuilder.Entity<TournamentMonitor>(e =>
        {
            e.HasOne(m => m.User)
             .WithMany()
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => new { m.UserId, m.CrawlerTournamentId }).IsUnique();
        });

        modelBuilder.Entity<Puzzle>(e =>
        {
            e.HasIndex(p => p.LichessId).IsUnique();
            e.HasIndex(p => p.Rating);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<PuzzleTag>(e =>
        {
            e.HasKey(pt => new { pt.PuzzleId, pt.TagId });
            // Killer-Index: „Puzzles mit Thema X im Rating-Fenster" → reiner Index-Range-Scan.
            e.HasIndex(pt => new { pt.TagId, pt.Rating });
            e.HasOne(pt => pt.Puzzle).WithMany().HasForeignKey(pt => pt.PuzzleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(pt => pt.Tag).WithMany(t => t.PuzzleTags).HasForeignKey(pt => pt.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PuzzleAttempt>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Puzzle)
             .WithMany()
             .HasForeignKey(a => a.PuzzleId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(a => a.AnonymousSessionId).HasMaxLength(36);
            e.HasIndex(a => new { a.UserId, a.PuzzleId });
            e.HasIndex(a => new { a.UserId, a.VisualizationLevel });
            e.HasIndex(a => a.AnonymousSessionId);
            e.HasIndex(a => a.AttemptedAt).IsDescending();
        });

        modelBuilder.Entity<SharedPuzzleAttempt>(e =>
        {
            // Ein Besucher zählt genau einmal je Puzzle (Erstversuch gewinnt).
            e.HasIndex(a => new { a.BookPuzzleId, a.IdentityKey }).IsUnique();
        });

        modelBuilder.Entity<Book>(e =>
        {
            e.HasIndex(b => b.FileName).IsUnique();
            e.HasIndex(b => b.OwnerUserId);
            // Öffentlicher Kurz-Alias eindeutig (mehrere NULLs erlaubt: MySQL wertet NULL im
            // Unique-Index nicht als gleich → Bücher ohne Alias kollidieren nicht).
            e.HasIndex(b => b.PublicSlug).IsUnique();
        });

        modelBuilder.Entity<Group>(e =>
        {
            e.HasIndex(g => g.Name).IsUnique();
        });

        modelBuilder.Entity<UserGroup>(e =>
        {
            e.HasKey(ug => new { ug.UserId, ug.GroupId });
            e.HasOne(ug => ug.User)
             .WithMany(u => u.Groups)
             .HasForeignKey(ug => ug.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ug => ug.Group)
             .WithMany(g => g.Members)
             .HasForeignKey(ug => ug.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookPuzzle>(e =>
        {
            e.HasIndex(bp => bp.LineId).IsUnique();
            e.HasIndex(bp => bp.BookFileName);
            e.HasIndex(bp => bp.BookId);
            // Pool-Filterung (Daily/Random/Blind) schließt ausgemusterte Puzzles aus.
            e.HasIndex(bp => bp.Retired);

            e.HasOne(bp => bp.Book)
             .WithMany(b => b.Puzzles)
             .HasForeignKey(bp => bp.BookId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookPuzzleAttempt>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // Kein zweiter Cascade-Pfad über BookPuzzle (BookPuzzle wird via Book kaskadiert);
            // Restrict vermeidet den MySQL "multiple cascade paths"-Fehler (analog CoursePuzzleResult).
            e.HasOne(a => a.BookPuzzle)
             .WithMany()
             .HasForeignKey(a => a.BookPuzzleId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(a => a.AnonymousSessionId).HasMaxLength(36);
            e.HasIndex(a => new { a.BookPuzzleId, a.AttemptedAt });
            e.HasIndex(a => new { a.BookPuzzleId, a.UserId });
            // Anonyme Lösungen sind „genau einmal je (Puzzle, Session)" — hart per Unique-Index erzwingen
            // (statt nur check-then-insert), sonst blähen Parallel-Requests AnonymousSolvedCount + Webhooks auf.
            // Authentifizierte Versuche haben AnonymousSessionId = NULL → MySQL erlaubt beliebig viele NULLs,
            // d. h. mehrere Versuche je User bleiben möglich; nur anonyme Sessions sind dedupliziert.
            e.HasIndex(a => new { a.BookPuzzleId, a.AnonymousSessionId }).IsUnique();
        });

        modelBuilder.Entity<EndlessProgress>(e =>
        {
            e.HasOne(ep => ep.User)
             .WithMany(u => u.EndlessProgresses)
             .HasForeignKey(ep => ep.UserId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ep => ep.UserId).IsUnique();
            // Unique je anonymer Session: SaveAnonymousProgressAsync verlässt sich auf einen
            // Konflikt bei Races (DbUpdateException → re-read). MySQL erlaubt mehrere NULLs in
            // einem Unique-Index → die eingeloggten Zeilen (AnonymousSessionId NULL) kollidieren nicht.
            e.HasIndex(ep => ep.AnonymousSessionId).IsUnique();
            e.Property(ep => ep.AnonymousSessionId).HasMaxLength(36);
        });

        modelBuilder.Entity<EndlessSession>(e =>
        {
            e.HasOne(es => es.User)
             .WithMany(u => u.EndlessSessions)
             .HasForeignKey(es => es.UserId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(es => new { es.UserId, es.Timestamp });
            e.HasIndex(es => es.AnonymousSessionId);
            e.Property(es => es.AnonymousSessionId).HasMaxLength(36);
        });

        modelBuilder.Entity<RepertoireCardState>(e =>
        {
            // Karten sterben mit dem Repertoire (Cascade). User-FK bewusst Restrict, sonst zwei
            // Cascade-Pfade zu AppUser (direkt + via Repertoire) → MySQL "multiple cascade paths".
            e.HasOne(c => c.Repertoire)
             .WithMany()
             .HasForeignKey(c => c.RepertoireId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(c => new { c.UserId, c.RepertoireId, c.CardKey }).IsUnique();
            e.HasIndex(c => new { c.UserId, c.RepertoireId, c.DueAt });
        });

        modelBuilder.Entity<RepertoireSrSettings>(e =>
        {
            e.HasOne(s => s.User)
             .WithMany()
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId).IsUnique();
        });

        modelBuilder.Entity<CourseProgress>(e =>
        {
            e.HasOne(cp => cp.User)
             .WithMany(u => u.CourseProgresses)
             .HasForeignKey(cp => cp.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(cp => cp.Book)
             .WithMany()
             .HasForeignKey(cp => cp.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(cp => new { cp.UserId, cp.BookId }).IsUnique();
        });

        modelBuilder.Entity<CoursePuzzleResult>(e =>
        {
            e.HasOne(cr => cr.User)
             .WithMany(u => u.CoursePuzzleResults)
             .HasForeignKey(cr => cr.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(cr => cr.Book)
             .WithMany()
             .HasForeignKey(cr => cr.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            // Kein zusätzlicher Cascade-Pfad über BookPuzzle: BookPuzzle wird bereits via Book
            // kaskadiert; ein zweiter Cascade-Pfad (BookPuzzle -> CoursePuzzleResult) löst in
            // MySQL/MariaDB einen "multiple cascade paths"-Fehler aus. Daher Restrict.
            e.HasOne(cr => cr.BookPuzzle)
             .WithMany()
             .HasForeignKey(cr => cr.BookPuzzleId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(cr => new { cr.UserId, cr.BookPuzzleId }).IsUnique();
            e.HasIndex(cr => new { cr.UserId, cr.BookId });
        });

        modelBuilder.Entity<CoursePin>(e =>
        {
            e.HasOne(p => p.User)
             .WithMany(u => u.CoursePins)
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.Book)
             .WithMany()
             .HasForeignKey(p => p.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            // Ein Pin pro (User, Buch); Sortier-/Ladeindex nach Anpin-Zeitpunkt.
            e.HasIndex(p => new { p.UserId, p.BookId }).IsUnique();
            e.HasIndex(p => new { p.UserId, p.PinnedAt });
        });

        modelBuilder.Entity<UserPushSubscription>(e =>
        {
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.Endpoint).IsUnique();   // eine Subscription je Endpoint
            e.HasIndex(s => s.UserId);
        });

        modelBuilder.Entity<NotificationPushSetting>(e =>
        {
            e.HasKey(s => s.UserId);                  // genau eine Zeile je User
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseShare>(e =>
        {
            // Cascade NUR über das Buch (ein einziger Cascade-Pfad; ein persönlicher Kurs wird beim
            // Löschen samt seiner Freigaben entfernt). Die beiden AppUser-FKs sind Restrict, sonst
            // erzeugt MySQL/MariaDB einen "multiple cascade paths"-Fehler (analog Friendship).
            e.HasOne(cs => cs.Book)
             .WithMany()
             .HasForeignKey(cs => cs.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(cs => cs.Owner)
             .WithMany()
             .HasForeignKey(cs => cs.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(cs => cs.Recipient)
             .WithMany()
             .HasForeignKey(cs => cs.RecipientId)
             .OnDelete(DeleteBehavior.Restrict);

            // Ein Kurs wird an einen Empfänger höchstens einmal geteilt.
            e.HasIndex(cs => new { cs.BookId, cs.RecipientId }).IsUnique();
            // „Welche Kurse sind mit mir geteilt?" (Kursliste/Menü-Sichtbarkeit).
            e.HasIndex(cs => cs.RecipientId);
        });

        modelBuilder.Entity<CourseLink>(e =>
        {
            e.HasOne(l => l.User)
             .WithMany()
             .HasForeignKey(l => l.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // Nur EIN Cascade-Pfad von Book (über BookId); LinkedBookId hat bewusst keinen FK
            // (sonst „multiple cascade paths"). Cleanup der Gegenzeile beim Buch-Löschen erfolgt
            // explizit in BookAdminService.DeleteBookAsync.
            e.HasOne(l => l.Book)
             .WithMany()
             .HasForeignKey(l => l.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            // Ein Buch hat je Nutzer höchstens einen verknüpften Partner.
            e.HasIndex(l => new { l.UserId, l.BookId }).IsUnique();
        });

        modelBuilder.Entity<RepertoireShare>(e =>
        {
            // Cascade NUR über das Repertoire (ein Cascade-Pfad); Owner/Recipient Restrict (analog
            // CourseShare/Friendship) → kein MySQL "multiple cascade paths".
            e.HasOne(rs => rs.Repertoire)
             .WithMany()
             .HasForeignKey(rs => rs.RepertoireId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(rs => rs.Owner)
             .WithMany()
             .HasForeignKey(rs => rs.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(rs => rs.Recipient)
             .WithMany()
             .HasForeignKey(rs => rs.RecipientId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(rs => new { rs.RepertoireId, rs.RecipientId }).IsUnique();
            e.HasIndex(rs => rs.RecipientId);
        });

        modelBuilder.Entity<CourseInfoView>(e =>
        {
            e.HasOne(iv => iv.User)
             .WithMany()
             .HasForeignKey(iv => iv.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(iv => iv.Book)
             .WithMany()
             .HasForeignKey(iv => iv.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            // Kein zweiter Cascade-Pfad über BookPuzzle (wird via Book kaskadiert) → Restrict,
            // analog CoursePuzzleResult/CourseAttempt (MySQL "multiple cascade paths").
            e.HasOne(iv => iv.BookPuzzle)
             .WithMany()
             .HasForeignKey(iv => iv.BookPuzzleId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(iv => new { iv.UserId, iv.BookPuzzleId }).IsUnique();
            e.HasIndex(iv => new { iv.UserId, iv.BookId });
        });

        modelBuilder.Entity<CourseAttempt>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Book)
             .WithMany()
             .HasForeignKey(a => a.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            // Kein zweiter Cascade-Pfad über BookPuzzle (wird via Book kaskadiert) → Restrict,
            // analog CoursePuzzleResult/BookPuzzleAttempt (MySQL "multiple cascade paths").
            e.HasOne(a => a.BookPuzzle)
             .WithMany()
             .HasForeignKey(a => a.BookPuzzleId)
             .OnDelete(DeleteBehavior.Restrict);

            // Fenster-Aggregation je User (AttemptedAt >= windowStart).
            e.HasIndex(a => new { a.UserId, a.AttemptedAt });
        });

        modelBuilder.Entity<ChessableActivity>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(a => a.CourseId).HasMaxLength(32);
            e.Property(a => a.CourseName).HasMaxLength(200);
            // Fenster-Aggregation je User (AttemptedAt >= windowStart), analog CourseAttempt.
            e.HasIndex(a => new { a.UserId, a.AttemptedAt });
        });

        modelBuilder.Entity<ChessableCourseTheme>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.CourseId).HasMaxLength(32);
            e.Property(a => a.CourseName).HasMaxLength(200);
            // Eine Themen-Zuordnung je (User, Kurs) — Upsert-Schlüssel.
            e.HasIndex(a => new { a.UserId, a.CourseId }).IsUnique();
        });

        modelBuilder.Entity<ManualActivity>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.Note).HasMaxLength(200);
            // Fenster-Aggregation je User (Date >= windowStart) für den Tracker.
            e.HasIndex(a => new { a.UserId, a.Date });
        });

        modelBuilder.Entity<ActivityPreset>(e =>
        {
            e.HasOne(p => p.User)
             .WithMany()
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Label).HasMaxLength(100);
            e.HasIndex(p => p.UserId);
        });

        modelBuilder.Entity<ActivityTimer>(e =>
        {
            // PK = UserId → höchstens 1 laufender Timer je User (kein extra Unique nötig).
            e.HasKey(t => t.UserId);
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Label).HasMaxLength(100);
        });

        modelBuilder.Entity<RememberedPosition>(e =>
        {
            e.HasOne(p => p.User)
             .WithMany()
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Fen).HasMaxLength(120);
            e.Property(p => p.CourseId).HasMaxLength(32);
            e.Property(p => p.CourseName).HasMaxLength(200);
            e.Property(p => p.SourceUrl).HasMaxLength(1000);
            e.HasIndex(p => new { p.UserId, p.CreatedAt });
        });

        modelBuilder.Entity<CiBuildReport>(e =>
        {
            e.HasKey(x => x.Repo);
            e.Property(x => x.Repo).HasMaxLength(100);
            e.Property(x => x.Sha).HasMaxLength(64);
            e.Property(x => x.Ref).HasMaxLength(200);
        });

        modelBuilder.Entity<SavedGame>(e =>
        {
            e.HasOne(g => g.User)
             .WithMany()
             .HasForeignKey(g => g.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(g => g.Source).HasMaxLength(20);
            e.Property(g => g.ExternalId).HasMaxLength(120);
            e.Property(g => g.Pgn).HasColumnType("LONGTEXT");
            e.Property(g => g.White).HasMaxLength(120);
            e.Property(g => g.Black).HasMaxLength(120);
            e.Property(g => g.Result).HasMaxLength(12);
            e.Property(g => g.SourceUrl).HasMaxLength(1000);
            e.Property(g => g.ShareToken).HasMaxLength(32);
            e.HasIndex(g => g.ShareToken).IsUnique();
            // Auflistung je User (neueste zuerst).
            e.HasIndex(g => new { g.UserId, g.CreatedAt });
            // Dedup hart auf DB-Ebene erzwingen (statt nur check-then-insert): dieselbe externe Partie kann
            // nicht doppelt gespeichert werden, auch nicht bei parallelem Doppel-Klick. MySQL behandelt
            // NULL-ExternalId als verschieden → mehrere Saves OHNE externe Id (manuell) bleiben erlaubt.
            e.HasIndex(g => new { g.UserId, g.Source, g.ExternalId }).IsUnique();
        });

        modelBuilder.Entity<MenuItemSetting>(e =>
        {
            e.HasKey(s => s.ItemKey);
            e.Property(s => s.ItemKey).HasMaxLength(50);
        });
        modelBuilder.Entity<MenuItemGroupAccess>(e =>
        {
            e.HasKey(a => new { a.ItemKey, a.GroupId });
            e.Property(a => a.ItemKey).HasMaxLength(50);
            e.HasOne(a => a.Setting)
             .WithMany(s => s.Groups)
             .HasForeignKey(a => a.ItemKey)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Group)
             .WithMany()
             .HasForeignKey(a => a.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.GroupId);
        });

        modelBuilder.Entity<BookGroupAccess>(e =>
        {
            e.HasKey(a => new { a.BookId, a.GroupId });
            e.HasOne(a => a.Book)
             .WithMany()
             .HasForeignKey(a => a.BookId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Group)
             .WithMany()
             .HasForeignKey(a => a.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.GroupId);
        });

        modelBuilder.Entity<WeeklyPost>(e =>
        {
            e.HasIndex(w => w.ScheduledAt);
        });

        modelBuilder.Entity<WeeklyPostAttempt>(e =>
        {
            // WeeklyPost und AppUser sind voneinander unabhaengig → kein Multi-Cascade-Pfad,
            // beide FKs koennen cascaden (Post oder User geloescht → Versuche weg).
            e.HasOne(a => a.WeeklyPost)
             .WithMany()
             .HasForeignKey(a => a.WeeklyPostId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            // Ein Puzzle je (Post, User) genau einmal → idempotentes Aufzeichnen (erster Versuch zaehlt).
            e.HasIndex(a => new { a.WeeklyPostId, a.UserId, a.PuzzleIndex }).IsUnique();
            e.HasIndex(a => new { a.WeeklyPostId, a.UserId });
        });

        modelBuilder.Entity<DailyPuzzle>(e =>
        {
            // Date als PK: maximal ein Eintrag pro UTC-Tag, idempotente Insert-Race-Behandlung
            // ueber den DB-eigenen Unique-Constraint.
            e.HasKey(d => d.Date);
            e.HasOne(d => d.BookPuzzle)
             .WithMany()
             .HasForeignKey(d => d.BookPuzzleId)
             .OnDelete(DeleteBehavior.Restrict); // Buch-Loeschung soll Historie nicht killen
        });

        modelBuilder.Entity<UserApiToken>(e =>
        {
            // Unique-Index auf TokenHash → O(1)-Lookup bei jeder Authentifizierung.
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.UserId, t.Name });
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            // Unique-Index auf TokenHash → O(1)-Lookup beim Einloesen.
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupTrainingGoal>(e =>
        {
            // Höchstens eine Vorlage je Gruppe.
            e.HasIndex(g => g.GroupId).IsUnique();
            e.HasOne(g => g.Group)
             .WithMany()
             .HasForeignKey(g => g.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChessableCredential>(e =>
        {
            // 1:1 zu AppUser; Cascade-Delete entfernt den verschluesselten Bearer
            // mit dem User.
            e.HasIndex(c => c.UserId).IsUnique();
            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.EncryptedBearer).HasColumnType("TEXT");
            e.Property(c => c.CachedCoursesJson).HasColumnType("LONGTEXT");
        });

        modelBuilder.Entity<ChessableImport>(e =>
        {
            e.HasIndex(i => new { i.UserId, i.CreatedAt });
            e.HasIndex(i => i.Status);
            e.HasOne(i => i.User)
             .WithMany()
             .HasForeignKey(i => i.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(i => i.FetchedPgn).HasColumnType("LONGTEXT");
        });

        modelBuilder.Entity<UserTrainingGoal>(e =>
        {
            // Höchstens ein persönlicher Override je User.
            e.HasIndex(g => g.UserId).IsUnique();
            e.HasOne(g => g.User)
             .WithMany()
             .HasForeignKey(g => g.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayTimeDaily>(e =>
        {
            e.HasOne(p => p.User)
             .WithMany()
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.UserId, p.Date, p.Platform }).IsUnique();
        });

        modelBuilder.Entity<PlayTimeSync>(e =>
        {
            e.HasOne(p => p.User)
             .WithMany()
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.UserId, p.Platform }).IsUnique();
        });
    }
}
