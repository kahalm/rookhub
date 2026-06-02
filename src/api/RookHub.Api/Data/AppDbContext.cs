using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;

namespace RookHub.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Repertoire> Repertoires => Set<Repertoire>();
    public DbSet<RepertoireFile> RepertoireFiles => Set<RepertoireFile>();
    public DbSet<TournamentSubscription> TournamentSubscriptions => Set<TournamentSubscription>();
    public DbSet<TournamentFavorite> TournamentFavorites => Set<TournamentFavorite>();
    public DbSet<TournamentUserSetting> TournamentUserSettings => Set<TournamentUserSetting>();
    public DbSet<TournamentMonitor> TournamentMonitors => Set<TournamentMonitor>();
    public DbSet<Puzzle> Puzzles => Set<Puzzle>();
    public DbSet<PuzzleAttempt> PuzzleAttempts => Set<PuzzleAttempt>();
    public DbSet<BookPuzzle> BookPuzzles => Set<BookPuzzle>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<EndlessProgress> EndlessProgresses => Set<EndlessProgress>();
    public DbSet<EndlessSession> EndlessSessions => Set<EndlessSession>();
    public DbSet<CourseProgress> CourseProgresses => Set<CourseProgress>();
    public DbSet<CoursePuzzleResult> CoursePuzzleResults => Set<CoursePuzzleResult>();
    public DbSet<BookGroupAccess> BookGroupAccesses => Set<BookGroupAccess>();
    public DbSet<WeeklyPost> WeeklyPosts => Set<WeeklyPost>();

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

        modelBuilder.Entity<Book>(e =>
        {
            e.HasIndex(b => b.FileName).IsUnique();
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

            e.HasOne(bp => bp.Book)
             .WithMany(b => b.Puzzles)
             .HasForeignKey(bp => bp.BookId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EndlessProgress>(e =>
        {
            e.HasOne(ep => ep.User)
             .WithMany(u => u.EndlessProgresses)
             .HasForeignKey(ep => ep.UserId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ep => ep.UserId).IsUnique();
            e.HasIndex(ep => ep.AnonymousSessionId);
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
    }
}
