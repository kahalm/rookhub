using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Trainingsvideos (privat je Konto): Stunden, besprochene Partien, Segmente (Stellung + Kommentar), Fragen, Notizen.
// Die Volltext-Indizes auf SearchText legt die Migration per SQL an (FULLTEXT kennt das Modell nicht).

internal sealed class TrainingLessonConfiguration : IEntityTypeConfiguration<TrainingLesson>
{
    public void Configure(EntityTypeBuilder<TrainingLesson> e)
    {
        e.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        // Re-Import ersetzt den Teil (privat/allgemein) der Stunde desselben Datums für dasselbe Konto.
        e.Property(l => l.Part).HasMaxLength(16);
        e.HasIndex(l => new { l.UserId, l.Date, l.Part }).IsUnique();
        e.Property(l => l.VideoFile).HasMaxLength(200);
        e.Property(l => l.Summary).HasColumnType("text");
        e.Property(l => l.SpeakerTurns).HasColumnType("mediumtext");
    }
}

internal sealed class TrainingLessonGameConfiguration : IEntityTypeConfiguration<TrainingLessonGame>
{
    public void Configure(EntityTypeBuilder<TrainingLessonGame> e)
    {
        e.HasOne(g => g.Lesson).WithMany(l => l.Games).HasForeignKey(g => g.LessonId).OnDelete(DeleteBehavior.Cascade);
        e.Property(g => g.Key).HasMaxLength(32);
        e.Property(g => g.Kind).HasMaxLength(16);
        e.Property(g => g.White).HasMaxLength(120);
        e.Property(g => g.Black).HasMaxLength(120);
        e.Property(g => g.Event).HasMaxLength(200);
        e.Property(g => g.Eco).HasMaxLength(8);
        e.Property(g => g.Result).HasMaxLength(10);
        e.Property(g => g.MatchConfidence).HasMaxLength(8);
        e.Property(g => g.Pgn).HasColumnType("mediumtext");
        e.Property(g => g.AnalysisPgn).HasColumnType("mediumtext");
        // Trainerkommentare an einer RookHub-Partie: alle Stunden, die sie besprechen.
        e.HasIndex(g => g.RookhubGameId);
    }
}

internal sealed class TrainingSegmentConfiguration : IEntityTypeConfiguration<TrainingSegment>
{
    public void Configure(EntityTypeBuilder<TrainingSegment> e)
    {
        e.HasOne(s => s.Lesson).WithMany(l => l.Segments).HasForeignKey(s => s.LessonId).OnDelete(DeleteBehavior.Cascade);
        e.Property(s => s.GameKey).HasMaxLength(32);
        e.Property(s => s.Fen).HasMaxLength(100);
        e.Property(s => s.BoardFen).HasMaxLength(80);
        e.Property(s => s.Move).HasMaxLength(16);
        e.Property(s => s.MoveKind).HasMaxLength(16);
        e.Property(s => s.Tags).HasMaxLength(200);
        e.Property(s => s.Comment).HasColumnType("text");
        e.Property(s => s.Quote).HasColumnType("text");
        e.Property(s => s.Variations).HasColumnType("text");
        e.Property(s => s.SearchText).HasColumnType("mediumtext");
        // Stellungssuche: nur das Figurenfeld, unabhängig von Zugrecht, Rochade und Zählern.
        e.HasIndex(s => s.BoardFen);
        e.HasIndex(s => new { s.LessonId, s.Position });
    }
}

internal sealed class TrainingQuestionConfiguration : IEntityTypeConfiguration<TrainingQuestion>
{
    public void Configure(EntityTypeBuilder<TrainingQuestion> e)
    {
        e.HasOne(q => q.Segment).WithOne(s => s.Question).HasForeignKey<TrainingQuestion>(q => q.SegmentId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(q => q.SegmentId).IsUnique();
        e.Property(q => q.Prompt).HasColumnType("text");
        e.Property(q => q.Answer).HasMaxLength(16);
        e.Property(q => q.AnswerLine).HasColumnType("text");
        e.Property(q => q.Explanation).HasColumnType("text");
    }
}

internal sealed class TrainingNoteConfiguration : IEntityTypeConfiguration<TrainingNote>
{
    public void Configure(EntityTypeBuilder<TrainingNote> e)
    {
        e.HasOne(n => n.Lesson).WithMany(l => l.Notes).HasForeignKey(n => n.LessonId).OnDelete(DeleteBehavior.Cascade);
        e.Property(n => n.GameKey).HasMaxLength(32);
        e.Property(n => n.Category).HasMaxLength(32);
        e.Property(n => n.Text).HasColumnType("text");
        e.Property(n => n.Quote).HasColumnType("text");
        e.Property(n => n.SearchText).HasColumnType("mediumtext");
        e.HasIndex(n => new { n.LessonId, n.Position });
    }
}

internal sealed class TrainingSegmentProgressConfiguration : IEntityTypeConfiguration<TrainingSegmentProgress>
{
    public void Configure(EntityTypeBuilder<TrainingSegmentProgress> e)
    {
        e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(p => p.Lesson).WithMany().HasForeignKey(p => p.LessonId).OnDelete(DeleteBehavior.Cascade);
        e.Property(p => p.SegmentKey).HasMaxLength(100);
        e.HasIndex(p => new { p.UserId, p.LessonId, p.SegmentKey }).IsUnique();
        e.Property(p => p.Status).HasMaxLength(16);
    }
}

internal sealed class TrainingLessonShareConfiguration : IEntityTypeConfiguration<TrainingLessonShare>
{
    public void Configure(EntityTypeBuilder<TrainingLessonShare> e)
    {
        // Eintrag weg (Re-Import ohne diesen Teil, Konto gelöscht) → seine Links mit.
        e.HasOne(s => s.Lesson).WithMany().HasForeignKey(s => s.LessonId).OnDelete(DeleteBehavior.Cascade);
        e.Property(s => s.Token).HasMaxLength(64);
        e.HasIndex(s => s.Token).IsUnique();
    }
}
