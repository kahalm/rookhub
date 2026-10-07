using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Training + Backtest eines Prognose-Modells je Region (2026-10-07, „eigenes Modell für Bayern"): der Fit ist ein Port von
/// <c>model.fit</c> (Newton, L2 = 1, 30 Schritte), die Zeilen kommen aus <see cref="LeagueFeatures.RowsFor"/>.
/// </summary>
public class LeagueTrainingTests
{
    // ---- Fit --------------------------------------------------------------------------------------

    [Fact]
    public void Fit_SyntheticData_RecoversKnownWeights_AndIsStationary()
    {
        var rng = new Random(42);
        double[] truth = [-0.5, 1.5, -2.0, 0.8];
        var x = new List<double[]>();
        var y = new List<int>();
        for (var n = 0; n < 40_000; n++)
        {
            double[] xi = [1, rng.NextDouble() * 2 - 1, rng.Next(2), rng.NextDouble()];
            var z = xi.Zip(truth, (a, b) => a * b).Sum();
            x.Add(xi);
            y.Add(rng.NextDouble() < 1 / (1 + Math.Exp(-z)) ? 1 : 0);
        }
        var w = LeagueTraining.Fit(x, y);
        for (var j = 0; j < truth.Length; j++) Assert.InRange(w[j], truth[j] - 0.12, truth[j] + 0.12);

        // Optimalität: Gradient der bestraften Log-Likelihood = 0 (L2 nicht auf dem Achsenabschnitt)
        var g = new double[truth.Length];
        for (var n = 0; n < x.Count; n++)
        {
            var p = 1 / (1 + Math.Exp(-x[n].Zip(w, (a, b) => a * b).Sum()));
            for (var j = 0; j < g.Length; j++) g[j] += x[n][j] * (p - y[n]);
        }
        for (var j = 1; j < g.Length; j++) g[j] += w[j];
        Assert.All(g, v => Assert.InRange(v, -1e-6, 1e-6));
    }

    [Fact]
    public void Fit_L2_ShrinksButLeavesTheIntercept()
    {
        // vollständig trennbare Daten: ohne Strafe liefe das Gewicht davon — mit L2 = 1 bleibt es endlich
        var x = new List<double[]> { new double[] { 1, 1 }, new double[] { 1, 1 }, new double[] { 1, 0 }, new double[] { 1, 0 } };
        var y = new List<int> { 1, 1, 0, 0 };
        var w = LeagueTraining.Fit(x, y);
        // Vergleichswerte aus model.fit (numpy) auf denselben vier Zeilen
        Assert.Equal(-0.40105814, w[0], 7);
        Assert.Equal(0.80211628, w[1], 7);
    }

    [Fact]
    public void Solve_SmallSystem()
    {
        var a = new double[,] { { 0, 2 }, { 3, 1 } };   // Pivot nötig
        var x = LeagueTraining.Solve(a, [4, 5]);
        Assert.Equal(1.0, x[0], 12);
        Assert.Equal(2.0, x[1], 12);
    }

    /// <summary>Dieselben Tiroler Zeilen wie Python (<c>rows.json</c> aus features.py) → dieselben Gewichte wie
    /// <c>Assets/league-model.json</c> (export_weights.py: alle Saisonen außer der jüngsten). Nur lokal — die CI hat die Datei nicht.</summary>
    [RowsJsonFact]
    public void Fit_TirolRows_ReproducesThePythonWeights()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(RowsJsonFactAttribute.PathOrNull!));
        var all = doc.RootElement.EnumerateArray().ToList();
        var latest = all.Select(r => r.GetProperty("season").GetString()!).Max(StringComparer.Ordinal);
        var tirol = LeagueModel.FromEmbedded();
        var x = new List<double[]>();
        var y = new List<int>();
        foreach (var r in all.Where(r => r.GetProperty("season").GetString() != latest))
        {
            double D(string k) => r.GetProperty(k).GetDouble();
            int I(string k) => (int)r.GetProperty(k).GetDouble();
            var row = new FeatureRow
            {
                Level = I("level"), B = I("B"), Pos = D("pos"), Top = I("top"), Bench = I("bench"), QSame = D("q_same"),
                QHigher = D("q_higher"), QLower = D("q_lower"), NewPrev = I("new_prev"), NewEver = I("new_ever"), N = I("n"),
                First = I("first"), Cur = D("cur"), Last = I("last"), Last2 = I("last2"), Yesterday = I("yesterday"),
                YestPlayed = I("yest_played"), ConflictHi = D("conflict_hi"), ConflictLo = D("conflict_lo"),
            };
            x.Add(tirol.Vec(row));
            y.Add(I("y"));
        }
        var w = LeagueTraining.Fit(x, y);
        for (var j = 0; j < w.Length; j++) Assert.Equal(tirol.Weights[j], w[j], 6);
    }

    // ---- Merkmale ----------------------------------------------------------------------------------

    [Fact]
    public void Vec_BayernFeatures_MapLevelAndKreis()
    {
        var m = new LeagueModel(["const", "lvl_n", "kreis", "kreis_q", "lvl7", "lvl2", "q_same"], new double[7]);
        var oberliga = m.Vec(new FeatureRow { Level = 1, QSame = 0.5 });
        Assert.Equal([1, 0, 0, 0, 0, 0, 0.5], oberliga);
        var aKlasse = m.Vec(new FeatureRow { Level = 7, QSame = 0.5 });
        Assert.Equal([1, 6.0 / 8, 1, 0.5, 1, 0, 0.5], aKlasse);
        var zl = m.Vec(new FeatureRow { Level = LeagueModel.KreisLevel, QSame = 0.25 });
        Assert.Equal(1, zl[2]);
        Assert.Equal(0.25, zl[3]);
        Assert.Throws<InvalidOperationException>(() => new LeagueModel(["const", "gibts_nicht"], new double[2]).Vec(new FeatureRow()));
    }

    [Fact]
    public void BayernFeatures_DoNotUseTheTyroleanLevelDummies()
    {
        Assert.DoesNotContain("lvl2", LeagueTraining.BayernFeatures);
        Assert.DoesNotContain("gk_q", LeagueTraining.BayernFeatures);
        Assert.Equal("const", LeagueTraining.BayernFeatures[0]);
        Assert.Equal(LeagueModel.FromEmbedded().Features, LeagueTraining.DefaultFeatures(LeagueRegions.Tirol));
    }

    // ---- Datensatz + Backtest ------------------------------------------------------------------------

    /// <summary>Kleine bayerische Liga über drei Saisonen (die jüngste ohne gespielte Runde): 4 Teams à 6 Gemeldete, 4 Bretter,
    /// 3 Runden. Es spielen die Gemeldeten 1, 2, 3 und 5 — Nr. 4 fehlt immer, Nr. 5 rückt nach.</summary>
    internal static (List<LeagueTournament> T, List<LeagueRound> R, List<LeagueMatch> M, List<LeagueGame> G, List<LeaguePlayer> P) BayernHistory()
    {
        var ts = new List<LeagueTournament>(); var rs = new List<LeagueRound>(); var ms = new List<LeagueMatch>();
        var gs = new List<LeagueGame>(); var ps = new List<LeaguePlayer>();
        int id = 1;
        string[] teams = ["SK A 1", "SK B 1", "SK C 1", "SK D 1"];
        (int R, string H, string A)[] plan = [(1, "SK A 1", "SK B 1"), (1, "SK C 1", "SK D 1"), (2, "SK B 1", "SK C 1"),
            (2, "SK D 1", "SK A 1"), (3, "SK A 1", "SK C 1"), (3, "SK D 1", "SK B 1")];
        foreach (var (season, y, played) in new[] { ("2024/25", 2024, true), ("2025/26", 2025, true), ("2026/27", 2026, false) })
        {
            var tnr = LigamanagerSource.TnrOf(y);
            ts.Add(new LeagueTournament { Tnr = tnr, Name = "Landesliga Süd", Season = season, Level = 3, League = "Landesliga Süd",
                Stage = "Liga", Source = LigamanagerSource.Source, Boards = 4 });
            for (var r = 1; r <= 3; r++) rs.Add(new LeagueRound { Tnr = tnr, Round = r, Date = new DateOnly(y, 10, 1).AddDays(14 * r) });
            foreach (var team in teams)
                for (var nr = 1; nr <= 6; nr++)
                    ps.Add(new LeaguePlayer { Id = id++, Tnr = tnr, Team = team, RosterBoard = nr, Name = $"{team[3]}spieler, Nr{nr}",
                        NameKey = $"{char.ToLowerInvariant(team[3])}spieler, nr{nr}", FideId = $"{team[3]}{nr}", EloI = 2000 - nr * 20 });
            var no = 0;
            foreach (var (r, h, a) in plan)
            {
                ms.Add(new LeagueMatch { Id = id++, Tnr = tnr, Round = r, MatchNo = ++no, Home = h, Away = a,
                    HomePts = played ? 2 : null, AwayPts = played ? 2 : null });
                int[] who = [1, 2, 3, 5];
                for (var b = 1; b <= 4; b++)
                    gs.Add(new LeagueGame
                    {
                        Id = id++, Tnr = tnr, Round = r, MatchNo = no, Board = b, HomeTeam = h, AwayTeam = a,
                        HomePlayer = played ? $"{h[3]}spieler, Nr{who[b - 1]}" : null, AwayPlayer = played ? $"{a[3]}spieler, Nr{who[b - 1]}" : null,
                        HomeFide = played ? $"{h[3]}{who[b - 1]}" : null, AwayFide = played ? $"{a[3]}{who[b - 1]}" : null,
                        Result = played ? "½-½" : "", HomeScore = played ? .5 : null, AwayScore = played ? .5 : null,
                    });
            }
        }
        return (ts, rs, ms, gs, ps);
    }

    [Fact]
    public void Dataset_OnlyPlayedRoundsOfTheRegion_LabelsTheLineup()
    {
        var (t, r, m, g, p) = BayernHistory();
        var w = new LeagueWorld(t, r, m, g, p);
        var groups = LeagueTraining.Dataset(w, LeagueRegions.Bayern);
        Assert.Equal(2 * 3 * 4, groups.Count);                         // 2 gespielte Saisonen × 3 Runden × 4 Teams
        Assert.All(groups, x => Assert.Equal(6, x.Rows.Count));
        Assert.All(groups, x => Assert.Equal(new[] { 1, 1, 1, 0, 1, 0 }, x.Y));
        Assert.DoesNotContain(groups, x => x.Season == "2026/27");
        Assert.Equal("R1", groups.First(x => x.Round == 1).Phase);
        Assert.Empty(LeagueTraining.Dataset(w, LeagueRegions.Tirol));
    }

    [Fact]
    public void Backtest_CountsHitsBoardsAndTheNaiveBaseline()
    {
        var (t, r, m, g, p) = BayernHistory();
        var w = new LeagueWorld(t, r, m, g, p);
        var groups = LeagueTraining.Dataset(w, LeagueRegions.Bayern);
        var model = LeagueTraining.Train(LeagueTraining.BayernFeatures, groups.Where(x => x.Season == "2024/25"));
        var bt = LeagueTraining.Backtest(model, groups.Where(x => x.Season == "2025/26"));
        var tot = bt.Total;
        Assert.Equal(12, tot.Matches);
        Assert.Equal(48, tot.Act);
        Assert.Equal(48, tot.Boards);
        Assert.Equal(36, tot.BaseHits);                                // Meldeliste 1–4: Nr. 4 fehlt immer
        Assert.True(tot.Hits > tot.BaseHits, $"Treffer {tot.Hits}");    // ab Runde 2 verraten die Einsätze, dass Nr. 5 statt Nr. 4 spielt
        Assert.Equal(36, tot.BaseTop1);                                // Bretter 1–3 nach Meldeliste richtig, an 4 sitzt Nr. 5
        Assert.Equal(48, tot.BaseTop3);                                // k, k+1, k+2 decken das Nachrücken ab
        Assert.True(tot.Top1 > tot.BaseTop1, $"Top-1 {tot.Top1}");
        Assert.True(tot.LogLoss < tot.BaseLogLoss);
        Assert.Equal(72, Enumerable.Range(0, 10).Sum(i => bt.Calibration[i, 0]));   // alle Zeilen in der Kalibrierung
        Assert.Equal(48, Enumerable.Range(0, 10).Sum(i => bt.Calibration[i, 2]));
        Assert.Contains((3, "R1"), bt.ByPhase.Keys);
        Assert.Contains("Kalibrierung", LeagueTraining.Report("Test", bt));
    }

    [Fact]
    public async Task TrainAsync_InMemory_HoldoutIsTheLatestCompleteSeason_ExportsTheModelFormat()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(opts);
        var (t, r, m, g, p) = BayernHistory();
        db.LeagueTournaments.AddRange(t); db.LeagueRounds.AddRange(r); db.LeagueMatches.AddRange(m);
        db.LeagueGames.AddRange(g); db.LeaguePlayers.AddRange(p);
        // eine Tiroler Liga gehört NICHT dazu
        db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Season = "2025/26", Level = 1, League = "Landesliga", Stage = "Liga" });
        await db.SaveChangesAsync();

        var rep = await LeagueTraining.TrainAsync(db, LeagueRegions.Bayern, LeagueTraining.BayernFeatures, null, LeagueModel.FromEmbedded(), default);
        Assert.Equal(["2025/26"], rep.Holdout);
        Assert.Equal(["2024/25", "2025/26"], rep.TrainedOn);          // die laufende Saison 2026/27 nicht
        Assert.Equal(2 * 12 * 6, rep.Rows);
        Assert.Equal(12, rep.Backtest.Total.Matches);
        Assert.NotNull(rep.Reference);
        Assert.Equal(12, rep.Reference!.Total.Matches);
        Assert.Equal(3, rep.Seasons.Count);
        Assert.All(rep.Seasons, s => Assert.Equal(24, s.PlayersWithFide));

        // dasselbe Format wie Assets/league-model.json — und es lädt wieder
        var json = rep.Json;
        Assert.Equal(LeagueTraining.BayernFeatures.Count, json["features"]!.AsArray().Count);
        Assert.Equal(json["features"]!.AsArray().Count, json["weights"]!.AsArray().Count);
        Assert.Equal(144, json["rows"]!.GetValue<int>());
        Assert.NotNull(json["note"]);
        var back = LeagueModel.FromJson(json.ToJsonString());
        Assert.Equal(rep.Model.Weights, back.Weights);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LeagueTraining.TrainAsync(db, LeagueRegions.Bayern, LeagueTraining.BayernFeatures, ["2024/25"], null, default));   // nichts davor
    }
}

/// <summary>Läuft nur, wenn die Python-Zeilen <c>rows.json</c> da sind (<c>LEAGUE_ROWS_JSON</c> oder
/// <c>~/claude/league-analyzer/rows.json</c>).</summary>
public sealed class RowsJsonFactAttribute : FactAttribute
{
    public static string? PathOrNull
    {
        get
        {
            var p = Environment.GetEnvironmentVariable("LEAGUE_ROWS_JSON");
            if (string.IsNullOrWhiteSpace(p))
                p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "claude", "league-analyzer", "rows.json");
            return File.Exists(p) ? p : null;
        }
    }

    public RowsJsonFactAttribute()
    {
        if (PathOrNull is null) Skip = "rows.json der Python-Fassung fehlt — Gewichts-Vergleich nur lokal.";
    }
}
