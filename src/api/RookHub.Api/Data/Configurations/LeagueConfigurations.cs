using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// LeagueHub (TMM-Aufstellungs-Prognosen): Ligen, Runden, Partien, Spieler, Online-Konten, Teilen.

internal sealed class LeagueTournamentConfiguration : IEntityTypeConfiguration<LeagueTournament>
{
    public void Configure(EntityTypeBuilder<LeagueTournament> e)
    {
        e.HasKey(t => t.Tnr);
        e.Property(t => t.Tnr).ValueGeneratedNever();
        e.Property(t => t.Name).HasMaxLength(200);
        e.Property(t => t.Season).HasMaxLength(10);
        e.Property(t => t.League).HasMaxLength(40);
        e.Property(t => t.Grp).HasMaxLength(40);
        e.Property(t => t.Stage).HasMaxLength(20);
        e.Property(t => t.Start).HasMaxLength(12);
        e.Property(t => t.End).HasMaxLength(12);
        e.HasIndex(t => t.Season);
    }
}

internal sealed class LeagueRoundConfiguration : IEntityTypeConfiguration<LeagueRound>
{
    public void Configure(EntityTypeBuilder<LeagueRound> e)
    {
        e.HasIndex(r => new { r.Tnr, r.Round }).IsUnique();
    }
}

internal sealed class LeagueMatchConfiguration : IEntityTypeConfiguration<LeagueMatch>
{
    public void Configure(EntityTypeBuilder<LeagueMatch> e)
    {
        e.Property(m => m.Home).HasMaxLength(80);
        e.Property(m => m.Away).HasMaxLength(80);
        e.Property(m => m.Date).HasMaxLength(12);
        e.Property(m => m.Time).HasMaxLength(12);
        e.Property(m => m.Venue).HasMaxLength(300);
        e.HasIndex(m => new { m.Tnr, m.Round });
    }
}

internal sealed class LeagueGameConfiguration : IEntityTypeConfiguration<LeagueGame>
{
    public void Configure(EntityTypeBuilder<LeagueGame> e)
    {
        e.Property(g => g.HomeTeam).HasMaxLength(80);
        e.Property(g => g.AwayTeam).HasMaxLength(80);
        e.Property(g => g.HomePlayer).HasMaxLength(120);
        e.Property(g => g.AwayPlayer).HasMaxLength(120);
        e.Property(g => g.HomeTitle).HasMaxLength(10);
        e.Property(g => g.AwayTitle).HasMaxLength(10);
        e.Property(g => g.HomeColor).HasMaxLength(1);
        e.Property(g => g.Result).HasMaxLength(12);
        e.Property(g => g.HomeFide).HasMaxLength(16);
        e.Property(g => g.AwayFide).HasMaxLength(16);
        e.Property(g => g.PgnId).HasMaxLength(20);
        e.HasIndex(g => new { g.Tnr, g.Round });
    }
}

internal sealed class LeaguePlayerConfiguration : IEntityTypeConfiguration<LeaguePlayer>
{
    public void Configure(EntityTypeBuilder<LeaguePlayer> e)
    {
        e.Property(p => p.Team).HasMaxLength(80);
        e.Property(p => p.Title).HasMaxLength(10);
        e.Property(p => p.Name).HasMaxLength(120);
        e.Property(p => p.NameKey).HasMaxLength(120);
        e.Property(p => p.FideId).HasMaxLength(16);
        e.Property(p => p.Fed).HasMaxLength(5);
        e.HasIndex(p => new { p.Tnr, p.Team });
        e.HasIndex(p => p.FideId);
    }
}

internal sealed class LeaguePlayerProfileConfiguration : IEntityTypeConfiguration<LeaguePlayerProfile>
{
    public void Configure(EntityTypeBuilder<LeaguePlayerProfile> e)
    {
        e.HasKey(p => p.FideId);
        e.Property(p => p.FideId).HasMaxLength(16);
        e.Property(p => p.Name).HasMaxLength(120);
    }
}

