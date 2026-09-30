using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Turniere: Abos, Favoriten, Runden-Monitor, Turnierverzeichnis, Spielerverlauf, Ortslexikon.

internal sealed class TournamentSubscriptionConfiguration : IEntityTypeConfiguration<TournamentSubscription>
{
    public void Configure(EntityTypeBuilder<TournamentSubscription> e)
    {
        e.HasOne(ts => ts.User)
         .WithMany(u => u.TournamentSubscriptions)
         .HasForeignKey(ts => ts.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(ts => new { ts.UserId, ts.CrawlerTournamentId }).IsUnique();
        e.HasIndex(ts => ts.CrawlerTournamentId);
    }
}

internal sealed class TournamentFavoriteConfiguration : IEntityTypeConfiguration<TournamentFavorite>
{
    public void Configure(EntityTypeBuilder<TournamentFavorite> e)
    {
        e.HasOne(tf => tf.User)
         .WithMany(u => u.TournamentFavorites)
         .HasForeignKey(tf => tf.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(tf => new { tf.UserId, tf.CrawlerTournamentId, tf.PlayerSnr }).IsUnique();
        e.HasIndex(tf => new { tf.UserId, tf.CrawlerTournamentId, tf.TeamSnr }).IsUnique();
    }
}

internal sealed class TournamentFavoriteDismissalConfiguration : IEntityTypeConfiguration<TournamentFavoriteDismissal>
{
    public void Configure(EntityTypeBuilder<TournamentFavoriteDismissal> e)
    {
        e.HasOne(d => d.User)
         .WithMany()
         .HasForeignKey(d => d.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(d => new { d.UserId, d.CrawlerTournamentId, d.PlayerSnr }).IsUnique();
    }
}

internal sealed class TournamentUserSettingConfiguration : IEntityTypeConfiguration<TournamentUserSetting>
{
    public void Configure(EntityTypeBuilder<TournamentUserSetting> e)
    {
        e.HasOne(s => s.User)
         .WithMany(u => u.TournamentUserSettings)
         .HasForeignKey(s => s.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(s => new { s.UserId, s.CrawlerTournamentId }).IsUnique();
    }
}

internal sealed class TournamentMonitorConfiguration : IEntityTypeConfiguration<TournamentMonitor>
{
    public void Configure(EntityTypeBuilder<TournamentMonitor> e)
    {
        e.HasOne(m => m.User)
         .WithMany()
         .HasForeignKey(m => m.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(m => new { m.UserId, m.CrawlerTournamentId }).IsUnique();
    }
}

internal sealed class TournamentDirectoryEntryConfiguration : IEntityTypeConfiguration<TournamentDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<TournamentDirectoryEntry> e)
    {
        // Die IDENTITAET des Eintrags — Adresse, Ausblenden, Meldung, Teilen-Link.
        e.HasIndex(d => d.PublicId).IsUnique();
        // Die chess-results-Nummer bleibt eindeutig, ist aber seit dem FIDE-Kalender
        // NULLABLE: MySQL erlaubt mehrere NULL in einem Unique-Index, mehrere Eintraege ohne
        // chess-results-Nummer sind damit kein Problem.
        e.HasIndex(d => d.ChessResultsId).IsUnique();
        // Kalender-/Listenabfragen laufen ueber das Enddatum; der Sweep zusaetzlich je Foederation.
        e.HasIndex(d => d.EndDate);
        e.HasIndex(d => new { d.Federation, d.EndDate });
        // Vorfilter der Umkreissuche: Bounding-Box auf Lat/Lon, exakte Distanz danach in C#.
        e.HasIndex(d => new { d.Lat, d.Lon });
        // Listen- und Kalenderabfragen gruppieren darueber.
        e.HasIndex(d => d.GroupKey);
    }
}

internal sealed class TournamentDirectoryVenueConfiguration : IEntityTypeConfiguration<TournamentDirectoryVenue>
{
    public void Configure(EntityTypeBuilder<TournamentDirectoryVenue> e)
    {
        e.HasOne(v => v.Entry)
         .WithMany(d => d.Venues)
         .HasForeignKey(v => v.TournamentDirectoryEntryId)
         .OnDelete(DeleteBehavior.Cascade);
        // Der Vorfilter der Umkreissuche laeuft jetzt HIER: ein Turnier gilt als in der Naehe,
        // wenn EINER seiner Spielorte in der Box liegt.
        e.HasIndex(v => new { v.Lat, v.Lon });
        e.HasIndex(v => new { v.TournamentDirectoryEntryId, v.Ordinal });
    }
}

internal sealed class TournamentDirectoryRoundConfiguration : IEntityTypeConfiguration<TournamentDirectoryRound>
{
    public void Configure(EntityTypeBuilder<TournamentDirectoryRound> e)
    {
        e.HasOne(r => r.Entry)
         .WithMany(d => d.RoundDates)
         .HasForeignKey(r => r.TournamentDirectoryEntryId)
         .OnDelete(DeleteBehavior.Cascade);
        // Der Kalender fragt „welche Turniere spielen an DIESEM Tag" — mit dem Datum vorn
        // laeuft das ueber den Index statt ueber einen Scan aller Spieltermine.
        e.HasIndex(r => r.Date);
        e.HasIndex(r => new { r.TournamentDirectoryEntryId, r.Number }).IsUnique();
    }
}

internal sealed class UserViewStateConfiguration : IEntityTypeConfiguration<UserViewState>
{
    public void Configure(EntityTypeBuilder<UserViewState> e)
    {
        // Ein Zustand je Nutzer UND Ansicht — der Upsert haengt daran.
        e.HasIndex(v => new { v.UserId, v.ViewKey }).IsUnique();
        e.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TournamentDirectorySourceConfiguration : IEntityTypeConfiguration<TournamentDirectorySource>
{
    public void Configure(EntityTypeBuilder<TournamentDirectorySource> e)
    {
        e.HasOne(x => x.Entry)
         .WithMany(d => d.Sources)
         .HasForeignKey(x => x.TournamentDirectoryEntryId)
         .OnDelete(DeleteBehavior.Cascade);
        // Eine Nummer gehoert zu genau EINEM Turnier — sonst waere beim Zusammenfuehren
        // zweier Quellen nicht entscheidbar, welcher Eintrag gemeint ist.
        e.HasIndex(x => new { x.Kind, x.ExternalId }).IsUnique();
        e.HasIndex(x => x.TournamentDirectoryEntryId);
    }
}

internal sealed class TournamentDirectoryIgnoreConfiguration : IEntityTypeConfiguration<TournamentDirectoryIgnore>
{
    public void Configure(EntityTypeBuilder<TournamentDirectoryIgnore> e)
    {
        e.HasOne(i => i.User)
         .WithMany()
         .HasForeignKey(i => i.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        // Zweimal ausblenden ist dasselbe wie einmal.
        e.HasIndex(i => new { i.UserId, i.PublicId }).IsUnique();
    }
}

internal sealed class PlayerTournamentResultConfiguration : IEntityTypeConfiguration<PlayerTournamentResult>
{
    public void Configure(EntityTypeBuilder<PlayerTournamentResult> e)
    {
        // Der Schluessel ist der SPIELER, nicht das Konto: die Historie ist fuer jeden
        // dieselbe, und ein Freund soll denselben Zwischenspeicher benutzen.
        e.HasIndex(r => new { r.PlayerKey, r.ChessResultsId }).IsUnique();
        e.HasIndex(r => new { r.PlayerKey, r.EndDate });
        e.Property(r => r.Points).HasPrecision(5, 2);
        e.Property(r => r.RatingChange).HasPrecision(6, 2);
    }
}

internal sealed class PlayerHistorySyncConfiguration : IEntityTypeConfiguration<PlayerHistorySync>
{
    public void Configure(EntityTypeBuilder<PlayerHistorySync> e)
    {
        e.HasKey(x => x.PlayerKey);
    }
}

internal sealed class TrackedPlayerConfiguration : IEntityTypeConfiguration<TrackedPlayer>
{
    public void Configure(EntityTypeBuilder<TrackedPlayer> e)
    {
        e.HasOne(t => t.User)
         .WithMany()
         .HasForeignKey(t => t.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        // Denselben Spieler zweimal zu verfolgen ergaebe zwei Reiter mit derselben Tabelle.
        e.HasIndex(t => new { t.UserId, t.PlayerKey }).IsUnique();
    }
}

// Die Bedenkzeit gehoert dem TURNIER — ein Abruf, den sich alle Konten teilen.
internal sealed class TournamentTimeControlConfiguration : IEntityTypeConfiguration<TournamentTimeControl>
{
    public void Configure(EntityTypeBuilder<TournamentTimeControl> e)
    {
        e.HasKey(x => x.ChessResultsId);
    }
}

internal sealed class HistoryTournamentCrawlConfiguration : IEntityTypeConfiguration<HistoryTournamentCrawl>
{
    public void Configure(EntityTypeBuilder<HistoryTournamentCrawl> e)
    {
        e.HasKey(x => x.ChessResultsId);
    }
}

internal sealed class TournamentSearchProfileConfiguration : IEntityTypeConfiguration<TournamentSearchProfile>
{
    public void Configure(EntityTypeBuilder<TournamentSearchProfile> e)
    {
        e.HasOne(p => p.User)
         .WithMany(u => u.TournamentSearchProfiles)
         .HasForeignKey(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
    }
}

internal sealed class GeoPlaceConfiguration : IEntityTypeConfiguration<GeoPlace>
{
    public void Configure(EntityTypeBuilder<GeoPlace> e)
    {
        e.HasIndex(g => new { g.Country, g.PostalCode });
        e.HasIndex(g => new { g.Country, g.NameNormalized });
        e.HasIndex(g => g.NameNormalized);
        // Die zweite Schreibweise wird genauso gesucht wie die erste (ue-Umschrift,
        // Kyrillisch/Griechisch) — ohne Index waere jede Ortsauflösung ein Tabellenscan
        // ueber ein Lexikon mit sechsstelliger Zeilenzahl.
        e.HasIndex(g => new { g.Country, g.NameTranscribed });
        e.HasIndex(g => g.NameTranscribed);
    }
}
