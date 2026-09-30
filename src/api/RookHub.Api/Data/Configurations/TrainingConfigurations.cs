using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Trainingsziele und Zeiterfassung: manuelle Aktivitäten, Timer, Ziele, Spielzeit.

internal sealed class ManualActivityConfiguration : IEntityTypeConfiguration<ManualActivity>
{
    public void Configure(EntityTypeBuilder<ManualActivity> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(a => a.Note).HasMaxLength(200);
        // Fenster-Aggregation je User (Date >= windowStart) für den Tracker.
        e.HasIndex(a => new { a.UserId, a.Date });
    }
}

internal sealed class ActivityPresetConfiguration : IEntityTypeConfiguration<ActivityPreset>
{
    public void Configure(EntityTypeBuilder<ActivityPreset> e)
    {
        e.HasOne(p => p.User)
         .WithMany()
         .HasForeignKey(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(p => p.Label).HasMaxLength(100);
        e.HasIndex(p => p.UserId);
    }
}

internal sealed class ActivityTimerConfiguration : IEntityTypeConfiguration<ActivityTimer>
{
    public void Configure(EntityTypeBuilder<ActivityTimer> e)
    {
        // PK = UserId → höchstens 1 laufender Timer je User (kein extra Unique nötig).
        e.HasKey(t => t.UserId);
        e.HasOne(t => t.User)
         .WithMany()
         .HasForeignKey(t => t.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(t => t.Label).HasMaxLength(100);
    }
}

internal sealed class GroupTrainingGoalConfiguration : IEntityTypeConfiguration<GroupTrainingGoal>
{
    public void Configure(EntityTypeBuilder<GroupTrainingGoal> e)
    {
        // Höchstens eine Vorlage je Gruppe.
        e.HasIndex(g => g.GroupId).IsUnique();
        e.HasOne(g => g.Group)
         .WithMany()
         .HasForeignKey(g => g.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserTrainingGoalConfiguration : IEntityTypeConfiguration<UserTrainingGoal>
{
    public void Configure(EntityTypeBuilder<UserTrainingGoal> e)
    {
        // Höchstens ein persönlicher Override je User.
        e.HasIndex(g => g.UserId).IsUnique();
        e.HasOne(g => g.User)
         .WithMany()
         .HasForeignKey(g => g.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PlayTimeDailyConfiguration : IEntityTypeConfiguration<PlayTimeDaily>
{
    public void Configure(EntityTypeBuilder<PlayTimeDaily> e)
    {
        e.HasOne(p => p.User)
         .WithMany()
         .HasForeignKey(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(p => new { p.UserId, p.Date, p.Platform }).IsUnique();
    }
}

internal sealed class PlayTimeSyncConfiguration : IEntityTypeConfiguration<PlayTimeSync>
{
    public void Configure(EntityTypeBuilder<PlayTimeSync> e)
    {
        e.HasOne(p => p.User)
         .WithMany()
         .HasForeignKey(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(p => new { p.UserId, p.Platform }).IsUnique();
    }
}
