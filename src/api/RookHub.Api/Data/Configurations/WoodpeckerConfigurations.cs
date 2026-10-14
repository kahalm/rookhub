using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Woodpecker-Training: Plan über Kurse, Zyklen, Satz und Versuche je Zyklus.
// Löschen: Plan geht mit dem Nutzer, alles andere mit dem Plan; Kurs löschen nimmt Kurs-Zeile, Satz und Versuche
// über BookId mit. Der FK auf BookPuzzle ist Restrict (wie CourseAttempt) und wird in BookPuzzleDependents abgeräumt.

internal sealed class WoodpeckerPlanConfiguration : IEntityTypeConfiguration<WoodpeckerPlan>
{
    public void Configure(EntityTypeBuilder<WoodpeckerPlan> e)
    {
        e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(p => p.UserId);
    }
}

internal sealed class WoodpeckerPlanBookConfiguration : IEntityTypeConfiguration<WoodpeckerPlanBook>
{
    public void Configure(EntityTypeBuilder<WoodpeckerPlanBook> e)
    {
        e.HasOne(b => b.Plan).WithMany(p => p.Books).HasForeignKey(b => b.PlanId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(b => b.Book).WithMany().HasForeignKey(b => b.BookId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(b => new { b.PlanId, b.BookId }).IsUnique();
    }
}

internal sealed class WoodpeckerCycleConfiguration : IEntityTypeConfiguration<WoodpeckerCycle>
{
    public void Configure(EntityTypeBuilder<WoodpeckerCycle> e)
    {
        e.HasOne(c => c.Plan).WithMany(p => p.Cycles).HasForeignKey(c => c.PlanId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(c => new { c.PlanId, c.Number }).IsUnique();
    }
}

internal sealed class WoodpeckerSetItemConfiguration : IEntityTypeConfiguration<WoodpeckerSetItem>
{
    public void Configure(EntityTypeBuilder<WoodpeckerSetItem> e)
    {
        e.HasOne(i => i.Plan).WithMany().HasForeignKey(i => i.PlanId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(i => i.Book).WithMany().HasForeignKey(i => i.BookId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(i => i.BookPuzzle).WithMany().HasForeignKey(i => i.BookPuzzleId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(i => new { i.PlanId, i.BookPuzzleId }).IsUnique();
    }
}

internal sealed class WoodpeckerAttemptConfiguration : IEntityTypeConfiguration<WoodpeckerAttempt>
{
    public void Configure(EntityTypeBuilder<WoodpeckerAttempt> e)
    {
        e.HasOne(a => a.Plan).WithMany().HasForeignKey(a => a.PlanId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Book).WithMany().HasForeignKey(a => a.BookId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.BookPuzzle).WithMany().HasForeignKey(a => a.BookPuzzleId).OnDelete(DeleteBehavior.Restrict);
        // Je Zyklus zählt nur der ERSTE Versuch an einer Aufgabe.
        e.HasIndex(a => new { a.PlanId, a.Cycle, a.BookPuzzleId }).IsUnique();
    }
}
