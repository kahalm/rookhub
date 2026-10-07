using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Modell je Region (2026-10-07): Bayern rechnet mit <c>Assets/league-model-bayern.json</c>, Tirol unverändert mit
/// <c>league-model.json</c>; eine Region ohne eigenes Modell fällt auf Tirol zurück.</summary>
public class LeagueModelsTests
{
    private static readonly Dictionary<string, int> NoCounts = new();
    private static readonly Dictionary<string, List<LeagueOnlineAccount>> NoAccounts = new();

    [Fact]
    public void Embedded_BayernModel_HasTheBayernFeatures()
    {
        var m = LeagueModel.FromEmbedded(LeagueRegions.Bayern)!;
        Assert.Equal(LeagueTraining.BayernFeatures, m.Features);
        Assert.Equal(m.Features.Count, m.Weights.Length);
        // dieselbe Richtung wie in Tirol: wer gestern spielte, spielt heute; wer bisher spielte, spielt
        Assert.True(m.Weights[m.Features.ToList().IndexOf("yest_played")] > 0);
        Assert.True(m.Weights[m.Features.ToList().IndexOf("cur")] > 0);
        Assert.Null(LeagueModel.FromEmbedded("mars"));
        Assert.Equal(LeagueModel.FromEmbedded().Weights, LeagueModel.FromEmbedded(LeagueRegions.Tirol)!.Weights);
    }

    [Fact]
    public void WithEmbedded_PicksTheModelOfTheRegion_FallsBackToTirol()
    {
        var tirol = LeagueModel.FromEmbedded();
        var models = LeagueModels.WithEmbedded(tirol, NullLogger.Instance);
        var bayern = LeagueModel.FromEmbedded(LeagueRegions.Bayern)!;
        Assert.Same(tirol, models.For(null));
        Assert.Equal(bayern.Weights, models.For(LigamanagerSource.Source).Weights);
        Assert.Equal(bayern.Weights, models.For(ZugspitzeSource.Source).Weights);
        Assert.True(models.Has(LeagueRegions.Bayern));
        Assert.False(models.Has("mars"));
        Assert.Same(tirol, models.ForRegion("mars"));                 // Rückfall (Warnung einmal je Region)
        Assert.Same(tirol, models.ForRegion("mars"));
        Assert.Same(tirol, new LeagueModels(tirol).For(LigamanagerSource.Source));   // ohne weitere Modelle: alles Tirol
    }

    [Fact]
    public void View_BayernLeague_UsesTheBayernWeights_AndItsBacktest()
    {
        var (t, r, m, g, p) = LeagueTrainingTests.BayernHistory();
        var w = new LeagueWorld(t, r, m, g, p);
        var tnr = LigamanagerSource.TnrOf(2026);
        var bayern = LeagueModel.FromEmbedded(LeagueRegions.Bayern)!;
        var view = new LeagueViewBuilder(w, LeagueModels.WithEmbedded(LeagueModel.FromEmbedded()), NoCounts, NoAccounts).Build(tnr, w.Games);
        var fx = view["fixtures"]!["SK A 1"]!["1"]!;
        var rows = LeagueFeatures.RowsFor(w, tnr, "SK B 1", 1);         // A spielt Runde 1 gegen B → Prognose für B
        var expect = bayern.Predict(rows);
        var got = fx["roster"]!.AsArray().Select(x => x!["p"]!.GetValue<double>()).ToArray();
        Assert.Equal(expect.Select(x => Math.Round(x, 3)), got);
        var tirolP = LeagueModel.FromEmbedded().Predict(rows).Select(x => Math.Round(x, 3)).ToArray();
        Assert.NotEqual(tirolP, got);
        Assert.Equal("R1", fx["phase"]!.GetValue<string>());
        Assert.Equal(Math.Round(.71 * 4, 1), fx["hit"]!.GetValue<double>());   // Landesliga (Stufe 3) R1, 4 Bretter

        // ein einziges (Tiroler) Modell für alles: rechnet, aber ohne Backtest-Erwartung für Bayern
        var single = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), NoCounts, NoAccounts).Build(tnr, w.Games);
        Assert.Null(single["fixtures"]!["SK A 1"]!["1"]!["hit"]);
        Assert.Equal(tirolP, single["fixtures"]!["SK A 1"]!["1"]!["roster"]!.AsArray().Select(x => x!["p"]!.GetValue<double>()).ToArray());
    }

    [Fact]
    public void View_TirolLeague_IsUnchangedByTheRegionalModels()
    {
        var w = LeagueEngineTests.TinyWorld();
        var before = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), NoCounts, NoAccounts).Build(1, w.Games).ToJsonString();
        var after = new LeagueViewBuilder(w, LeagueModels.WithEmbedded(LeagueModel.FromEmbedded()), NoCounts, NoAccounts).Build(1, w.Games).ToJsonString();
        Assert.Equal(before, after);
        Assert.Contains("\"hit\":", after);
    }

    [Theory]
    [InlineData("tirol", true, 1, "R1", .58)]
    [InlineData("bayern", true, 3, "R2+", .79)]
    [InlineData("bayern", true, 1, "So vorab", .79)]
    [InlineData("bayern", false, 3, "R2+", null)]
    [InlineData("bayern", true, 9, "R1", null)]
    [InlineData("mars", true, 1, "R1", null)]
    public void ExpectedHits_PerRegion(string region, bool own, int level, string phase, double? expected) =>
        Assert.Equal(expected, LeagueViewBuilder.ExpectedHits(region, own, level, phase));

    [Fact]
    public async Task RebuildViews_BayernLeague_RecomputesWithTheBayernModel()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(opts);
        var (t, r, m, g, p) = LeagueTrainingTests.BayernHistory();
        db.LeagueTournaments.AddRange(t); db.LeagueRounds.AddRange(r); db.LeagueMatches.AddRange(m);
        db.LeagueGames.AddRange(g); db.LeaguePlayers.AddRange(p);
        await db.SaveChangesAsync();
        var svc = new LeagueService(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        Assert.Equal(1, await svc.RebuildViewsAsync(default));
        var json = await svc.LeagueJsonAsync(LigamanagerSource.TnrOf(2026), default);
        Assert.Contains("\"hit\":2.8", json);                          // Bayern-Backtest Stufe 3 R1 · 4 Bretter
    }
}
