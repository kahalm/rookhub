using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Österreichische Bundesliga in LeagueHub (0.719.0, Wunsch 2026-10-08): Tiroler Stufen um zwei verschoben (1. Bundesliga 1,
/// 2. Bundesliga 2, Landesliga 3 … Gebietsklasse 6) — das trainierte Tiroler Modell rechnet dieselben Zahlen —, Runden-Blöcke der
/// Bundesliga (Fr–So, fünf Tage am Stück, Doppelrunde an einem Tag), Vereinsnamen der Bundesliga, Einsätze je Region.
/// </summary>
public class LeagueBundesligaTests
{
    private static readonly string[] Teams = { "A", "B", "C", "D" };

    /// <summary>Vier Teams, zwei Bretter, je Team vier Gemeldete; die ersten <paramref name="played"/> Runden sind gespielt
    /// (Brett b = Gemeldeter <paramref name="lineup"/>(Runde, b), Vorgabe b).</summary>
    internal static LeagueWorld BlockWorld(int level, DateOnly[] dates, int played, string? source = null, Func<int, int, int>? lineup = null)
    {
        lineup ??= (_, b) => b;
        var t = new LeagueTournament { Tnr = 1, Name = "Testliga", Season = "2026/27", Level = level, League = "Testliga", Stage = "Liga", Source = source };
        var rounds = dates.Select((d, i) => new LeagueRound { Tnr = 1, Round = i + 1, Date = d }).ToArray();
        (string, string)[][] pairs =
        {
            new[] { ("A", "B"), ("C", "D") }, new[] { ("A", "C"), ("B", "D") }, new[] { ("D", "A"), ("C", "B") },
        };
        var matches = new List<LeagueMatch>();
        var games = new List<LeagueGame>();
        int id = 1, gid = 1;
        for (var r = 1; r <= dates.Length; r++)
        {
            var isPlayed = r <= played;
            foreach (var (h, a) in pairs[(r - 1) % 3])
            {
                matches.Add(new LeagueMatch { Id = id++, Tnr = 1, Round = r, Home = h, Away = a, HomePts = isPlayed ? 1 : null, AwayPts = isPlayed ? 1 : null });
                for (var b = 1; b <= 2; b++)
                    games.Add(new LeagueGame
                    {
                        Id = gid++, Tnr = 1, Round = r, MatchNo = 1, Board = b, HomeTeam = h, AwayTeam = a,
                        HomePlayer = isPlayed ? $"{h}spieler, Nr{lineup(r, b)}" : null, AwayPlayer = isPlayed ? $"{a}spieler, Nr{lineup(r, b)}" : null,
                        HomeFide = isPlayed ? $"{h}{lineup(r, b)}" : null, AwayFide = isPlayed ? $"{a}{lineup(r, b)}" : null,
                        Result = isPlayed ? "½ - ½" : "", HomeScore = isPlayed ? .5 : null, AwayScore = isPlayed ? .5 : null,
                    });
            }
        }
        var players = new List<LeaguePlayer>();
        foreach (var team in Teams)
            for (var rb = 1; rb <= 4; rb++)
                players.Add(new LeaguePlayer
                {
                    Id = id++, Tnr = 1, Team = team, RosterBoard = rb, Name = $"{team}spieler, Nr{rb}",
                    NameKey = $"{team.ToLowerInvariant()}spieler, nr{rb}", FideId = $"{team}{rb}", EloI = 2000 - rb * 50,
                });
        return new LeagueWorld(new[] { t }, rounds, matches, games, players);
    }

    private static JsonObjectView View(LeagueWorld w) =>
        new(new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), new Dictionary<string, int>(), new Dictionary<string, List<LeagueOnlineAccount>>())
            .Build(1, w.Games));

    private sealed record JsonObjectView(System.Text.Json.Nodes.JsonObject V)
    {
        public HashSet<int> Open => V["rounds"]!.AsArray().Where(r => r!["open"]!.GetValue<bool>()).Select(r => r!["round"]!.GetValue<int>()).ToHashSet();
        public string? Phase(string team, int r) => V["fixtures"]![team]![r.ToString()]!["phase"]?.GetValue<string>();
        public int? UnlockAfter(string team, int r) => V["fixtures"]![team]![r.ToString()]!["unlock_after"]?.GetValue<int>();
        public double? Hit(string team, int r) => V["fixtures"]![team]![r.ToString()]!["hit"]?.GetValue<double>();
    }

    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    // ---- Stufen ------------------------------------------------------------------------------------

    [Fact]
    public void Levels_TyroleanLadder_StartsWithTheBundesliga()
    {
        Assert.Equal(new[] { "BL", "2.BL", "LL", "1.Kl", "2.Kl", "GK" },
            Enumerable.Range(1, 6).Select(l => LeagueLevels.Short(null, l)));
        Assert.Equal(Enumerable.Range(1, 6), LeagueLevels.Of(null));
        Assert.Equal("OL", LeagueLevels.Short(LigamanagerSource.Source, 1));     // Bayern unverändert
        // Python-Fassung (export_bundle.py, rows.json): Landesliga 1 … Gebietsklasse 4
        Assert.Equal(new[] { 3, 4, 5, 6 }, new[] { 1, 2, 3, 4 }.Select(LeagueLevels.FromTmm));
        Assert.Equal(LeagueLevels.TirolLandesliga, LeagueLevels.FromTmm(1));
        Assert.Equal(LeagueLevels.TirolGebietsklasse, LeagueLevels.FromTmm(4));
    }

    [Theory]
    [InlineData(1, 6)]   // 1. Bundesliga (2024/25–2026/27 nachgezählt)
    [InlineData(2, 6)]   // 2. Bundesliga
    [InlineData(3, 6)]   // Landesliga
    [InlineData(4, 6)]   // 1. Klasse
    [InlineData(5, 5)]   // 2. Klasse
    [InlineData(6, 4)]   // Gebietsklasse
    public void DefaultBoards_FollowTheShiftedLevels(int level, int boards) =>
        Assert.Equal(boards, LeagueLevels.DefaultBoards(null, level));

    [Fact]
    public void DefaultBoards_Bavaria_Unchanged()
    {
        Assert.Equal(new[] { 6, 6, 5, 4, 6 }, new[] { 1, 2, 3, 4, 5 }.Select(l => LeagueLevels.DefaultBoards(LigamanagerSource.Source, l)));
    }

    [Fact]
    public void RoundBlocks_BundesligaAndLandesliga_SameDayOnlyInTheBundesliga()
    {
        Assert.True(LeagueLevels.Consecutive(null, LeagueLevels.Bundesliga, D(12, 3, 2027), D(12, 3, 2027)));     // Doppelrunde
        Assert.True(LeagueLevels.Consecutive(null, LeagueLevels.Bundesliga2, D(16, 10), D(17, 10)));
        Assert.True(LeagueLevels.Consecutive(null, LeagueLevels.TirolLandesliga, D(3, 10), D(4, 10)));
        Assert.False(LeagueLevels.Consecutive(null, LeagueLevels.TirolLandesliga, D(1, 1), D(1, 1)));   // Platzhalter-Termine der TMM
        Assert.False(LeagueLevels.Consecutive(null, 4, D(3, 10), D(4, 10)));                            // 1. Klasse: kein Block
        Assert.True(LeagueLevels.Consecutive(LigamanagerSource.Source, 1, D(3, 10), D(4, 10)));          // Bayern wie bisher Stufe 1
        Assert.False(LeagueLevels.Consecutive(LigamanagerSource.Source, 3, D(3, 10), D(4, 10)));
        Assert.False(LeagueLevels.Consecutive(null, LeagueLevels.Bundesliga, null, D(4, 10)));
    }

    // ---- Modell: dieselben Zahlen wie vor der Verschiebung --------------------------------------------

    [Fact]
    public void TirolModel_UsesTheShiftedDummies()
    {
        var f = LeagueModel.FromEmbedded().Features;
        Assert.DoesNotContain("lvl2", f);
        Assert.DoesNotContain("lvl3", f);
        Assert.Equal(new[] { "lvl4", "lvl5", "lvl6", "gk_q" }, f.Skip(20).Take(4));
    }

    /// <summary>Der Logit einer Zeile mit der NEUEN Stufe ist derselbe, den das Python-Modell (lvl2–lvl4 = 1. Klasse … Gebietsklasse,
    /// gk_q = q_same · Gebietsklasse) mit der ALTEN Stufe rechnete — von Hand aus den Gewichten an ihren festen Plätzen.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TirolModel_LogitOfAShiftedRow_EqualsThePythonLogit(int oldLevel)
    {
        var m = LeagueModel.FromEmbedded();
        var w = m.Weights;
        var row = new FeatureRow { Level = LeagueLevels.FromTmm(oldLevel), QSame = 0.5, Top = 1, Pos = 0.25, Cur = 0.4, N = 3 };
        var fi = m.Features.ToList();
        double expected = w[fi.IndexOf("const")] + w[fi.IndexOf("top")] + 0.25 * w[fi.IndexOf("pos_c")] + 0.5 * w[fi.IndexOf("q_same")]
            + 0.4 * w[fi.IndexOf("cur")] + 0.4 * 3 / 6.0 * w[fi.IndexOf("cur_n")];
        // Python-Plätze 20–23: lvl2, lvl3, lvl4, gk_q
        if (oldLevel == 2) expected += w[20];
        if (oldLevel == 3) expected += w[21];
        if (oldLevel == 4) expected += w[22] + 0.5 * w[23];
        Assert.Equal(expected, m.Logit(row), 12);
    }

    [Fact]
    public void ExpectedHits_SitOnTheShiftedLevels_NoneForTheBundesliga()
    {
        Assert.Equal(.58, LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, LeagueLevels.TirolLandesliga, "R1"));
        Assert.Equal(.76, LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, 3, "So nach Sa"));
        Assert.Equal(.66, LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, 4, "R2+"));
        Assert.Equal(.63, LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, 5, "R2+"));
        Assert.Equal(.33, LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, LeagueLevels.TirolGebietsklasse, "R1"));
        Assert.Null(LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, LeagueLevels.Bundesliga, "R1"));
        Assert.Null(LeagueViewBuilder.ExpectedHits(LeagueRegions.Tirol, true, LeagueLevels.Bundesliga2, "R2+"));
        Assert.Equal(.74, LeagueViewBuilder.ExpectedHits(LeagueRegions.Bayern, true, 1, "R1"));         // Bayern unverändert
    }

    // ---- Runden-Blöcke in der Ansicht --------------------------------------------------------------

    [Fact]
    public void View_SecondBundesliga_OpensTheWholeFridayToSundayBlock()
    {
        var w = BlockWorld(LeagueLevels.Bundesliga2, new[] { D(16, 10), D(17, 10), D(18, 10), D(21, 11), D(22, 11) }, played: 0);
        var v = View(w);
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, v.Open);
        Assert.Equal("R1", v.Phase("A", 1));
        Assert.Equal("So vorab", v.Phase("A", 2));       // Freitag noch offen
        Assert.Equal("So vorab", v.Phase("A", 3));       // Freitag + Samstag noch offen (rekursive Mischung)
        Assert.Equal(3, v.UnlockAfter("A", 4));          // genauer, sobald der erste Block gespielt ist
        Assert.Equal(3, v.UnlockAfter("A", 5));
        Assert.Null(v.Hit("A", 1));                      // kein Backtest für die Bundesliga
        var sum = v.V["fixtures"]!["A"]!["3"]!["roster"]!.AsArray().Sum(r => r!["p"]!.GetValue<double>());
        Assert.Equal(2.0, sum, 2);                       // die Mischung bleibt auf B normiert
    }

    [Fact]
    public void View_SecondBundesliga_FridayPlayed_SaturdayWithFriday_SundayStillAhead()
    {
        var w = BlockWorld(LeagueLevels.Bundesliga2, new[] { D(16, 10), D(17, 10), D(18, 10), D(21, 11), D(22, 11) }, played: 1);
        var v = View(w);
        Assert.Equal(new HashSet<int> { 2, 3 }, v.Open);
        Assert.Equal("So nach Sa", v.Phase("A", 2));
        Assert.Equal("So vorab", v.Phase("A", 3));
    }

    [Fact]
    public void View_FirstBundesliga_DoubleRoundOnOneDay_CountsTheMorningRound()
    {
        var dates = new[] { D(10, 3, 2027), D(11, 3, 2027), D(12, 3, 2027), D(12, 3, 2027), D(13, 3, 2027) };
        var ahead = View(BlockWorld(LeagueLevels.Bundesliga, dates, played: 2));
        Assert.Equal(new HashSet<int> { 3, 4, 5 }, ahead.Open);
        Assert.Equal("So vorab", ahead.Phase("A", 4));   // Vormittag noch offen

        // Runden 1–2 spielen Gemeldete 1+2, die Vormittagsrunde 3 die Gemeldeten 3+4.
        var w = BlockWorld(LeagueLevels.Bundesliga, dates, played: 3, lineup: (r, b) => r == 3 ? b + 2 : b);
        var v = View(w);
        Assert.Equal(new HashSet<int> { 4, 5 }, v.Open);
        Assert.Equal("So nach Sa", v.Phase("A", 4));
        // Die am Vormittag gespielte Runde zählt mit (Wissen „bis Tagesende"), nicht nur Runden an früheren Tagen.
        var m = LeagueModel.FromEmbedded();
        var with = m.Predict(LeagueFeatures.RowsFor(w, 1, "B", 4, asof: D(13, 3, 2027)));
        var without = m.Predict(LeagueFeatures.RowsFor(w, 1, "B", 4));
        var roster = v.V["fixtures"]!["B"]!["4"]!["roster"]!.AsArray();   // Gegner von A in Runde 4 (Paarung 1: A–B)
        var shown = roster.Select(r => r!["p"]!.GetValue<double>()).ToArray();
        Assert.Equal(with.Select(x => Math.Round(x, 3)), shown);
        Assert.NotEqual(without.Select(x => Math.Round(x, 3)), shown);
        Assert.Equal(3, LeagueFeatures.RowsFor(w, 1, "B", 4, asof: D(13, 3, 2027))[0].N);
    }

    [Fact]
    public void View_FirstClass_SaturdayAndSunday_AreNoBlock()
    {
        var v = View(BlockWorld(4, new[] { D(3, 10), D(4, 10), D(7, 11) }, played: 0));
        Assert.Equal(new HashSet<int> { 1 }, v.Open);
        Assert.Equal("R1", v.Phase("A", 2));
        Assert.Equal(1, v.UnlockAfter("A", 2));
    }

    [Fact]
    public void View_Landesliga_StillSaturdayPlusSunday()
    {
        var v = View(BlockWorld(LeagueLevels.TirolLandesliga, new[] { D(3, 10), D(4, 10), D(7, 11), D(8, 11) }, played: 0));
        Assert.Equal(new HashSet<int> { 1, 2 }, v.Open);
        Assert.Equal("So vorab", v.Phase("A", 2));
        Assert.Equal(2, v.UnlockAfter("A", 4));
        Assert.Equal(Math.Round(.58 * 2, 1), v.Hit("A", 1));
    }

    // ---- Vereine + Einsätze ------------------------------------------------------------------------

    [Theory]
    [InlineData("Schachklub Schwaz", "Schwaz")]
    [InlineData("Schachclub Schwaz", "Schwaz")]
    [InlineData("Schwaz", "Schwaz")]
    [InlineData("Innsbruck Pradl", "Innsbruck-Pradl")]
    [InlineData("Innsbruck-Pradl", "Innsbruck-Pradl")]
    [InlineData("SPG Kufstein/Wörgl", "Kufstein/Wörgl")]
    [InlineData("SK Sparkasse Jenbach", "Jenbach")]
    [InlineData("Spg Hall/Mils", "Rum/Hall/Mils")]
    [InlineData("Rochade Rum", "Rum/Hall/Mils")]
    [InlineData("SK Elektro Strobl Hallein", "SK Elektro Strobl Hallein")]   // Salzburg, nicht Hall in Tirol
    [InlineData("SK Schwarzach", "SK Schwarzach")]
    public void Club_BundesligaNamesOfTyroleanClubs(string team, string club) => Assert.Equal(club, LeagueNames.Club(team));

    [Fact]
    public void Apps_AreSeparatedByRegion()
    {
        // Derselbe Spieler (FIDE 77) in der 1. Bundesliga (Tirol, Stufe 1) und in der Oberliga Bayern (Stufe 1), beide 2025/26.
        var at = new LeagueTournament { Tnr = 1, Season = "2025/26", Level = LeagueLevels.Bundesliga, League = "1. Bundesliga", Stage = "Liga" };
        var by = new LeagueTournament { Tnr = 900_000_001, Season = "2025/26", Level = 1, League = "Oberliga", Stage = "Liga", Source = LigamanagerSource.Source };
        LeagueGame G(int tnr, int round) => new()
        {
            Id = tnr + round, Tnr = tnr, Round = round, MatchNo = 1, Board = 1, HomeTeam = "H", AwayTeam = "G", HomePlayer = "Doppel, Max",
            HomeFide = "77", AwayPlayer = "Gast, X", AwayFide = "78", Result = "1 - 0", HomeScore = 1, AwayScore = 0,
        };
        var w = new LeagueWorld(new[] { at, by }, Array.Empty<LeagueRound>(),
            new[] { new LeagueMatch { Id = 1, Tnr = 1, Round = 1, Home = "H", Away = "G" }, new LeagueMatch { Id = 2, Tnr = 900_000_001, Round = 1, Home = "H", Away = "G" },
                new LeagueMatch { Id = 3, Tnr = 900_000_001, Round = 2, Home = "G", Away = "H" } },
            new[] { G(1, 1), G(900_000_001, 1), G(900_000_001, 2) }, Array.Empty<LeaguePlayer>());
        Assert.Equal(1, w.Apps(null, "2025/26", 1, "77"));
        Assert.Equal(2, w.Apps(LigamanagerSource.Source, "2025/26", 1, "77"));
        Assert.Equal(2, w.Apps(ZugspitzeSource.Source, "2025/26", 1, "77"));   // dieselbe Region Bayern
    }

    [Fact]
    public void RowsFor_Landesliga_CountsLastSeasonsBundesligaAsHigher()
    {
        // 2025/26: P spielte eine Runde 2. Bundesliga; 2026/27 steht P in der Meldeliste der Landesliga.
        var bl = new LeagueTournament { Tnr = 1, Season = "2025/26", Level = LeagueLevels.Bundesliga2, League = "2. Bundesliga", Grp = "West", Stage = "Liga" };
        var ll = new LeagueTournament { Tnr = 2, Season = "2026/27", Level = LeagueLevels.TirolLandesliga, League = "Landesliga", Stage = "Liga" };
        var w = new LeagueWorld(new[] { bl, ll },
            new[] { new LeagueRound { Tnr = 1, Round = 1, Date = D(10, 10, 2025) }, new LeagueRound { Tnr = 2, Round = 1, Date = D(3, 10) } },
            new[] { new LeagueMatch { Id = 1, Tnr = 1, Round = 1, Home = "Testdorf", Away = "Gast", HomePts = 1, AwayPts = 0 },
                new LeagueMatch { Id = 2, Tnr = 2, Round = 1, Home = "Testdorf 2", Away = "Gast 2" } },
            new[] { new LeagueGame { Id = 1, Tnr = 1, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Gast", HomePlayer = "Probe, Paul",
                HomeFide = "P", AwayPlayer = "Gast, X", AwayFide = "X", Result = "1 - 0", HomeScore = 1, AwayScore = 0 } },
            new[] { new LeaguePlayer { Id = 1, Tnr = 2, Team = "Testdorf 2", RosterBoard = 1, Name = "Probe, Paul", NameKey = "probe, paul", FideId = "P" },
                new LeaguePlayer { Id = 2, Tnr = 2, Team = "Testdorf 2", RosterBoard = 2, Name = "Neu, Nina", NameKey = "neu, nina", FideId = "N" } });
        var rows = LeagueFeatures.RowsFor(w, 2, "Testdorf 2", 1);
        var p = rows.Single(r => r.Pid == "P");
        Assert.Equal(1.0, p.QHigher);          // 1 von 1 Mannschaftskampf in der 2. Bundesliga
        Assert.Equal(0, p.NewPrev);
        Assert.Equal(1, rows.Single(r => r.Pid == "N").NewEver);
    }
}
