using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Regionen gegen ECHTES MariaDB (Zugspitze als dritte Liga-Quelle, 2026-10-07): <see cref="LeagueRegions.InRegion"/> ist für
/// Tirol <c>Source IS NULL</c> und für Bayern <c>Source IN ('ligamanager','zugspitze')</c> — InMemory rechnet beides in C#
/// und sieht nicht, ob der Anbieter die Liste übersetzt. Dazu das Nachfüllen der FIDE-IDs über die Region.
/// </summary>
public class LeagueRegionSqlTests(LeagueRegionSqlFixture fixture) : IAsyncLifetime, IClassFixture<LeagueRegionSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly int Lm = LigamanagerSource.TnrOf(2573), Zg = ZugspitzeSource.TnrOf(2026, 1);
    private const int Cr = 1206271;

    private async Task SeedAsync()
    {
        await using var db = fixture.Schema.NewContext();
        db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = Lm, Name = "LL", Season = "2026/27", Level = 3, League = "Landesliga Süd", Stage = "Liga", Source = LigamanagerSource.Source },
            new LeagueTournament { Tnr = Zg, Name = "ZL", Season = "2026/27", Level = 5, League = "Zugspitzliga", Stage = "Liga", Source = ZugspitzeSource.Source },
            new LeagueTournament { Tnr = Cr, Name = "TMM", Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = Lm, Team = "SK Weilheim 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "90000001" },
            new LeaguePlayer { Tnr = Zg, Team = "SK Weilheim II", Name = "Muster, Max", NameKey = "muster, max" },
            new LeaguePlayer { Tnr = Cr, Team = "Schwaz", Name = "Muster, Max", NameKey = "muster, max" });
        db.LeagueGames.Add(new LeagueGame { Tnr = Zg, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "SK Weilheim II", AwayTeam = "SF Fremd",
            HomePlayer = "Muster, Max", AwayPlayer = "Fremd, F" });
        await db.SaveChangesAsync();
    }

    [MySqlFact]
    public async Task InRegion_TranslatesNullAndListFilters()
    {
        await SeedAsync();
        await using var db = fixture.Schema.NewContext();
        Assert.Equal(new[] { Cr }, await db.LeagueTournaments.InRegion(LeagueRegions.Tirol).Select(t => t.Tnr).ToListAsync());
        Assert.Equal(new[] { Lm, Zg }.Order(), (await db.LeagueTournaments.InRegion(LeagueRegions.Bayern).Select(t => t.Tnr).ToListAsync()).Order());
        Assert.Empty(await db.LeagueTournaments.InRegion("mars").ToListAsync());
    }

    [MySqlFact]
    public async Task FillMissingFide_CarriesTheLigamanagerIdIntoZugspitzeOnly()
    {
        await SeedAsync();
        await using (var db = fixture.Schema.NewContext())
            Assert.Equal(1, await LeagueRegions.FillMissingFideAsync(db, LeagueRegions.Bayern, default));
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.Equal("90000001", (await db.LeaguePlayers.SingleAsync(p => p.Tnr == Zg)).FideId);
            Assert.Null((await db.LeaguePlayers.SingleAsync(p => p.Tnr == Cr)).FideId);
            Assert.Equal("90000001", (await db.LeagueGames.SingleAsync(g => g.Tnr == Zg)).HomeFide);
        }
    }
}

public sealed class LeagueRegionSqlFixture() : MariaDbClassFixture("lregion", withApp: false);
