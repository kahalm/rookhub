using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Kalkulations-Modus und Kalk-Serie: Analysebaum, Ausgaben, Verteiler, Betrachter.

internal sealed class CalculationTreeConfiguration : IEntityTypeConfiguration<CalculationTree>
{
    public void Configure(EntityTypeBuilder<CalculationTree> e)
    {
        e.HasOne(ct => ct.User)
         .WithMany()
         .HasForeignKey(ct => ct.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(ct => ct.Book)
         .WithMany()
         .HasForeignKey(ct => ct.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Wie bei CoursePuzzleResult: BookPuzzle hängt schon via Book am Cascade — ein zweiter
        // Cascade-Pfad wäre in MySQL/MariaDB ein "multiple cascade paths"-Fehler. Daher Restrict.
        e.HasOne(ct => ct.BookPuzzle)
         .WithMany()
         .HasForeignKey(ct => ct.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        e.Property(ct => ct.TreeJson).HasColumnType("LONGTEXT");
        e.Property(ct => ct.ChosenSan).HasMaxLength(20);
        e.Property(ct => ct.ChosenUci).HasMaxLength(10);
        // Rechenzeit/Punkte/Festlegung bewusst als SPALTEN neben dem opaken TreeJson: im
        // Baum-JSON vergraben wären sie für den Server für immer unabfragbar — als Spalten
        // lassen sich Kapitel- und Kurssummen direkt in der DB rechnen.
        e.Property(ct => ct.SecondsSpent).HasDefaultValue(0);
        // Idempotenz-Marke des zuletzt verbuchten Zeit-Deltas (+ wie viel darunter schon zählte):
        // die Zeit kommt als Delta und wird ADDIERT, ein Retry darf sie nicht doppelt buchen.
        e.Property(ct => ct.SecondsToken).HasMaxLength(64);
        e.Property(ct => ct.SecondsTokenApplied).HasDefaultValue(0);
        e.HasIndex(ct => new { ct.UserId, ct.BookPuzzleId }).IsUnique();
        e.HasIndex(ct => new { ct.UserId, ct.BookId });
    }
}

internal sealed class CalcEditionConfiguration : IEntityTypeConfiguration<CalcEdition>
{
    public void Configure(EntityTypeBuilder<CalcEdition> e)
    {
        e.HasOne(x => x.Book)
         .WithMany()
         .HasForeignKey(x => x.BookId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(x => x.Chapter).HasMaxLength(300);
        e.Property(x => x.Title).HasMaxLength(300);
        e.Property(x => x.VideoUrl).HasMaxLength(500);
        // Eine Ausgabe je Buch+Kapitel (Upsert-Schlüssel).
        e.HasIndex(x => new { x.BookId, x.Chapter }).IsUnique();
    }
}

internal sealed class CalcSeriesMemberConfiguration : IEntityTypeConfiguration<CalcSeriesMember>
{
    public void Configure(EntityTypeBuilder<CalcSeriesMember> e)
    {
        e.HasOne(x => x.Book)
         .WithMany()
         .HasForeignKey(x => x.BookId)
         .OnDelete(DeleteBehavior.Cascade);
        // Ein Nutzer steht je Buch höchstens einmal im Verteiler.
        e.HasIndex(x => new { x.BookId, x.UserId }).IsUnique();
    }
}

internal sealed class CalcEditionViewConfiguration : IEntityTypeConfiguration<CalcEditionView>
{
    public void Configure(EntityTypeBuilder<CalcEditionView> e)
    {
        e.HasOne(x => x.Edition)
         .WithMany()
         .HasForeignKey(x => x.CalcEditionId)
         .OnDelete(DeleteBehavior.Cascade);
        // Ein Mitglied „sieht" eine Ausgabe höchstens einmal.
        e.HasIndex(x => new { x.CalcEditionId, x.UserId }).IsUnique();
    }
}
