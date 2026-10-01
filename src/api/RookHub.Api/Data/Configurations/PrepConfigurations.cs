using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Spielervorbereitung (Prep): Partiebestand aus Megabase + Lumbra. Keine Fremdschlüssel und keine Navigationen —
// EF legte sonst je Fremdschlüssel einen Index an (EventId bräuchte keinen), und jeder kostet bei 15 Mio. Zeilen
// Hunderte Megabyte auf der Platte, auf der auch die Prod-DB liegt. Die Zuordnung hält das Einlesen selbst ein.

internal sealed class PrepPlayerConfiguration : IEntityTypeConfiguration<PrepPlayer>
{
    public void Configure(EntityTypeBuilder<PrepPlayer> e)
    {
        e.Property(p => p.Name).HasMaxLength(120);
        e.Property(p => p.NameKey).HasMaxLength(120);
        e.Property(p => p.FideId).HasMaxLength(16);
        e.HasIndex(p => p.KeyHash).IsUnique();
        // Suche (PrepPlayerSearch): Präfix auf NameKey, sortiert nach Games — mit Games im Index liest sie den Bereich
        // nur im Index, ohne eine Zeile je Kandidat (Namens-Zwilling: NameKey = …, ebenfalls über diesen Index).
        e.HasIndex(p => new { p.NameKey, p.Games });
        e.HasIndex(p => p.FideId);
    }
}

internal sealed class PrepEventConfiguration : IEntityTypeConfiguration<PrepEvent>
{
    public void Configure(EntityTypeBuilder<PrepEvent> e)
    {
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Site).HasMaxLength(200);
        e.HasIndex(x => x.KeyHash).IsUnique();
    }
}

internal sealed class PrepGameConfiguration : IEntityTypeConfiguration<PrepGame>
{
    public void Configure(EntityTypeBuilder<PrepGame> e)
    {
        e.Property(g => g.Round).HasMaxLength(12);
        e.Property(g => g.Eco).HasMaxLength(3);
        e.Property(g => g.Moves).HasColumnType("text");
        // Die Partien EINES Spielers (Karte, Baum, letzte Partien) — der Baum wird im Speicher gebaut, nicht per SQL-Präfix.
        e.HasIndex(g => new { g.WhiteId, g.PlayedOn });
        e.HasIndex(g => new { g.BlackId, g.PlayedOn });
        // Dubletten beim Einlesen.
        e.HasIndex(g => g.MovesHash);
    }
}

internal sealed class PrepImportConfiguration : IEntityTypeConfiguration<PrepImport>
{
    public void Configure(EntityTypeBuilder<PrepImport> e)
    {
        e.Property(i => i.Source).HasMaxLength(16);
        e.Property(i => i.DiscardReasons).HasMaxLength(400);
        e.HasIndex(i => new { i.Source, i.Chunk }).IsUnique();
    }
}
