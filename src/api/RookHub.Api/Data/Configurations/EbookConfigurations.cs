using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// E-Book-Bibliothek: Titel, Dateien je Format, Gruppen-Freigabe je Buch, Lesestand je Nutzer.

internal sealed class EbookConfiguration : IEntityTypeConfiguration<Ebook>
{
    public void Configure(EntityTypeBuilder<Ebook> e)
    {
        e.HasIndex(b => b.OwnerUserId);
        e.HasIndex(b => b.Title);
        // „Welche Bücher braucht der Cover-Worker noch?" — ältestes zuerst.
        e.HasIndex(b => new { b.CoverState, b.Id });
        e.HasOne(b => b.Owner)
         .WithMany()
         .HasForeignKey(b => b.OwnerUserId)
         // Konto weg → das Buch bleibt in der Bibliothek, nur noch Admins verwalten es.
         .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class EbookFileConfiguration : IEntityTypeConfiguration<EbookFile>
{
    public void Configure(EntityTypeBuilder<EbookFile> e)
    {
        e.HasOne(f => f.Ebook)
         .WithMany(b => b.Files)
         .HasForeignKey(f => f.EbookId)
         .OnDelete(DeleteBehavior.Cascade);
        // Ein Pfad gehört genau EINEM Buch — der Bibliotheks-Import prüft darüber, was schon eingelesen ist.
        e.HasIndex(f => f.RelativePath).IsUnique();
        // Doppelt hochgeladene Datei erkennen (nur Uploads tragen einen Hash; NULLs kollidieren nicht).
        e.HasIndex(f => f.Sha256);
    }
}

internal sealed class EbookGroupAccessConfiguration : IEntityTypeConfiguration<EbookGroupAccess>
{
    public void Configure(EntityTypeBuilder<EbookGroupAccess> e)
    {
        e.HasKey(a => new { a.EbookId, a.GroupId });
        e.HasOne(a => a.Ebook)
         .WithMany(b => b.GroupAccess)
         .HasForeignKey(a => a.EbookId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Group)
         .WithMany()
         .HasForeignKey(a => a.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(a => a.GroupId);
    }
}

internal sealed class EbookProgressConfiguration : IEntityTypeConfiguration<EbookProgress>
{
    public void Configure(EntityTypeBuilder<EbookProgress> e)
    {
        e.HasKey(p => new { p.EbookId, p.UserId });
        e.HasOne(p => p.Ebook)
         .WithMany()
         .HasForeignKey(p => p.EbookId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(p => p.User)
         .WithMany()
         .HasForeignKey(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        // „Zuletzt gelesen" des Nutzers in der Übersicht.
        e.HasIndex(p => new { p.UserId, p.UpdatedAt });
    }
}
