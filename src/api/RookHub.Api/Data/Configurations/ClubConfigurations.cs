using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// ClubHub (Kartei der Kinder und Jugendlichen): Mitglieder, Kontakte, Gruppen, Trainer, Einheiten, Anwesenheit, Notizen.

internal sealed class ClubMemberConfiguration : IEntityTypeConfiguration<ClubMember>
{
    public void Configure(EntityTypeBuilder<ClubMember> e)
    {
        e.HasOne(m => m.LinkedUser).WithMany().HasForeignKey(m => m.LinkedUserId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(m => m.LinkedUserId).IsUnique();
        e.HasIndex(m => m.LinkCode).IsUnique();
        e.HasIndex(m => new { m.LastName, m.FirstName });
    }
}

internal sealed class ClubContactConfiguration : IEntityTypeConfiguration<ClubContact>
{
    public void Configure(EntityTypeBuilder<ClubContact> e)
    {
        e.HasOne(c => c.Member).WithMany(m => m.Contacts).HasForeignKey(c => c.MemberId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(c => new { c.MemberId, c.Position });
    }
}

internal sealed class ClubGroupMemberConfiguration : IEntityTypeConfiguration<ClubGroupMember>
{
    public void Configure(EntityTypeBuilder<ClubGroupMember> e)
    {
        e.HasKey(gm => new { gm.GroupId, gm.MemberId });
        e.HasOne(gm => gm.Group).WithMany(g => g.Members).HasForeignKey(gm => gm.GroupId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(gm => gm.Member).WithMany(m => m.Groups).HasForeignKey(gm => gm.MemberId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(gm => gm.MemberId);
    }
}

internal sealed class ClubGroupTrainerConfiguration : IEntityTypeConfiguration<ClubGroupTrainer>
{
    public void Configure(EntityTypeBuilder<ClubGroupTrainer> e)
    {
        e.HasKey(gt => new { gt.GroupId, gt.UserId });
        e.HasOne(gt => gt.Group).WithMany(g => g.Trainers).HasForeignKey(gt => gt.GroupId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(gt => gt.User).WithMany().HasForeignKey(gt => gt.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(gt => gt.UserId);
    }
}

internal sealed class ClubSessionConfiguration : IEntityTypeConfiguration<ClubSession>
{
    public void Configure(EntityTypeBuilder<ClubSession> e)
    {
        e.Property(s => s.Notes).HasColumnType("text");
        e.HasOne(s => s.Group).WithMany(g => g.Sessions).HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(s => new { s.GroupId, s.Date }).IsUnique();
    }
}

internal sealed class ClubSessionPhotoConfiguration : IEntityTypeConfiguration<ClubSessionPhoto>
{
    public void Configure(EntityTypeBuilder<ClubSessionPhoto> e)
    {
        e.Property(p => p.Image).HasColumnType("LONGBLOB");
        e.Property(p => p.Thumb).HasColumnType("MEDIUMBLOB");
        e.HasOne(p => p.Session).WithMany(s => s.Photos).HasForeignKey(p => p.SessionId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(p => p.SessionId);
    }
}

internal sealed class ClubAttendanceConfiguration : IEntityTypeConfiguration<ClubAttendance>
{
    public void Configure(EntityTypeBuilder<ClubAttendance> e)
    {
        e.HasKey(a => new { a.SessionId, a.MemberId });
        e.HasOne(a => a.Session).WithMany(s => s.Attendance).HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Member).WithMany().HasForeignKey(a => a.MemberId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(a => a.MemberId);
    }
}

internal sealed class ClubNoteConfiguration : IEntityTypeConfiguration<ClubNote>
{
    public void Configure(EntityTypeBuilder<ClubNote> e)
    {
        e.Property(n => n.Text).HasColumnType("text");
        e.HasOne(n => n.Member).WithMany(m => m.NoteEntries).HasForeignKey(n => n.MemberId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(n => new { n.MemberId, n.CreatedAt });
    }
}