internal sealed class LeagueOnlineAccountConfiguration : IEntityTypeConfiguration<LeagueOnlineAccount>
{
    public void Configure(EntityTypeBuilder<LeagueOnlineAccount> e)
    {
        e.Property(a => a.FideId).HasMaxLength(16);
        e.Property(a => a.Site).HasMaxLength(20);
        e.Property(a => a.UserName).HasMaxLength(60);
        e.Property(a => a.Url).HasMaxLength(200);
        e.Property(a => a.Confidence).HasMaxLength(20);
        e.Property(a => a.Evidence).HasMaxLength(1000);
        e.Property(a => a.SyncError).HasMaxLength(300);
        e.HasIndex(a => a.FideId);
    }
}

internal sealed class LeagueOnlineGameConfiguration : IEntityTypeConfiguration<LeagueOnlineGame>
{
    public void Configure(EntityTypeBuilder<LeagueOnlineGame> e)
    {
        e.Property(g => g.FideId).HasMaxLength(16);
        e.Property(g => g.ExternalId).HasMaxLength(40);
        e.Property(g => g.Speed).HasMaxLength(16);
        e.Property(g => g.Result).HasMaxLength(8);
        e.Property(g => g.Opponent).HasMaxLength(60);
        e.Property(g => g.Line).HasMaxLength(400);
        e.Property(g => g.Moves).HasColumnType("longtext");
        e.HasOne(g => g.Account).WithMany().HasForeignKey(g => g.AccountId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(g => new { g.AccountId, g.ExternalId }).IsUnique();
        e.HasIndex(g => new { g.FideId, g.White, g.PlayedAt });
    }
}

internal sealed class LeagueAccountSuggestionConfiguration : IEntityTypeConfiguration<LeagueAccountSuggestion>
{
    public void Configure(EntityTypeBuilder<LeagueAccountSuggestion> e)
    {
        e.Property(a => a.FideId).HasMaxLength(16);
        e.Property(a => a.Site).HasMaxLength(20);
        e.Property(a => a.UserName).HasMaxLength(60);
        e.Property(a => a.Url).HasMaxLength(200);
        e.Property(a => a.Evidence).HasMaxLength(500);
        e.Property(a => a.Source).HasMaxLength(16);
        e.Property(a => a.ProfileName).HasMaxLength(120);
        e.Property(a => a.Location).HasMaxLength(120);
        e.HasIndex(a => new { a.FideId, a.Site, a.UserName }).IsUnique();
        e.HasIndex(a => new { a.Status, a.Score });
    }
}

internal sealed class LeagueAccountScanConfiguration : IEntityTypeConfiguration<LeagueAccountScan>
{
    public void Configure(EntityTypeBuilder<LeagueAccountScan> e)
    {
        e.HasKey(a => a.FideId);
        e.Property(a => a.FideId).HasMaxLength(16);
        e.Property(a => a.Note).HasMaxLength(200);
        e.Property(a => a.Federation).HasMaxLength(8);
    }
}

internal sealed class LeagueScoutAccountConfiguration : IEntityTypeConfiguration<LeagueScoutAccount>
{
    public void Configure(EntityTypeBuilder<LeagueScoutAccount> e)
    {
        e.HasKey(a => a.UserName);
        e.Property(a => a.UserName).HasMaxLength(30);
        e.Property(a => a.DisplayName).HasMaxLength(30);
        e.Property(a => a.Teams).HasMaxLength(500);
        e.Property(a => a.PlayedFor).HasMaxLength(200);
        e.Property(a => a.Events).HasMaxLength(500);
        e.Property(a => a.Result).HasMaxLength(300);
        e.HasIndex(a => a.CheckedAt);
    }
}

internal sealed class LeagueSelfReportConfiguration : IEntityTypeConfiguration<LeagueSelfReport>
{
    public void Configure(EntityTypeBuilder<LeagueSelfReport> e)
    {
        e.Property(a => a.FideId).HasMaxLength(16);
        e.Property(a => a.Site).HasMaxLength(20);
        e.Property(a => a.UserName).HasMaxLength(60);
        e.Property(a => a.Source).HasMaxLength(120);
        e.Property(a => a.Team).HasMaxLength(200);
        e.Property(a => a.Reporter).HasMaxLength(60);
        e.Property(a => a.Note).HasMaxLength(200);
        e.HasIndex(a => new { a.Site, a.UserName, a.Source }).IsUnique();
        e.HasIndex(a => a.FideId);
    }
}

internal sealed class LeagueBroadcastConfiguration : IEntityTypeConfiguration<LeagueBroadcast>
{
    public void Configure(EntityTypeBuilder<LeagueBroadcast> e)
    {
        e.HasKey(b => b.TourId);
        e.Property(b => b.TourId).HasMaxLength(12);
        e.Property(b => b.Name).HasMaxLength(200);
        e.Property(b => b.Location).HasMaxLength(200);
        e.Property(b => b.Error).HasMaxLength(300);
        e.HasIndex(b => b.Finished);
    }
}

internal sealed class LeagueShareConfiguration : IEntityTypeConfiguration<LeagueShare>
{
    public void Configure(EntityTypeBuilder<LeagueShare> e)
    {
        e.HasKey(s => s.Token);
        e.Property(s => s.Token).HasMaxLength(40);
        e.Property(s => s.Team).HasMaxLength(80);
        e.HasIndex(s => new { s.Tnr, s.Round, s.Team }).IsUnique();
    }
}

internal sealed class LeagueViewConfiguration : IEntityTypeConfiguration<LeagueView>
{
    public void Configure(EntityTypeBuilder<LeagueView> e)
    {
        e.HasKey(v => v.Tnr);
        e.Property(v => v.Tnr).ValueGeneratedNever();
    }
}

internal sealed class LeagueMegaPlayerConfiguration : IEntityTypeConfiguration<LeagueMegaPlayer>
{
    public void Configure(EntityTypeBuilder<LeagueMegaPlayer> e)
    {
        e.Property(p => p.Name).HasMaxLength(120);
        e.Property(p => p.NameKey).HasMaxLength(120);
        e.Property(p => p.FideId).HasMaxLength(16);
        e.HasIndex(p => p.NameKey);
        e.HasIndex(p => p.FideId);
    }
}

internal sealed class LeagueClubDraftConfiguration : IEntityTypeConfiguration<LeagueClubDraft>
{
    public void Configure(EntityTypeBuilder<LeagueClubDraft> e)
    {
        e.Property(d => d.AccessKey).HasMaxLength(32);
        e.Property(d => d.AnonIpHash).HasMaxLength(64);
        e.Property(d => d.Source).HasMaxLength(16);
        e.Property(d => d.Label).HasMaxLength(300);
        e.Property(d => d.Pgn).HasColumnType("longtext");
        e.Property(d => d.StateJson).HasColumnType("longtext");
        e.Property(d => d.Imported).HasColumnType("text");
        e.HasIndex(d => d.AccessKey).IsUnique();
        e.HasIndex(d => new { d.UserId, d.UpdatedAt });
    }
}

internal sealed class LeagueNameAliasConfiguration : IEntityTypeConfiguration<LeagueNameAlias>
{
    public void Configure(EntityTypeBuilder<LeagueNameAlias> e)
    {
        e.Property(a => a.NameKey).HasMaxLength(120);
        e.Property(a => a.Fide).HasMaxLength(16);
        e.Property(a => a.Name).HasMaxLength(120);
        e.HasIndex(a => a.NameKey).IsUnique();
    }
}

internal sealed class LeagueClubGameConfiguration : IEntityTypeConfiguration<LeagueClubGame>
{
    public void Configure(EntityTypeBuilder<LeagueClubGame> e)
    {
        e.Property(g => g.White).HasMaxLength(120);
        e.Property(g => g.Black).HasMaxLength(120);
        e.Property(g => g.WhiteFide).HasMaxLength(16);
        e.Property(g => g.BlackFide).HasMaxLength(16);
        e.Property(g => g.Result).HasMaxLength(12);
        e.Property(g => g.Event).HasMaxLength(200);
        e.Property(g => g.Pgn).HasColumnType("LONGTEXT");
        e.Property(g => g.MovesHash).HasMaxLength(64);
        e.Property(g => g.UploadShareHash).HasMaxLength(64);
        e.HasIndex(g => g.MovesHash);
        e.HasIndex(g => g.WhiteFide);
        e.HasIndex(g => g.BlackFide);
        e.HasIndex(g => g.UploadShareHash);
    }
}
