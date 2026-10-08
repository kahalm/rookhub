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
            new LeagueTournament { Tnr = Cr, Name = "TMM", Season = "2026/27", Level = 3, League = "Landesliga", Stage = "Liga" });
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
    public async Task IndexAndForecastStats_FilterByRegionInSql()
    {
        await SeedAsync();
        await using (var db = fixture.Schema.NewContext())
        {
            db.LeagueViews.AddRange(new LeagueView { Tnr = Lm, Json = "{}", GeneratedAt = DateTime.UtcNow },
                new LeagueView { Tnr = Zg, Json = "{}", GeneratedAt = DateTime.UtcNow }, new LeagueView { Tnr = Cr, Json = "{}", GeneratedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var league = new LeagueService(db, LeagueModel.FromEmbedded(), Microsoft.Extensions.Logging.Abstractions.NullLogger<LeagueService>.Instance);
            var bayern = await league.IndexAsync(new LeagueClub { Id = 2, Name = "SK Weilheim", TeamPrefix = "SK Weilheim", AnonName = "Weilheim",
                Region = LeagueRegions.Bayern }, default);
            Assert.Equal(new[] { Lm, Zg }, bayern["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()));
            var tirol = await league.IndexAsync(new LeagueClub { Id = 1, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz" }, default);
            Assert.Equal(new[] { Cr }, tirol["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()));
            // 0.710.0: nur Ligen mit eigener Mannschaft — DISTINCT über Spielplan + Meldelisten, verglichen im Speicher
            var nowhere = await league.IndexAsync(new LeagueClub { Id = 3, Name = "SK Nirgends", TeamPrefix = "Nirgends", AnonName = "Nirgends",
                Region = LeagueRegions.Bayern }, default);
            Assert.Empty(nowhere["leagues"]!.AsArray());
            Assert.Equal(2, nowhere["total"]!.GetValue<int>());
            Assert.Equal("2026/27", (await league.ForecastStatsAsync(LeagueRegions.Bayern, default))["season"]!.GetValue<string>());
        }
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

    /// <summary>Das Wartungswerkzeug (league-train) lädt die Welt EINER Region — die Listen-Filter über die Turniernummern müssen
    /// in SQL übersetzt werden (2026-10-07, eigenes Modell für Bayern).</summary>
    [MySqlFact]
    public async Task TrainingWorld_LoadsOnlyTheRegion()
    {
        await SeedAsync();
        await using var db = fixture.Schema.NewContext();
        var w = await LeagueTraining.LoadWorldAsync(db, LeagueRegions.Bayern, default);
        Assert.Equal(new[] { Lm, Zg }.Order(), w.T.Keys.Order());
        Assert.Single(w.Games);
        Assert.Equal(2, w.Roster.Count);
        Assert.Empty(LeagueTraining.Dataset(w, LeagueRegions.Bayern));   // ohne Begegnung kein Spielplan → keine Trainingszeile
    }
}

public sealed class LeagueRegionSqlFixture() : MariaDbClassFixture("lregion", withApp: false);
