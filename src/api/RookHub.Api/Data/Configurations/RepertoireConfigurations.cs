using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Repertoires, Karteikarten, geteilte Linien und alles aus Chessable bzw. der RepCheck-Extension.

internal sealed class RepertoireConfiguration : IEntityTypeConfiguration<Repertoire>
{
    public void Configure(EntityTypeBuilder<Repertoire> e)
    {
        e.HasOne(r => r.User)
         .WithMany(u => u.Repertoires)
         .HasForeignKey(r => r.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RepertoireFileConfiguration : IEntityTypeConfiguration<RepertoireFile>
{
    public void Configure(EntityTypeBuilder<RepertoireFile> e)
    {
        e.HasOne(rf => rf.Repertoire)
         .WithMany(r => r.Files)
         .HasForeignKey(rf => rf.RepertoireId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RepertoireCardStateConfiguration : IEntityTypeConfiguration<RepertoireCardState>
{
    public void Configure(EntityTypeBuilder<RepertoireCardState> e)
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
    }
}

internal sealed class RepertoireSrSettingsConfiguration : IEntityTypeConfiguration<RepertoireSrSettings>
{
    public void Configure(EntityTypeBuilder<RepertoireSrSettings> e)
    {
        e.HasOne(s => s.User)
         .WithMany()
         .HasForeignKey(s => s.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(s => s.UserId).IsUnique();
    }
}

internal sealed class RepertoireShareConfiguration : IEntityTypeConfiguration<RepertoireShare>
{
    public void Configure(EntityTypeBuilder<RepertoireShare> e)
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
    }
}

internal sealed class ChessableActivityConfiguration : IEntityTypeConfiguration<ChessableActivity>
{
    public void Configure(EntityTypeBuilder<ChessableActivity> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(a => a.CourseId).HasMaxLength(32);
        e.Property(a => a.CourseName).HasMaxLength(200);
        // Fenster-Aggregation je User (AttemptedAt >= windowStart), analog CourseAttempt.
        e.HasIndex(a => new { a.UserId, a.AttemptedAt });
    }
}

internal sealed class RepertoireFlashcardMarkConfiguration : IEntityTypeConfiguration<RepertoireFlashcardMark>
{
    public void Configure(EntityTypeBuilder<RepertoireFlashcardMark> e)
    {
        e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(m => m.Repertoire).WithMany().HasForeignKey(m => m.RepertoireId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(m => new { m.UserId, m.RepertoireId, m.LineKey }).IsUnique();
    }
}

internal sealed class ChessableProblemMoveConfiguration : IEntityTypeConfiguration<ChessableProblemMove>
{
    public void Configure(EntityTypeBuilder<ChessableProblemMove> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        // Upsert-Identität: eine Zeile je (User, Kurs, Linie); Abfrage typischerweise je Kurs.
        e.HasIndex(a => new { a.UserId, a.Bid, a.Oid }).IsUnique();
    }
}

internal sealed class ChessableReviewLineConfiguration : IEntityTypeConfiguration<ChessableReviewLine>
{
    public void Configure(EntityTypeBuilder<ChessableReviewLine> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(a => a.Json).HasColumnType("LONGTEXT");
        e.Property(a => a.ChapterTitle).HasMaxLength(300);
        // Upsert-Identität: eine Zeile je (User, Kurs, Linie).
        e.HasIndex(a => new { a.UserId, a.Bid, a.Oid }).IsUnique();
    }
}

internal sealed class AnonymousChessableReviewLineConfiguration : IEntityTypeConfiguration<AnonymousChessableReviewLine>
{
    public void Configure(EntityTypeBuilder<AnonymousChessableReviewLine> e)
    {
        e.Property(a => a.ChessableUid).HasMaxLength(32);
        e.Property(a => a.Bid).HasMaxLength(12);
        e.Property(a => a.Oid).HasMaxLength(32);
        e.Property(a => a.Json).HasColumnType("LONGTEXT");
        e.Property(a => a.ChapterTitle).HasMaxLength(300);
        // Upsert-Identität: eine Zeile je (Chessable-uid, Kurs, Linie). Kein FK (uid ≠ RookHub-User).
        e.HasIndex(a => new { a.ChessableUid, a.Bid, a.Oid }).IsUnique();
        // Claim/Retention scannen nach uid.
        e.HasIndex(a => a.ChessableUid);
    }
}

internal sealed class ChessableSessionMoveConfiguration : IEntityTypeConfiguration<ChessableSessionMove>
{
    public void Configure(EntityTypeBuilder<ChessableSessionMove> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(a => a.MovesJson).HasColumnType("LONGTEXT");
        // Append-Log, KEIN Unique-Index: mehrere Durchläufe derselben Linie = mehrere Zeilen.
        e.HasIndex(a => new { a.UserId, a.Bid, a.Oid });
    }
}

internal sealed class ChessableCourseThemeConfiguration : IEntityTypeConfiguration<ChessableCourseTheme>
{
    public void Configure(EntityTypeBuilder<ChessableCourseTheme> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(a => a.CourseId).HasMaxLength(32);
        e.Property(a => a.CourseName).HasMaxLength(200);
        // Eine Themen-Zuordnung je (User, Kurs) — Upsert-Schlüssel.
        e.HasIndex(a => new { a.UserId, a.CourseId }).IsUnique();
    }
}

internal sealed class RememberedPositionConfiguration : IEntityTypeConfiguration<RememberedPosition>
{
    public void Configure(EntityTypeBuilder<RememberedPosition> e)
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
    }
}

internal sealed class SharedLineConfiguration : IEntityTypeConfiguration<SharedLine>
{
    public void Configure(EntityTypeBuilder<SharedLine> e)
    {
        e.HasOne(s => s.Owner)
         .WithMany()
         .HasForeignKey(s => s.OwnerUserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(s => s.Title).HasMaxLength(200);
        e.Property(s => s.RepertoireName).HasMaxLength(200);
        e.Property(s => s.Pgn).HasColumnType("LONGTEXT");
        e.Property(s => s.LineHash).HasMaxLength(64);
        e.Property(s => s.ShareToken).HasMaxLength(32);
        e.HasIndex(s => s.ShareToken).IsUnique();
        // Dedup: dieselbe Linie desselben Besitzers erneut teilen ⇒ bestehender Link (kein Duplikat).
        e.HasIndex(s => new { s.OwnerUserId, s.LineHash }).IsUnique();
    }
}

internal sealed class ChessableCredentialConfiguration : IEntityTypeConfiguration<ChessableCredential>
{
    public void Configure(EntityTypeBuilder<ChessableCredential> e)
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
    }
}

internal sealed class ChessableImportConfiguration : IEntityTypeConfiguration<ChessableImport>
{
    public void Configure(EntityTypeBuilder<ChessableImport> e)
    {
        e.HasIndex(i => new { i.UserId, i.CreatedAt });
        e.HasIndex(i => i.Status);
        // Zustaende liegen als Enum im Code, aber unveraendert als Zeichenkette in der Datenbank:
        // deshalb braucht die Umstellung KEINE Migration und keine Datenaenderung. Die
        // Spaltentypen werden ausdruecklich festgehalten (Status varchar(255), weil indiziert;
        // Phase longtext), sonst leitete EF aus dem Enum andere ab und verlangte eine Migration.
        e.Property(i => i.Status)
         .HasConversion(v => v.ToWire(), v => ChessableImportStates.ParseStatus(v))
         .HasColumnType("varchar(255)")
         .IsRequired();
        e.Property(i => i.Phase)
         .HasConversion(v => v.ToWire(), v => ChessableImportStates.ParsePhase(v))
         .HasColumnType("longtext")
         .IsRequired();
        e.HasOne(i => i.User)
         .WithMany()
         .HasForeignKey(i => i.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(i => i.FetchedPgn).HasColumnType("LONGTEXT");
    }
}
