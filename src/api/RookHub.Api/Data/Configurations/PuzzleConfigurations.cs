using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Puzzles (Lichess), Versuche, Favoriten, Aufgabenblätter, Endlos-Modus, Tages- und Wochenpuzzle, KidHub.

internal sealed class FavoritePuzzleConfiguration : IEntityTypeConfiguration<FavoritePuzzle>
{
    public void Configure(EntityTypeBuilder<FavoritePuzzle> e)
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
    }
}

internal sealed class WorksheetConfiguration : IEntityTypeConfiguration<Worksheet>
{
    public void Configure(EntityTypeBuilder<Worksheet> e)
    {
        e.HasOne(w => w.User)
         .WithMany()
         .HasForeignKey(w => w.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(w => w.Name).HasMaxLength(120);
        e.Property(w => w.ShareToken).HasMaxLength(32);
        e.Property(w => w.Themes).HasMaxLength(300);

        // Übersicht: Zwischenablage zuerst, dann nach letzter Änderung.
        e.HasIndex(w => new { w.UserId, w.IsClipboard });
        // Einstieg des öffentlichen Links (/w/{token}); NULL = nicht geteilt (MariaDB lässt
        // beliebig viele NULLs im Unique-Index zu).
        e.HasIndex(w => w.ShareToken).IsUnique();
    }
}

internal sealed class WorksheetItemConfiguration : IEntityTypeConfiguration<WorksheetItem>
{
    public void Configure(EntityTypeBuilder<WorksheetItem> e)
    {
        e.HasOne(i => i.Worksheet)
         .WithMany(w => w.Items)
         .HasForeignKey(i => i.WorksheetId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(i => i.Fen).HasMaxLength(120);
        e.Property(i => i.Orientation).HasMaxLength(5);
        e.Property(i => i.Heading).HasMaxLength(200);
        e.Property(i => i.Text).HasMaxLength(2000);
        e.Property(i => i.SolutionMoves).HasMaxLength(1000);
        e.Property(i => i.SourceThemes).HasMaxLength(200);

        // Reihenfolge auf dem Blatt (Lesen + Umsortieren gehen immer über sie).
        e.HasIndex(i => new { i.WorksheetId, i.SortOrder });

        // SourceId ist polymorph (Puzzles ODER BookPuzzles, je nach Source) → bewusst KEIN FK:
        // ein gelöschtes/neu importiertes Puzzle darf ein fertiges Blatt nicht anfassen.
    }
}

internal sealed class PuzzleConfiguration : IEntityTypeConfiguration<Puzzle>
{
    public void Configure(EntityTypeBuilder<Puzzle> e)
    {
        e.HasIndex(p => p.LichessId).IsUnique();
        e.HasIndex(p => p.Rating);
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> e)
    {
        e.HasIndex(t => t.Name).IsUnique();
    }
}

internal sealed class PuzzleTagConfiguration : IEntityTypeConfiguration<PuzzleTag>
{
    public void Configure(EntityTypeBuilder<PuzzleTag> e)
    {
        e.HasKey(pt => new { pt.PuzzleId, pt.TagId });
        // Killer-Index: „Puzzles mit Thema X im Rating-Fenster" → reiner Index-Range-Scan.
        e.HasIndex(pt => new { pt.TagId, pt.Rating });
        e.HasOne(pt => pt.Puzzle).WithMany().HasForeignKey(pt => pt.PuzzleId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(pt => pt.Tag).WithMany(t => t.PuzzleTags).HasForeignKey(pt => pt.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class KidsPuzzleConfiguration : IEntityTypeConfiguration<KidsPuzzle>
{
    public void Configure(EntityTypeBuilder<KidsPuzzle> e)
    {
        // Ein Puzzle steht hoechstens EINMAL in der Leiter.
        e.HasKey(k => k.PuzzleId);
        e.Property(k => k.Theme).HasMaxLength(20);
        e.HasIndex(k => new { k.Level, k.Position });
        e.HasOne(k => k.Puzzle).WithMany().HasForeignKey(k => k.PuzzleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class KidsLevelProgressConfiguration : IEntityTypeConfiguration<KidsLevelProgress>
{
    public void Configure(EntityTypeBuilder<KidsLevelProgress> e)
    {
        e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(p => new { p.UserId, p.Level }).IsUnique();
    }
}

internal sealed class KidsCourseProgressConfiguration : IEntityTypeConfiguration<KidsCourseProgress>
{
    public void Configure(EntityTypeBuilder<KidsCourseProgress> e)
    {
        e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(p => p.Book).WithMany().HasForeignKey(p => p.BookId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(p => new { p.UserId, p.BookId }).IsUnique();
    }
}

internal sealed class KidsCourseLineConfiguration : IEntityTypeConfiguration<KidsCourseLine>
{
    public void Configure(EntityTypeBuilder<KidsCourseLine> e)
    {
        e.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(l => l.Book).WithMany().HasForeignKey(l => l.BookId).OnDelete(DeleteBehavior.Cascade);
        // Bewusst kein FK auf BookPuzzle (siehe KidsCourseLine.BookPuzzleId).
        e.HasIndex(l => new { l.UserId, l.BookPuzzleId }).IsUnique();
        e.HasIndex(l => new { l.UserId, l.BookId });
    }
}

internal sealed class PuzzleAttemptConfiguration : IEntityTypeConfiguration<PuzzleAttempt>
{
    public void Configure(EntityTypeBuilder<PuzzleAttempt> e)
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
        // KEINE Modus-Spalte: die Spielweise folgt hier vollständig aus VisualizationLevel
        // (0 = "easy", > 0 = "training") und wird deshalb abgeleitet statt gespeichert.
        e.HasIndex(a => new { a.UserId, a.PuzzleId });
        e.HasIndex(a => new { a.UserId, a.VisualizationLevel });
        e.HasIndex(a => a.AnonymousSessionId);
        e.HasIndex(a => a.AttemptedAt).IsDescending();
    }
}

internal sealed class SharedPuzzleAttemptConfiguration : IEntityTypeConfiguration<SharedPuzzleAttempt>
{
    public void Configure(EntityTypeBuilder<SharedPuzzleAttempt> e)
    {
        // Ein Besucher zählt genau einmal je Puzzle (Erstversuch gewinnt).
        e.HasIndex(a => new { a.BookPuzzleId, a.IdentityKey }).IsUnique();
    }
}

internal sealed class EndlessProgressConfiguration : IEntityTypeConfiguration<EndlessProgress>
{
    public void Configure(EntityTypeBuilder<EndlessProgress> e)
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
    }
}

internal sealed class EndlessSessionConfiguration : IEntityTypeConfiguration<EndlessSession>
{
    public void Configure(EntityTypeBuilder<EndlessSession> e)
    {
        e.HasOne(es => es.User)
         .WithMany(u => u.EndlessSessions)
         .HasForeignKey(es => es.UserId)
         .IsRequired(false)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(es => new { es.UserId, es.Timestamp });
        e.HasIndex(es => es.AnonymousSessionId);
        e.Property(es => es.AnonymousSessionId).HasMaxLength(36);
        // Spielweise des Laufs; DB-Default "training" → Altbestand gilt als Trainings-Modus.
        e.Property(es => es.Mode).HasMaxLength(10).HasDefaultValue(SolveMode.Training);
    }
}

internal sealed class WeeklyPostConfiguration : IEntityTypeConfiguration<WeeklyPost>
{
    public void Configure(EntityTypeBuilder<WeeklyPost> e)
    {
        e.HasIndex(w => w.ScheduledAt);
    }
}

internal sealed class WeeklyPostAttemptConfiguration : IEntityTypeConfiguration<WeeklyPostAttempt>
{
    public void Configure(EntityTypeBuilder<WeeklyPostAttempt> e)
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
        // Spielmodus des Versuchs; DB-Default "training" → Altbestand gilt als Trainings-Modus.
        e.Property(a => a.Mode).HasMaxLength(10).HasDefaultValue(SolveMode.Training);
        // Ein Puzzle je (Post, User) genau einmal → idempotentes Aufzeichnen (erster Versuch zaehlt).
        e.HasIndex(a => new { a.WeeklyPostId, a.UserId, a.PuzzleIndex }).IsUnique();
        e.HasIndex(a => new { a.WeeklyPostId, a.UserId });
    }
}

internal sealed class DailyPuzzleConfiguration : IEntityTypeConfiguration<DailyPuzzle>
{
    public void Configure(EntityTypeBuilder<DailyPuzzle> e)
    {
        // Date als PK: maximal ein Eintrag pro UTC-Tag, idempotente Insert-Race-Behandlung
        // ueber den DB-eigenen Unique-Constraint.
        e.HasKey(d => d.Date);
        e.HasOne(d => d.BookPuzzle)
         .WithMany()
         .HasForeignKey(d => d.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict); // Buch-Loeschung soll Historie nicht killen
    }
}
